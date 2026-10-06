// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;
using RedBolt.IO;
using RedBolt.Replica;
using RedBolt.Resource;
using RedBolt.Scene;

public delegate void PrepareLoadSceneRequestHandler(
    NetworkLoadableScene sender, string sceneFileName, VectorBuffer request);
public delegate void LoadSceneRequestReceivedHandler(
    NetworkLoadableScene sender, string sceneFileName, ReadOnlySpan<byte> request, ref bool accept);
public delegate void LoadSceneStartedHandler(NetworkLoadableScene sender, string sceneFileName);
public delegate void LoadSceneFinishedHandler(
    NetworkLoadableScene sender, string sceneFileName, ref bool success, VectorBuffer response);
public delegate void RemoteLoadSceneFinishedHandler(
    NetworkLoadableScene sender, string sceneFileName, bool success, ReadOnlySpan<byte> response);

/// <summary>Helper class for network-driven scene loading.</summary>
public class NetworkLoadableScene : RedObject
{
    public NetworkLoadableScene()
    {
    }

    public NetworkLoadableScene(NetworkConnection connection, ReplicatedPeer? replicationConnection = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Attach(connection, replicationConnection);
    }

    public event PrepareLoadSceneRequestHandler? PrepareLoadSceneRequest;
    public event LoadSceneRequestReceivedHandler? LoadSceneRequestReceived;
    public event LoadSceneStartedHandler? LoadSceneStarted;
    public event LoadSceneFinishedHandler? LoadSceneFinished;
    public event RemoteLoadSceneFinishedHandler? RemoteLoadSceneFinished;

    public NetworkConnection? Connection => _connection;
    public Scene? Scene => _scene;
    public bool IsLoadingScene => _loadingSceneName.Length != 0;
    public string LoadingSceneName => _loadingSceneName;
    public bool IsSwitchInProgress => _loadState != LoadState.Idle;

    public void Attach(NetworkConnection? connection, ReplicatedPeer? replicationConnection = null)
    {
        if (ReferenceEquals(_connection, connection))
        {
            _replicationConnection = replicationConnection;
            return;
        }

        if (_connection is not null)
        {
            _connection.OnMessageReceived -= ProcessNetworkMessage;
            _connection.OnConnected -= HandleConnectionConnected;
            _connection.OnDisconnected -= HandleConnectionDisconnected;
        }

        _connection = connection;
        _replicationConnection = replicationConnection;
        if (connection is null)
            return;

        connection.OnMessageReceived += ProcessNetworkMessage;
        connection.OnConnected += HandleConnectionConnected;
        connection.OnDisconnected += HandleConnectionDisconnected;
    }

    public void SetScene(Scene? scene)
    {
        if (_loadState != LoadState.Idle)
        {
            HandleProtocolError("SetScene was called while previous scene-loading action is still in progress");
            return;
        }

        _scene = scene;
        _loadingSceneName = string.Empty;
        _expectedSceneName = string.Empty;
        if (IsServerSideConnection())
            RequestRemoteLoadScene();
    }

    public override void Dispose()
    {
        Attach(null);
        base.Dispose();
    }

    protected virtual void OnPrepareLoadSceneRequest(string sceneFileName, VectorBuffer request) =>
        PrepareLoadSceneRequest?.Invoke(this, sceneFileName, request);

    protected virtual void OnLoadSceneRequestReceived(
        string sceneFileName, ReadOnlySpan<byte> request, ref bool accept) =>
        LoadSceneRequestReceived?.Invoke(this, sceneFileName, request, ref accept);

    protected virtual void OnLoadSceneStarted(string sceneFileName) =>
        LoadSceneStarted?.Invoke(this, sceneFileName);

    protected virtual void OnLoadSceneFinished(
        string sceneFileName, ref bool success, VectorBuffer response) =>
        LoadSceneFinished?.Invoke(this, sceneFileName, ref success, response);

