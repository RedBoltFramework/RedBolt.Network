// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using System.Diagnostics;

using RedBolt.Core;
using RedBolt.Math;

/// <summary>Handler of received data. The span is valid only during the call.</summary>
public delegate void NetworkDataHandler(
    NetworkConnection connection, ReadOnlySpan<byte> data, ref bool handled);
/// <summary>
/// Handler of data to send. The span is valid only during the call.
/// A handler must not send on the same connection during the call.
/// </summary>
public delegate void NetworkSendDataHandler(
    NetworkConnection connection, ReadOnlySpan<byte> data, PacketType type, ref bool handled);
/// <summary>Handler of a received message. The span is the message payload and is valid only during the call.</summary>
public delegate void NetworkMessageHandler(
    NetworkConnection connection, NetworkMessageId messageId, ReadOnlySpan<byte> messagePayload,
    ref bool handled);
/// <summary>Handler of a message to send. The span is the message payload and is valid only during the call.</summary>
public delegate void NetworkSendMessageHandler(
    NetworkConnection connection, NetworkMessageId messageId, ReadOnlySpan<byte> messagePayload,
    PacketType type, string debugInfo, ref bool handled);

public enum NetworkConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}

public abstract class NetworkConnection : RedObject
{
    protected NetworkConnection()
    {
        // The work queue is cached here. Network threads must not read the subsystem dictionary of the context.
        _workQueue = Context.GetSubsystem<WorkQueue>();
        _processQueuedPacketsTask = (_, _) => ProcessQueuedPackets();
    }

    public abstract bool Connect(URL url);

    public virtual void Disconnect()
    {
        NotifyDisconnecting();
    }

    /// <summary>
    /// Send data as is via the underlying transport. Data may be copied and sent asynchronously.
    /// An implementation must not send on the same connection during the call, and must copy the span
    /// if it keeps the data.
    /// </summary>
    public virtual unsafe bool SendData(ReadOnlySpan<byte> data, PacketType type = PacketType.ReliableOrdered)
    {
        var eventData = new NetworkEvents.ClientSendDataEvent
        {
            Connection = this,
            Type = type,
            Size = data.Length
        };
        fixed (byte* pointer = data)
        {
            eventData.DataPointer = (nint)pointer;
            SendEvent(ref eventData);
        }

        var handled = eventData.Handled;
        if (!handled)
            OnSendData?.Invoke(this, data, type, ref handled);
        return !handled;
    }

    /// <summary>Send a network message. Data may be copied and sent asynchronously.</summary>
    public virtual unsafe bool SendMessage(NetworkMessageId messageId, ReadOnlySpan<byte> data,
        PacketType type = PacketType.ReliableOrdered, string debugInfo = "")
    {
        var eventData = new NetworkEvents.ClientSendMessageEvent
        {
            Connection = this,
            MessageId = messageId,
            Type = type,
            Size = data.Length
        };
        fixed (byte* pointer = data)
        {
            eventData.DataPointer = (nint)pointer;
            SendEvent(ref eventData);
        }

        var handled = eventData.Handled;
        if (!handled)
            OnSendMessage?.Invoke(this, messageId, data, type, debugInfo, ref handled);
        if (handled)
            return false;

        // The outgoing buffer is reused and grows only when needed, so a message does not allocate.
        // A nested send on the same connection would overwrite the buffer, so it is not allowed.
        Debug.Assert(!_sendInProgress, "Nested send on the same connection from a send handler or SendData");
        _sendInProgress = true;
        try
        {
            Span<byte> header = stackalloc byte[5];
            var headerSize = WriteVle(header, (uint)messageId);
            var size = checked(headerSize + data.Length);
            if (_outgoing.Length < size)
                Array.Resize(ref _outgoing, size);
            header[..headerSize].CopyTo(_outgoing);
            data.CopyTo(_outgoing.AsSpan(headerSize));
            return SendData(_outgoing.AsSpan(0, size), type);
        }
        finally
        {
            _sendInProgress = false;
        }
    }

    /// <summary>Maximum size of a raw network message, including the message header.</summary>
    public virtual uint MaxMessageSize => NetworkProtocol.MaxNetworkPacketSize;

