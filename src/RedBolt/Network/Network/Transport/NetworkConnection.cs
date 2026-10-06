// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;
using RedBolt.IO;

public delegate void NetworkDataHandler(
    NetworkConnection connection, MemoryBuffer data, ref bool handled);
public delegate void NetworkSendDataHandler(
    NetworkConnection connection, MemoryBuffer data, PacketType type, ref bool handled);
public delegate void NetworkMessageHandler(
    NetworkConnection connection, NetworkMessageId messageId, MemoryBuffer data, ref bool handled);
public delegate void NetworkSendMessageHandler(
    NetworkConnection connection, NetworkMessageId messageId, MemoryBuffer data, PacketType type,
    string debugInfo, ref bool handled);

public enum NetworkConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}

public abstract class NetworkConnection : RedObject
{
    public abstract bool Connect(URL url);

    public virtual void Disconnect()
    {
        NotifyDisconnecting();
    }

    public virtual bool SendData(MemoryBuffer data, PacketType type = PacketType.ReliableOrdered)
    {
        var eventData = new NetworkEvents.ClientSendDataEvent
        {
            Connection = this,
            Type = type,
            Data = data
        };
        SendEvent(ref eventData);

        var handled = eventData.Handled;
        if (!handled)
            OnSendData?.Invoke(this, data, type, ref handled);
        return !handled;
    }

    public virtual bool SendMessage(NetworkMessageId messageId, MemoryBuffer data,
        PacketType type = PacketType.ReliableOrdered, string debugInfo = "")
    {
        var eventData = new NetworkEvents.ClientSendMessageEvent
        {
            Connection = this,
            MessageId = messageId,
            Type = type,
            Data = data
        };
        SendEvent(ref eventData);

        var handled = eventData.Handled;
        if (!handled)
            OnSendMessage?.Invoke(this, messageId, data, type, debugInfo, ref handled);
        if (handled)
            return false;

        _outgoing.WriteVLE((uint)messageId);
        CopyAll(data, _outgoing);
        var sent = SendData(new MemoryBuffer(_outgoing), type);
        _outgoing.Clear();
        return sent;
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

    public virtual void HandleConnected()
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

    public virtual void HandleDisconnected()
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

    public virtual bool HandleDataReceived(MemoryBuffer message)
    {
        var messageId = (NetworkMessageId)message.ReadVLE();
        var eventData = new NetworkEvents.ClientMessageEvent
        {
            Connection = this,
            MessageId = messageId,
            Data = message
        };
        SendEvent(ref eventData);

        var handled = eventData.Handled;
        if (!handled)
            OnMessageReceived?.Invoke(this, messageId, message, ref handled);
        return handled || HandleMessageReceived(messageId, message);
    }

    public virtual bool HandleMessageReceived(NetworkMessageId messageId, MemoryBuffer message) => false;

    protected void NotifyDisconnecting()
    {
        Context.GetSubsystem<Network>()?.OnConnectionDisconnecting(this);
    }

    protected void SetServer(NetworkServer? server)
    {
        _server = server;
    }

    protected void DispatchConnected()
    {
        var workQueue = Context.GetSubsystem<WorkQueue>()!;
        workQueue.PostTaskForMainThread(() =>
        {
            _state = NetworkConnectionState.Connected;
            HandleConnected();
        });
    }

    protected void DispatchDisconnected()
    {
        var workQueue = Context.GetSubsystem<WorkQueue>()!;
        workQueue.PostTaskForMainThread(() =>
        {
            _state = NetworkConnectionState.Disconnected;
            HandleDisconnected();
        });
    }

    protected void DispatchDataReceived(MemoryBuffer message)
    {
        if (!ProcessDataOnMainThread || WorkQueue.IsMainThread())
        {
            var eventData = new NetworkEvents.ClientDataEvent { Connection = this, Data = message };
            SendEvent(ref eventData);

            var handled = eventData.Handled;
            if (!handled)
                OnDataReceived?.Invoke(this, message, ref handled);
            if (!handled)
                HandleDataReceived(message);
        }
        else
            QueueIncomingPacket(message);
    }

    protected void DispatchMessageReceived(NetworkMessageId messageId, MemoryBuffer message)
    {
        HandleMessageReceived(messageId, message);
    }

    protected NetworkConnectionState _state = NetworkConnectionState.Disconnected;
    protected string _address = string.Empty;
    protected ushort _port;

    private void QueueIncomingPacket(MemoryBuffer message)
    {
        var packet = CopyBuffer(message);
        lock (_mutex)
        {
            _incoming.Add(packet);
            if (_incoming.Count > 1)
                return;
        }
        Context.GetSubsystem<WorkQueue>()!.PostDelayedTaskForMainThread(ProcessQueuedPackets);
    }

    private void ProcessQueuedPackets()
    {
        lock (_mutex)
        {
            (_incoming, _incomingBack) = (_incomingBack, _incoming);
        }

        foreach (var packet in _incomingBack)
        {
            using var message = new MemoryBuffer(packet);
            DispatchDataReceived(message);
        }

        lock (_mutex)
        {
            foreach (var packet in _incomingBack)
            {
                packet.Clear();
                _buffers.Add(packet);
            }
            _incomingBack.Clear();
        }
    }

    private VectorBuffer CopyBuffer(MemoryBuffer message)
    {
        VectorBuffer packet;
        lock (_mutex)
        {
            if (_buffers.Count != 0)
            {
                packet = _buffers[^1];
                _buffers.RemoveAt(_buffers.Count - 1);
            }
            else
                packet = new VectorBuffer();
        }

        var oldPosition = message.Position;
        message.Seek(0);
        var data = new byte[checked((int)message.Size)];
        message.Read(data);
        message.Seek(oldPosition);
        packet.SetData(data);
        return packet;
    }

    private static void CopyAll(MemoryBuffer source, VectorBuffer destination)
    {
        if (source.Size == 0)
            return;

        var oldPosition = source.Position;
        source.Seek(0);
        var data = new byte[checked((int)source.Size)];
        source.Read(data);
        source.Seek(oldPosition);
        destination.Write(data);
    }

    private readonly VectorBuffer _outgoing = new();
    private NetworkServer? _server;

    private readonly object _mutex = new();
    private List<VectorBuffer> _incoming = [];
    private List<VectorBuffer> _incomingBack = [];
    private readonly List<VectorBuffer> _buffers = [];
}