    protected virtual void OnRemoteLoadSceneFinished(
        string sceneFileName, bool success, ReadOnlySpan<byte> response) =>
        RemoteLoadSceneFinished?.Invoke(this, sceneFileName, success, response);

    // Read the scene file name from the start of the payload. The remaining payload starts at "consumed".
    private static unsafe string ReadSceneFileName(ReadOnlySpan<byte> message, out int consumed)
    {
        fixed (byte* pointer = message)
        {
            using var reader = new MemoryBuffer((nint)pointer, (uint)message.Length, readOnly: true);
            var sceneFileName = reader.ReadString();
            consumed = checked((int)reader.Position);
            return sceneFileName;
        }
    }

    private bool IsServerSideConnection() => _connection?.Server is not null;

    private void HandleProtocolError(string reason)
    {
        if (_connection is null)
            return;
        Logging.Log.GetLogger().Error("NetworkLoadableScene protocol error: {}", reason);
        _connection.Disconnect();
    }

    private void RequestRemoteLoadScene()
    {
        if (_connection is null || _scene is null || !_connection.IsConnected
            || !IsServerSideConnection())
            return;

        if (_loadState != LoadState.Idle)
        {
            HandleProtocolError("cannot send MSG_LOAD_SCENE while state is not idle");
            return;
        }

        var sceneFileName = _scene.GetFileName();
        using var request = new VectorBuffer();
        request.WriteString(sceneFileName);
        OnPrepareLoadSceneRequest(sceneFileName, request);

        _expectedSceneName = sceneFileName;
        _loadState = LoadState.AwaitingRemoteLoadResult;
        _connection.SendMessage(NetworkMessageId.LoadScene, request.Buffer, PacketType.ReliableOrdered);
    }

    private void ProcessNetworkMessage(NetworkConnection connection, NetworkMessageId messageId,
        ReadOnlySpan<byte> message, ref bool handled)
    {
        if (handled || !ReferenceEquals(connection, _connection))
            return;

        switch (messageId)
        {
            case NetworkMessageId.LoadScene:
                ProcessLoadSceneRequest(message);
                handled = true;
                break;
            case NetworkMessageId.SceneLoadResult:
                ProcessLoadSceneResult(message);
                handled = true;
                break;
        }
    }

    private void ProcessLoadSceneRequest(ReadOnlySpan<byte> message)
    {
        if (_connection is null)
            return;
        if (IsServerSideConnection())
        {
            HandleProtocolError("server received unexpected MSG_LOAD_SCENE");
            return;
        }
        if (_loadState != LoadState.Idle)
        {
            HandleProtocolError("client received MSG_LOAD_SCENE while another scene load is active");
            return;
        }

        var sceneFileName = ReadSceneFileName(message, out var consumed);
        var accept = true;
        OnLoadSceneRequestReceived(sceneFileName, message[consumed..], ref accept);
        if (!accept)
        {
            SendLocalLoadResult(sceneFileName, false);
            return;
        }

        if (!StartLocalLoad(sceneFileName))
            FinishLocalLoad(false);
    }

    private void ProcessLoadSceneResult(ReadOnlySpan<byte> message)
    {
        if (_connection is null)
            return;
        if (!IsServerSideConnection())
        {
            HandleProtocolError("client received unexpected MSG_SCENE_LOAD_RESULT");
            return;
        }
        if (_loadState != LoadState.AwaitingRemoteLoadResult)
        {
            HandleProtocolError("server received MSG_SCENE_LOAD_RESULT without pending MSG_LOAD_SCENE request");
            return;
        }

        var sceneFileName = ReadSceneFileName(message, out var consumed);
        if (message.Length - consumed < sizeof(byte))
        {
            HandleProtocolError("MSG_SCENE_LOAD_RESULT payload is too short: missing success flag");
            return;
        }
        var success = message[consumed++] != 0;
        if (sceneFileName != _expectedSceneName)
        {
            HandleProtocolError("MSG_SCENE_LOAD_RESULT scene name does not match active request");
            return;
        }

        _loadState = LoadState.Idle;
        _expectedSceneName = string.Empty;
        if (success)
            AssignConnectionReplicationManager(_replicationConnection, _scene);
        OnRemoteLoadSceneFinished(sceneFileName, success, message[consumed..]);
    }

