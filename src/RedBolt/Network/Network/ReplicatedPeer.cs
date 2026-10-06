// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Container;
using RedBolt.Core;
using RedBolt.IO;
using RedBolt.Replica;

/// <summary>Settings of the clock synchronization of a ReplicatedPeer.</summary>
public class ReplicatedPeerSettings
{
    public uint PingIntervalMs { get; init; } = 250;
    public uint MaxPingMs { get; init; } = 10000;
    public uint ClockBufferSize { get; init; } = 40;
    public uint PingBufferSize { get; init; } = 10;

    /// <summary>Return the local timestamp in milliseconds. If null, use Time.GetSystemTime.</summary>
    public Func<uint>? GetTimestamp { get; init; }
}

/// <summary>Replication-oriented interface attached to a transport connection.</summary>
public class ReplicatedPeer : IDisposable
{
    public ReplicatedPeer(NetworkConnection connection, ReplicatedPeerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        settings ??= new ReplicatedPeerSettings();
        _connection = connection;
        _clock = new ClockSynchronizer(settings.PingIntervalMs, settings.MaxPingMs,
            settings.ClockBufferSize, settings.PingBufferSize, settings.GetTimestamp);
        _listener = new Listener(this);

        Logging.Log.GetLogger().Info("Replicated peer #{} is created for {} connection \"{}\"", ObjectId,
            connection.Server is not null ? "server" : "client", connection.Address);
    }

    public uint ObjectId => _idFamily.GetObjectID();
    public NetworkConnection Connection => _connection;

    public void SetReplicationManager(ReplicationManager? replicationManager)
    {
        if (ReferenceEquals(_replicationManager, replicationManager))
            return;

        if (_replicationManager is not null && _connection.IsConnected)
            OnDisconnected();

        _replicationManager = replicationManager;

        if (_replicationManager is not null && _connection.IsConnected)
            OnConnected();
    }

    public ReplicationManager? ReplicationManager => _replicationManager;

    public override string ToString()
    {
        if (_connection.IsDisconnected)
            return $"#{ObjectId} <disconnected>";
        return $"#{ObjectId} {_connection.Address}:{_connection.Port}";
    }

    public virtual bool IsClockSynchronized => _clock.IsReady;
    public virtual uint RemoteToLocalTime(uint time) => _clock.RemoteToLocal(time);
    public virtual uint LocalToRemoteTime(uint time) => _clock.LocalToRemote(time);
    public virtual uint LocalTime => Time.GetSystemTime();
    public virtual uint LocalTimeOfLatestRoundtrip => _clock.LocalTimeOfLatestRoundtrip;
    public virtual uint Ping => _clock.Ping;
    public ClockSynchronizer Clock => _clock;

    public virtual void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        SetReplicationManager(null);
        _listener.Dispose();
        Logging.Log.GetLogger().Info("Replicated peer #{} is destroyed", ObjectId);
        _idFamily.Dispose();
        GC.SuppressFinalize(this);
    }

    private unsafe void OnMessageReceived(
        NetworkMessageId messageId, ReadOnlySpan<byte> message, ref bool handled)
    {
        if (handled)
            return;

        // The replication code reads the message as a stream. The span is valid during this call.
        fixed (byte* pointer = message)
        {
            using var messageReader = new MemoryBuffer((nint)pointer, (uint)message.Length, readOnly: true);
            ProcessReplicationMessage(messageId, messageReader, ref handled);
        }
    }

    private void ProcessReplicationMessage(NetworkMessageId messageId, MemoryBuffer message, ref bool handled)
    {
        if (messageId == NetworkMessageId.ClockSync)
        {
            var clockMessage = NetworkMessageUtils.ReadSerializedMessage<ClockSynchronizerMessage>(message);
            Clock.ProcessMessage(clockMessage);
            handled = true;
        }

        if (_replicationManager is not null)
            handled |= _replicationManager.ProcessMessage(this, messageId, message);
    }

    private void SendReplicationMessages()
    {
        if (!_connection.IsConnected)
            return;

        while (Clock.PollMessage() is { } clockMessage)
        {
            NetworkMessageUtils.WriteSerializedMessage(_connection, NetworkMessageId.ClockSync,
                clockMessage, PacketType.UnreliableUnordered);
        }
    }

    private void OnConnected()
    {
        if (_replicationManager is null)
            return;

        if (_replicationManager.IsServer)
        {
            Logging.Log.GetLogger().Info("Replicated peer #{} is attached to server-side scene", ObjectId);
            _replicationManager.ServerReplicator!.AddConnection(this);
        }
        else
        {
            Logging.Log.GetLogger().Info("Replicated peer #{} is attached to client-side scene", ObjectId);
            _replicationManager.StartClient(this);
        }
    }

    private void OnDisconnected()
    {
        if (_replicationManager is null)
            return;

        _replicationManager.DropConnection(this);
        Logging.Log.GetLogger().Info("Replicated peer #{} is detached from the scene", ObjectId);
    }

    private sealed class Listener : RedObject
    {
        public Listener(ReplicatedPeer owner)
        {
            _owner = owner;
            owner._connection.OnMessageReceived += HandleMessageReceived;
            owner._connection.OnConnected += HandleConnected;
            owner._connection.OnDisconnected += HandleDisconnected;
            SubscribeToEvent<CoreEvents.PostUpdateEvent>(HandlePostUpdate);
        }

        public override void Dispose()
        {
            _owner._connection.OnMessageReceived -= HandleMessageReceived;
            _owner._connection.OnConnected -= HandleConnected;
            _owner._connection.OnDisconnected -= HandleDisconnected;
            base.Dispose();
        }

        private void HandlePostUpdate(ref CoreEvents.PostUpdateEvent eventData) =>
            _owner.SendReplicationMessages();

        private void HandleMessageReceived(NetworkConnection connection, NetworkMessageId messageId,
            ReadOnlySpan<byte> message, ref bool handled) =>
            _owner.OnMessageReceived(messageId, message, ref handled);

        private void HandleConnected(NetworkConnection connection) => _owner.OnConnected();
        private void HandleDisconnected(NetworkConnection connection) => _owner.OnDisconnected();

        private readonly ReplicatedPeer _owner;
    }

    private readonly NetworkConnection _connection;
    private readonly ClockSynchronizer _clock;
    private readonly Listener _listener;
    private readonly IDFamily<ReplicatedPeer> _idFamily = new();
    private ReplicationManager? _replicationManager;
    private bool _disposed;
}
