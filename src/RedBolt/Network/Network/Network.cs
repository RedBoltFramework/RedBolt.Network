// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

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
    }

    /// <summary>Register Network library objects.</summary>
    public static void RegisterNetworkLibrary(Context context)
    {
        context.RegisterAssembly(typeof(Network).Assembly);
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
        {
            SendNetworkUpdateEvent(true);
            SendNetworkUpdateSentEvent(true);
        }

        SendNetworkUpdateEvent(false);
        SendNetworkUpdateSentEvent(false);
    }

    internal void OnConnectionDisconnecting(NetworkConnection connection)
    {
        _disconnectingConnections.Add(connection);
    }

    internal void OnServerStopping(NetworkServer server)
    {
        _stoppingServers.Add(server);
    }

    private void HandleBeginFrame(ref CoreEvents.BeginFrameEvent eventData)
    {
        Update(eventData.TimeStep);
    }

    private void HandleRenderUpdate(ref CoreEvents.RenderUpdateEvent eventData)
    {
        PostUpdate(eventData.TimeStep);
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

    private void SendNetworkUpdateSentEvent(bool isServer)
    {
        var eventData = new NetworkEvents.NetworkUpdateSentEvent { IsServer = isServer };
        SendEvent(ref eventData);
    }

    private uint _updateFps = 30;
    private float _updateInterval = 1.0f / 30;
    private float _updateAccumulator;
    private bool _updateNow;

    private readonly HashSet<NetworkServer> _stoppingServers = [];
    private readonly HashSet<NetworkConnection> _disconnectingConnections = [];
}
