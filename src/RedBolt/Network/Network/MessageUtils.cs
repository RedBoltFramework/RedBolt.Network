// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using System.Diagnostics;
using System.Text;

using RedBolt.IO;

public interface INetworkSerializableMessage
{
    void Load(MemoryBuffer source);
    void Save(VectorBuffer destination);
}

/// <summary>Helper to send a large message as multiple small messages.</summary>
public sealed class LargeMessageWriter : IDisposable
{
    public LargeMessageWriter(NetworkConnection connection, VectorBuffer buffer, StringBuilder debugInfo,
        NetworkMessageId incompleteMessageId, NetworkMessageId lastMessageId)
    {
        _connection = connection;
        _buffer = buffer;
        _debugInfo = debugInfo;
        _incompleteMessageId = incompleteMessageId;
        _lastMessageId = lastMessageId;

        _buffer.Clear();
        _debugInfo.Clear();
    }

    public void Discard()
    {
        _discarded = true;
    }

    public VectorBuffer Buffer => _buffer;
    public StringBuilder DebugInfo => _debugInfo;

    public void Dispose()
    {
        // The size is 0 when the connection is not connected: there is nothing to send to
        var maxMessageSize = _connection.MaxPayloadSize;
        if (!_discarded && maxMessageSize != 0)
        {
            var payload = _buffer.Buffer;
            var numChunks = System.Math.Max(1u, (_buffer.Size + maxMessageSize - 1) / maxMessageSize);
            for (var i = 0u; i < numChunks; ++i)
            {
                var isLastChunk = i + 1 == numChunks;
                var messageId = isLastChunk ? _lastMessageId : _incompleteMessageId;
                var chunkOffset = i * maxMessageSize;
                // Upstream rbfx uses "buffer size % maxMessageSize" for the last chunk. That loses the last chunk
                // when the buffer size is an exact multiple of the payload size, because the chunk size becomes 0.
                // Upstream also sends nothing for an empty buffer, but a message with only an id is valid.
                var chunkSize = isLastChunk ? _buffer.Size - chunkOffset : maxMessageSize;
                var chunkData = payload.Slice(checked((int)chunkOffset), checked((int)chunkSize));
                var chunkDebugInfo = isLastChunk ? _debugInfo.ToString() : string.Empty;
                _connection.SendMessage(messageId, chunkData, PacketType.ReliableOrdered, chunkDebugInfo);
            }
        }
        _buffer.Clear();
        GC.SuppressFinalize(this);
    }

    private readonly NetworkConnection _connection;
    private readonly VectorBuffer _buffer;
    private readonly StringBuilder _debugInfo;
    private readonly NetworkMessageId _incompleteMessageId;
    private readonly NetworkMessageId _lastMessageId;
    private bool _discarded;
}

/// <summary>Helper to reassemble large messages.</summary>
public class LargeMessageReader
{
    public LargeMessageReader(VectorBuffer buffer,
        NetworkMessageId incompleteMessageId, NetworkMessageId lastMessageId)
    {
        _buffer = buffer;
        _incompleteMessageId = incompleteMessageId;
        _lastMessageId = lastMessageId;
    }

    /// <summary>
    /// Add a message. The whole data of <paramref name="messageData" /> is appended, regardless of its position,
    /// and the position moves to the end, as upstream rbfx does.
    /// </summary>
    public unsafe void OnMessage(NetworkMessageId messageId, MemoryBuffer messageData,
        Action<MemoryBuffer> onMessageReceived)
    {
        Debug.Assert(messageId == _incompleteMessageId || messageId == _lastMessageId);

        if (messageId == _lastMessageId && _buffer.Size == 0)
        {
            onMessageReceived(messageData);
        }
        else
        {
            _buffer.Seek(_buffer.Size);
            _buffer.Write(new ReadOnlySpan<byte>((void*)messageData.GetData(), checked((int)messageData.Size)));
            messageData.Seek(messageData.Size);
            if (messageId == _lastMessageId)
            {
                using var memoryBuffer = new MemoryBuffer(_buffer);
                onMessageReceived(memoryBuffer);
                _buffer.Clear();
            }
        }
    }