    /// <summary>Maximum size of a network message payload.</summary>
    public uint MaxPayloadSize
    {
        get
        {
            const uint headerSize = NetworkProtocol.NetworkMessageHeaderSize;
            return System.Math.Max(headerSize, MaxMessageSize) - headerSize;
        }
    }

    public string Address => _address;
    public ushort Port => _port;
    public NetworkConnectionState State => _state;
    public bool IsConnected => _state == NetworkConnectionState.Connected;
    public bool IsDisconnected => _state == NetworkConnectionState.Disconnected;
    public bool ProcessDataOnMainThread { get; set; } = true;
    public NetworkServer? Server => _server;

    public event Action<NetworkConnection>? OnConnected;
    public event Action<NetworkConnection>? OnDisconnected;
    public event NetworkDataHandler? OnDataReceived;
    public event NetworkMessageHandler? OnMessageReceived;
    public event NetworkSendDataHandler? OnSendData;
    public event NetworkSendMessageHandler? OnSendMessage;

    protected void NotifyDisconnecting()
    {
        Context.GetSubsystem<Network>()?.OnConnectionDisconnecting(this);
    }

    protected void SetServer(NetworkServer? server)
    {
        _server = server;
    }

    /// <summary>Called once, when the connection is set up and data can be sent and received. Runs on the main thread.</summary>
    protected virtual void HandleConnected()
    {
        OnConnected?.Invoke(this);
        var eventData = new NetworkEvents.ClientConnectedEvent
        {
            Connection = this,
            Address = _address,
            Port = _port
        };
        SendEvent(ref eventData);
    }

    /// <summary>Called once, when the disconnection starts (only if <see cref="HandleConnected" /> was called). Runs on the main thread.</summary>
    protected virtual void HandleDisconnected()
    {
        OnDisconnected?.Invoke(this);
        var eventData = new NetworkEvents.ClientDisconnectedEvent
        {
            Connection = this,
            Address = _address,
            Port = _port
        };
        SendEvent(ref eventData);
    }

    /// <summary>
    /// Called when data is received. May be called from a non-main thread, depending on the configuration.
    /// Return true when the message was processed and other handlers must not get it.
    /// </summary>
    protected virtual unsafe bool HandleDataReceived(ReadOnlySpan<byte> message)
    {
        var messageId = (NetworkMessageId)ReadVle(message, out var headerSize);
        var payload = message[headerSize..];

        var eventData = new NetworkEvents.ClientMessageEvent
        {
            Connection = this,
            MessageId = messageId,
            Size = payload.Length
        };
        fixed (byte* pointer = payload)
        {
            eventData.DataPointer = (nint)pointer;
            SendEvent(ref eventData);
        }

        var handled = eventData.Handled;
        if (!handled)
            OnMessageReceived?.Invoke(this, messageId, payload, ref handled);
        return handled || HandleMessageReceived(messageId, payload);
    }

    /// <summary>
    /// Called when a network message is received. May be called from a non-main thread.
    /// Return true when the message was processed and other handlers must not get it.
    /// </summary>
    protected virtual bool HandleMessageReceived(NetworkMessageId messageId, ReadOnlySpan<byte> message) => false;

    protected void DispatchConnected()
    {
        RunTaskOnMainThread(() =>
        {
            _state = NetworkConnectionState.Connected;
            HandleConnected();
        });
    }

    protected void DispatchDisconnected()
    {
        RunTaskOnMainThread(() =>
        {
            _state = NetworkConnectionState.Disconnected;
            HandleDisconnected();
        });
    }

    /// <summary>
    /// Dispatch received data from the transport. The span is valid only during the call.
    /// When the data must be processed on another thread, the data is copied.
    /// </summary>
    protected unsafe void DispatchDataReceived(ReadOnlySpan<byte> message)
    {
        if (!ProcessDataOnMainThread || WorkQueue.IsMainThread())
        {
            var eventData = new NetworkEvents.ClientDataEvent { Connection = this, Size = message.Length };
            fixed (byte* pointer = message)
            {
                eventData.DataPointer = (nint)pointer;
                SendEvent(ref eventData);
            }

            var handled = eventData.Handled;
            if (!handled)
                OnDataReceived?.Invoke(this, message, ref handled);
            if (!handled)
                HandleDataReceived(message);
        }
        else
            QueueIncomingPacket(message);
    }

