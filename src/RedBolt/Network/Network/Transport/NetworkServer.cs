// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;

public abstract class NetworkServer : RedObject
{
    protected NetworkServer()
    {
        // Cached here because a dispatch can run on a network thread. The subsystem can be missing until first use.
        _workQueue = Context.GetSubsystem<WorkQueue>();
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
        RunTaskOnMainThread(() => AddConnection(connection));
        return connection;
    }

    public event Action<NetworkServer, NetworkConnection>? OnConnected;
    public event Action<NetworkServer, NetworkConnection>? OnDisconnected;
    public event Action<NetworkServer>? OnListenStart;
    public event Action<NetworkServer>? OnListenStop;

    protected void NotifyStopping()
    {
        Context.GetSubsystem<Network>()?.OnServerStopping(this);
    }

    protected virtual void HandleConnected(NetworkConnection connection)
    {
        OnConnected?.Invoke(this, connection);
        var eventData = new NetworkEvents.ServerClientConnectedEvent
        {
            Server = this,
            Connection = connection,
            Address = connection.Address,
            Port = connection.Port
        };
        SendEvent(ref eventData);
    }

    protected virtual void HandleDisconnected(NetworkConnection connection)
    {
        OnDisconnected?.Invoke(this, connection);
        var eventData = new NetworkEvents.ServerClientDisconnectedEvent
        {
            Server = this,
            Connection = connection,
            Address = connection.Address,
            Port = connection.Port
        };
        SendEvent(ref eventData);

        RemoveConnection(connection);
    }

    protected virtual void HandleListenStart()
    {
        OnListenStart?.Invoke(this);
        var eventData = new NetworkEvents.ServerListenStartEvent { Server = this };
        SendEvent(ref eventData);
    }

    protected virtual void HandleListenStop()
    {
        OnListenStop?.Invoke(this);
        var eventData = new NetworkEvents.ServerListenStopEvent { Server = this };
        SendEvent(ref eventData);
    }

    protected internal void DispatchConnected(NetworkConnection connection)
    {
        RunTaskOnMainThread(() => HandleConnected(connection));
    }

    protected internal void DispatchDisconnected(NetworkConnection connection)
    {
        RunTaskOnMainThread(() => HandleDisconnected(connection));
    }

    protected void DispatchListenStart()
    {
        RunTaskOnMainThread(HandleListenStart);
    }

    protected void DispatchListenStop()
    {
        RunTaskOnMainThread(HandleListenStop);
    }

    protected void AddConnection(NetworkConnection connection)
    {
        _connections.Add(connection);
    }

    protected void RemoveConnection(NetworkConnection connection)
    {
        _connections.RemoveAll(item => ReferenceEquals(item, connection));
    }

    private WorkQueue GetWorkQueue() => _workQueue ?? Context.GetSubsystem<WorkQueue>()!;

    // Same as upstream WorkQueue::RunTaskOnMainThread: run now on the main thread, otherwise run between frames.
    private void RunTaskOnMainThread(Action task)
    {
        if (WorkQueue.IsMainThread())
            task();
        else
            GetWorkQueue().PostDelayedTaskForMainThread(task);
    }

    private readonly WorkQueue? _workQueue;
    private readonly List<NetworkConnection> _connections = [];
    private Func<NetworkConnection>? _connectionFactory;
}
