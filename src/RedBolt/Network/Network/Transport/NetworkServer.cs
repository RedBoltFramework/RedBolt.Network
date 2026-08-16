// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;

public abstract class NetworkServer : RedObject
{
    protected NetworkServer()
    {
        _workQueue = Context.GetSubsystem<WorkQueue>()!;
    }

    public abstract bool Listen(URL url);
    public abstract void Stop();
    public abstract bool IsListening { get; }

    public IReadOnlyList<NetworkConnection> Connections => _connections;

    public void SetConnectionFactory(Func<NetworkConnection> factory)
    {
        _connectionFactory = factory;
    }

    public NetworkConnection CreateConnection()
    {
        System.Diagnostics.Debug.Assert(_connectionFactory is not null);
        var connection = _connectionFactory!();
        AddConnection(connection);
        return connection;
    }

    public event Action<NetworkServer, NetworkConnection>? Connected;
    public event Action<NetworkServer, NetworkConnection>? Disconnected;
    public event Action<NetworkServer>? ListenStarted;
    public event Action<NetworkServer>? ListenStopped;

    protected void NotifyStopping()
    {
        Context.GetSubsystem<Network>()?.OnServerStopping(this);
    }

    protected virtual void OnConnected(NetworkConnection connection)
    {
        Connected?.Invoke(this, connection);
        var eventData = new NetworkEvents.ServerClientConnectedEvent
        {
            Server = this,
            Connection = connection,
            Address = connection.Address,
            Port = connection.Port
        };
        SendEvent(ref eventData);
    }

    protected virtual void OnDisconnected(NetworkConnection connection)
    {
        Disconnected?.Invoke(this, connection);
        var eventData = new NetworkEvents.ServerClientDisconnectedEvent
        {
            Server = this,
            Connection = connection,
            Address = connection.Address,
            Port = connection.Port
        };
        SendEvent(ref eventData);
    }

    protected virtual void OnListenStart()
    {
        ListenStarted?.Invoke(this);
        var eventData = new NetworkEvents.ServerListenStartEvent { Server = this };
        SendEvent(ref eventData);
    }

    protected virtual void OnListenStop()
    {
        ListenStopped?.Invoke(this);
        var eventData = new NetworkEvents.ServerListenStopEvent { Server = this };
        SendEvent(ref eventData);
    }

    protected internal void DoOnConnected(NetworkConnection connection)
    {
        _workQueue.PostTaskForMainThread(() => OnConnected(connection));
    }

    protected internal void DoOnDisconnected(NetworkConnection connection)
    {
        _workQueue.PostTaskForMainThread(() =>
        {
            OnDisconnected(connection);
            RemoveConnection(connection);
        });
    }

    protected void DoOnListenStart()
    {
        _workQueue.PostTaskForMainThread(OnListenStart);
    }

    protected void DoOnListenStop()
    {
        _workQueue.PostTaskForMainThread(OnListenStop);
    }

    protected void AddConnection(NetworkConnection connection)
    {
        _workQueue.PostTaskForMainThread(() => _connections.Add(connection));
    }

    protected void RemoveConnection(NetworkConnection connection)
    {
        _workQueue.PostTaskForMainThread(() => _connections.RemoveAll(item => ReferenceEquals(item, connection)));
    }

    private readonly WorkQueue _workQueue;
    private readonly List<NetworkConnection> _connections = [];
    private Func<NetworkConnection>? _connectionFactory;
}