    protected void DispatchMessageReceived(NetworkMessageId messageId, ReadOnlySpan<byte> message)
    {
        HandleMessageReceived(messageId, message);
    }

    protected NetworkConnectionState _state = NetworkConnectionState.Disconnected;
    protected string _address = string.Empty;
    protected ushort _port;

    private WorkQueue GetWorkQueue() => _workQueue ?? Context.GetSubsystem<WorkQueue>()!;

    // Same as upstream WorkQueue::RunTaskOnMainThread: run now on the main thread, otherwise run between frames.
    private void RunTaskOnMainThread(Action task)
    {
        if (WorkQueue.IsMainThread())
            task();
        else
            GetWorkQueue().PostDelayedTaskForMainThread(task);
    }

    private void QueueIncomingPacket(ReadOnlySpan<byte> message)
    {
        lock (_mutex)
        {
            IncomingPacket packet;
            if (_buffers.Count != 0)
            {
                packet = _buffers[^1];
                _buffers.RemoveAt(_buffers.Count - 1);
            }
            else
                packet = new IncomingPacket();

            packet.Set(message);
            _incoming.Add(packet);

            // First packet schedules a callback on the main thread. If more packets arrive after that time,
            // they are processed in the same callback to avoid flooding the main thread with tasks.
            if (_incoming.Count > 1)
                return;
        }

        GetWorkQueue().PostTaskForMainThread(_processQueuedPacketsTask);
    }

    private void ProcessQueuedPackets()
    {
        // Move the front buffer to the back buffer.
        lock (_mutex)
        {
            (_incoming, _incomingBack) = (_incomingBack, _incoming);
        }

        // Process the back buffer packets while the network thread queues new packets to the front buffer.
        var processed = 0;
        try
        {
            while (processed < _incomingBack.Count)
            {
                var packet = _incomingBack[processed++];
                DispatchDataReceived(packet.Data.AsSpan(0, packet.Length));
            }
        }
        finally
        {
            // A handler that throws leaves the rest of the packets undispatched. They are recycled too.
            var dropped = _incomingBack.Count - processed;

            // Recycle the back buffer packets.
            lock (_mutex)
            {
                _buffers.AddRange(_incomingBack);
                _incomingBack.Clear();
            }

            if (dropped > 0)
                Logging.Log.GetLogger().Warning("Dropped {} queued packets because a data handler failed", dropped);
        }
    }

    // Write a variable-length encoded unsigned integer, as in Serializer.WriteVLE. Return the number of bytes.
    private static int WriteVle(Span<byte> destination, uint value) =>
        (int)MathDefs.EncodeVariableLength(value, destination);

    // Read a variable-length encoded unsigned integer. Missing bytes are read as zero, as in Deserializer.ReadVLE.
    private static uint ReadVle(ReadOnlySpan<byte> source, out int size)
    {
        var result = 0u;
        var offset = 0u;
        size = 0;
        while (offset < 32 && size < source.Length)
        {
            if (MathDefs.DecodeVariableLength(ref result, ref offset, source[size++]))
                break;
        }
        return result;
    }

    private sealed class IncomingPacket
    {
        public byte[] Data = [];
        public int Length;

        public void Set(ReadOnlySpan<byte> message)
        {
            if (Data.Length < message.Length)
                Data = new byte[message.Length];
            message.CopyTo(Data);
            Length = message.Length;
        }
    }

    private readonly WorkQueue? _workQueue;
    private readonly TaskFunctionDelegate _processQueuedPacketsTask;
    private byte[] _outgoing = new byte[64];
    private bool _sendInProgress;
    private NetworkServer? _server;

    private readonly object _mutex = new();
    private List<IncomingPacket> _incoming = [];
    private List<IncomingPacket> _incomingBack = [];
    private readonly List<IncomingPacket> _buffers = [];
}