    private bool StartLocalLoad(string sceneFileName)
    {
        _scene ??= new Scene();
        _scene.StopAsyncLoading();
        _loadState = LoadState.LoadingLocalScene;
        _loadingSceneName = sceneFileName;
        OnLoadSceneStarted(sceneFileName);

        var cache = GetSubsystem<ResourceCache>();
        if (cache is null)
            return false;

        var extension = Path.GetExtension(sceneFileName).ToLowerInvariant();
        var success = sceneFileName.Length == 0;
        if (sceneFileName.Length != 0)
        {
            if (extension == ".xml")
            {
                var resource = cache.GetResourceOrNull<XMLFile>(sceneFileName);
                success = resource is not null && _scene.LoadXML(resource.GetRoot());
            }
            else if (extension == ".json")
            {
                var resource = cache.GetResourceOrNull<JSONFile>(sceneFileName);
                success = resource is not null && _scene.LoadJSON(resource.Root);
            }
            else
            {
                var resource = cache.GetResourceOrNull<BinaryFile>(sceneFileName);
                if (resource is not null)
                {
                    var source = resource.AsDeserializer();
                    source.Seek(0);
                    success = _scene.Load(source);
                }
            }
        }

        if (!success)
            return false;
        FinishLocalLoad(true);
        return true;
    }

    private void FinishLocalLoad(bool success)
    {
        if (_loadState != LoadState.LoadingLocalScene)
            return;
        SendLocalLoadResult(_loadingSceneName, success);
        _loadingSceneName = string.Empty;
        _loadState = LoadState.Idle;
    }

    private void SendLocalLoadResult(string sceneFileName, bool success)
    {
        if (_connection is null)
            return;
        if (IsServerSideConnection())
        {
            HandleProtocolError("server attempted to send MSG_SCENE_LOAD_RESULT");
            return;
        }

        using var response = new VectorBuffer();
        var effectiveSuccess = success;
        OnLoadSceneFinished(sceneFileName, ref effectiveSuccess, response);
        if (effectiveSuccess)
            AssignConnectionReplicationManager(_replicationConnection, _scene);

        using var messageData = new VectorBuffer();
        messageData.WriteString(sceneFileName);
        messageData.WriteBool(effectiveSuccess);
        if (response.Size != 0)
            messageData.Write(response.Buffer);
        _connection.SendMessage(NetworkMessageId.SceneLoadResult,
            messageData.Buffer, PacketType.ReliableOrdered);
    }

    private void HandleConnectionConnected(NetworkConnection connection)
    {
        if (ReferenceEquals(connection, _connection) && IsServerSideConnection())
            RequestRemoteLoadScene();
    }

    private void HandleConnectionDisconnected(NetworkConnection connection)
    {
        if (!ReferenceEquals(connection, _connection))
            return;
        _loadingSceneName = string.Empty;
        _loadState = LoadState.Idle;
        _expectedSceneName = string.Empty;
    }

    private static void AssignConnectionReplicationManager(ReplicatedPeer? replicatedPeer, Scene? scene)
    {
        replicatedPeer?.SetReplicationManager(scene?.GetComponent<ReplicationManager>());
    }

    private enum LoadState
    {
        Idle,
        AwaitingRemoteLoadResult,
        LoadingLocalScene
    }

    private NetworkConnection? _connection;
    private ReplicatedPeer? _replicationConnection;
    private Scene? _scene;
    private LoadState _loadState;
    private string _expectedSceneName = string.Empty;
    private string _loadingSceneName = string.Empty;
}
