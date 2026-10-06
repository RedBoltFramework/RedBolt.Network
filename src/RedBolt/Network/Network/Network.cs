// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Container;
using RedBolt.Core;

/// <summary>Network subsystem that provides update scheduling and replication events.</summary>
public class Network : RedObject
{
    public Network()
    {
        RegisterNetworkLibrary(Context);
        SubscribeToEvent<CoreEvents.BeginFrameEvent>(HandleBeginFrame);
        SubscribeToEvent<CoreEvents.RenderUpdateEvent>(HandleRenderUpdate);
        SubscribeToEvent<NetworkEvents.ClientDisconnectedEvent>(HandleClientDisconnected);
        SubscribeToEvent<NetworkEvents.ServerClientDisconnectedEvent>(HandleServerClientDisconnected);
        SubscribeToEvent<CoreEvents.ExitDelayQueryEvent>(HandleExitDelayQuery);
    }

    /// <summary>Register Network library objects.</summary>
    public static void RegisterNetworkLibrary(Context context)
    {
        context.RegisterAssembly(typeof(Network).Assembly);
    }

    // The DataChannel types have no object factory, so that the editor does not list them as components.
    // Upstream has no factory for them either. Network creates them directly.

    /// <summary>Create a network connection. An empty type creates a <see cref="DataChannelConnection"/>.</summary>
    /// <returns>The new connection, or null if the type is unknown or is not a <see cref="NetworkConnection"/>.</returns>
    public NetworkConnection? CreateConnection(StringHash type = default)
    {
        if (IsEmptyType(type) || type == typeof(DataChannelConnection).GetTypeId())
            return new DataChannelConnection();

        var created = Context.CreateObject(type);
        if (created is NetworkConnection connection)
            return connection;
        created?.Dispose();
        return null;
    }

    /// <summary>Create a network server. An empty type creates a <see cref="DataChannelServer"/>.</summary>
    /// <returns>The new server, or null if the type is unknown or is not a <see cref="NetworkServer"/>.</returns>
    public NetworkServer? CreateServer(StringHash type = default)
    {
        if (IsEmptyType(type) || type == typeof(DataChannelServer).GetTypeId())
            return new DataChannelServer();

        var created = Context.CreateObject(type);
        if (created is NetworkServer server)
            return server;
        created?.Dispose();
        return null;
    }

    public void SetUpdateFps(uint fps)
    {
        _updateFps = System.Math.Max(fps, 1);
        _updateInterval = 1.0f / _updateFps;
        _updateAccumulator = 0.0f;
    }

    public uint UpdateFps => _updateFps;
    public float UpdateOvertime => _updateAccumulator;
    public bool IsUpdateNow => _updateNow;
    public bool HasActiveResources => _stoppingServers.Count != 0 || _disconnectingConnections.Count != 0;

    public void Update(float timeStep)
    {
        _updateAccumulator += timeStep;
        _updateNow = _updateAccumulator >= _updateInterval;
        if (_updateNow)
            _updateAccumulator %= _updateInterval;

        var eventData = new NetworkEvents.NetworkInputProcessedEvent { TimeStep = timeStep };
        SendEvent(ref eventData);
    }

    public void PostUpdate(float timeStep)
    {
        if (_updateNow)
            SendNetworkUpdateEvent(true);

        SendNetworkUpdateEvent(false);
    }

    internal void OnConnectionDisconnecting(NetworkConnection connection)
    {
        _disconnectingConnections.Add(connection);
    }

    internal void OnServerStopping(NetworkServer server)
    {
        _stoppingServers.Add(server);
    }

    // A default-initialized hash has the value zero, which differs from StringHash.Empty.
    private static bool IsEmptyType(StringHash type) => type.IsEmpty() || type.Value == 0;

    private void HandleBeginFrame(ref CoreEvents.BeginFrameEvent eventData)
    {
        Update(eventData.TimeStep);
    }

    private void HandleRenderUpdate(ref CoreEvents.RenderUpdateEvent eventData)
    {
        PostUpdate(eventData.TimeStep);
    }

    private void HandleExitDelayQuery(ref CoreEvents.ExitDelayQueryEvent eventData)
    {
        eventData.Delay |= HasActiveResources;
    }

    private void HandleClientDisconnected(ref NetworkEvents.ClientDisconnectedEvent eventData)
    {
        _disconnectingConnections.Remove(eventData.Connection);
    }

    private void HandleServerClientDisconnected(ref NetworkEvents.ServerClientDisconnectedEvent eventData)
    {
        _disconnectingConnections.Remove(eventData.Connection);
        if (eventData.Server.Connections.Count <= 1)
            _stoppingServers.Remove(eventData.Server);
    }

    private void SendNetworkUpdateEvent(bool isServer)
    {
        var eventData = new NetworkEvents.NetworkUpdateEvent { IsServer = isServer };
        SendEvent(ref eventData);
    }

    private uint _updateFps = 30;
    private float _updateInterval = 1.0f / 30;
    private float _updateAccumulator;
    private bool _updateNow;

    private readonly HashSet<NetworkServer> _stoppingServers = [];
    private readonly HashSet<NetworkConnection> _disconnectingConnections = [];
}