    private readonly VectorBuffer _buffer;
    private readonly NetworkMessageId _incompleteMessageId;
    private readonly NetworkMessageId _lastMessageId;
}

/// <summary>Helper to send multiple messages of one type with a shared header.</summary>
public sealed class MultiMessageWriter : IDisposable
{
    public MultiMessageWriter(NetworkConnection connection, VectorBuffer buffer, StringBuilder debugInfo,
        NetworkMessageId messageId, PacketType packetType)
    {
        _connection = connection;
        _buffer = buffer;
        _debugInfo = debugInfo;
        _messageId = messageId;
        _packetType = packetType;

        _buffer.Clear();
        _debugInfo.Clear();
    }

    public void CompleteHeader()
    {
        if (_headerSize.HasValue)
        {
            Debug.Fail($"Common message header ({_headerSize.Value} bytes) is already completed");
            return;
        }

        _headerSize = _buffer.Size;
        _nextPayloadOffset = _headerSize.Value;
        Debug.Assert(_connection.MaxPayloadSize == 0 || _headerSize.Value <= _connection.MaxPayloadSize);
    }

    public void CompletePayload()
    {
        _headerSize ??= 0;

        if (_buffer.Size > _connection.MaxPayloadSize && _nextPayloadOffset != _headerSize)
        {
            SendPreviousPayloads();

            var data = _buffer.GetBuffer();
            var nextPayloadSize = (uint)data.Length - _nextPayloadOffset;
            data.AsSpan(checked((int)_nextPayloadOffset), checked((int)nextPayloadSize))
                .CopyTo(data.AsSpan(checked((int)_headerSize.Value)));
            _buffer.Resize(_headerSize.Value + nextPayloadSize);
            _debugInfo.Remove(0, checked((int)_nextDebugInfoOffset));
        }

        _nextPayloadOffset = _buffer.Size;
        _nextDebugInfoOffset = (uint)_debugInfo.Length;
    }

    public VectorBuffer Buffer => _buffer;
    public StringBuilder DebugInfo => _debugInfo;

    public void Dispose()
    {
        if (_nextPayloadOffset != _headerSize)
            SendPreviousPayloads();
        _buffer.Clear();
        _debugInfo.Clear();
        GC.SuppressFinalize(this);
    }

    private void SendPreviousPayloads()
    {
        var payload = _buffer.Buffer[..checked((int)_nextPayloadOffset)];
        var debugInfo = _debugInfo.ToString(0, checked((int)_nextDebugInfoOffset));
        _connection.SendMessage(_messageId, payload, _packetType, debugInfo);
    }

    private readonly NetworkConnection _connection;
    private readonly VectorBuffer _buffer;
    private readonly StringBuilder _debugInfo;
    private readonly NetworkMessageId _messageId;
    private readonly PacketType _packetType;

    private uint? _headerSize;
    private uint _nextPayloadOffset;
    private uint _nextDebugInfoOffset;
}

public static class NetworkMessageUtils
{
    public static T ReadSerializedMessage<T>(MemoryBuffer source)
        where T : struct, INetworkSerializableMessage
    {
        var message = new T();
        message.Load(source);
        return message;
    }

    public static void WriteSerializedMessage<T>(NetworkConnection connection,
        NetworkMessageId messageId, T message,
        PacketType messageType = PacketType.ReliableOrdered)
        where T : struct, INetworkSerializableMessage
    {
        var buffer = t_buffer ??= new VectorBuffer();
        buffer.Clear();
        message.Save(buffer);

        connection.SendMessage(messageId, buffer.Buffer, messageType, message.ToString() ?? string.Empty);
    }

    [ThreadStatic]
    private static VectorBuffer? t_buffer;
}
