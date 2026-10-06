// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;
using RedBolt.IO;
using RedBolt.Math;
using RedBolt.Replica;
using RedBolt.Scene;

/// <summary>
/// Server-side scene reloader. Dispose the loader before the connection and the peer.
/// </summary>
public class ServerSceneLoader : RedObject
{
    public ServerSceneLoader(NetworkConnection connection, ReplicatedPeer replicatedPeer)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(replicatedPeer);

        _connection = connection;
        _replicatedPeer = replicatedPeer;

        connection.OnConnected += OnClientConnected;
        connection.OnDisconnected += OnClientDisconnected;
        connection.OnMessageReceived += OnMessageReceived;
    }

    /// <summary>Set scene to be (re)loaded on the client side.</summary>
    public void SetScene(Scene? scene, StringVariantMap? args = null)
    {
        _scene = scene;
        ReplaceArgs(args);
        _pendingRequestMagic = null;

        if (_connection is not null && _connection.IsConnected)
            BeginSceneLoad();
    }

    public override void Dispose()
    {
        var connection = _connection;
        if (connection is not null)
        {
            connection.OnConnected -= OnClientConnected;
            connection.OnDisconnected -= OnClientDisconnected;
            connection.OnMessageReceived -= OnMessageReceived;
        }
        _connection = null;
        _args.Dispose();
        base.Dispose();
    }

    private void ReplaceArgs(StringVariantMap? args)
    {
        var copy = args is null ? new StringVariantMap() : new StringVariantMap(args);
        _args.Dispose();
        _args = copy;
    }

    private void BeginSceneLoad()
    {
        if (_scene is null || _connection is null)
            return;

        var magic = RandomEngine.GetDefaultEngine().GetUInt();
        _pendingRequestMagic = magic;
        _replicatedPeer.SetReplicationManager(null);

        using var message = new VectorBuffer();
        message.WriteUInt(magic);
        message.WriteStringVariantMap(_args);
        _connection.SendMessage(NetworkMessageId.LoadScene, message.Buffer, PacketType.ReliableOrdered);

        Logging.Log.GetLogger().Info("Server requested scene load for replicated peer #{} with parameters {}",
            _replicatedPeer.ObjectId, SceneLoaderUtils.FormatArgs(_args));
    }

    private void OnClientConnected(NetworkConnection connection)
    {
        BeginSceneLoad();
    }

    private void OnClientDisconnected(NetworkConnection connection)
    {
        _pendingRequestMagic = null;
    }

    private void OnMessageReceived(NetworkConnection connection, NetworkMessageId messageId,
        ReadOnlySpan<byte> message, ref bool handled)
    {
        if (_scene is null)
            return;

        if (!handled && messageId == NetworkMessageId.SceneLoadResult)
        {
            var magic = SceneLoaderUtils.ReadUInt(message);
            if (magic == _pendingRequestMagic)
            {
                Logging.Log.GetLogger().Info("Replicated peer #{} completed scene load on server side",
                    _replicatedPeer.ObjectId);
                _pendingRequestMagic = null;
                _replicatedPeer.SetReplicationManager(_scene.GetComponent<ReplicationManager>());
            }
            else
            {
                Logging.Log.GetLogger().Warning("Replicated peer #{} reported unexpected scene load",
                    _replicatedPeer.ObjectId);
            }
            handled = true;
        }
    }

    private NetworkConnection? _connection;
    private readonly ReplicatedPeer _replicatedPeer;
    private Scene? _scene;
    private StringVariantMap _args = new();
    private uint? _pendingRequestMagic;
}

/// <summary>
/// Client-side scene reloader. Dispose the loader before the connection and the peer.
/// </summary>
public class ClientSceneLoader : RedObject
{
    public ClientSceneLoader(NetworkConnection connection, ReplicatedPeer replicatedPeer)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(replicatedPeer);

        _connection = connection;
        _replicatedPeer = replicatedPeer;

        connection.OnMessageReceived += OnMessageReceived;
    }

    /// <summary>Set scene that receives load requests.</summary>
    public void SetScene(Scene? scene)
    {
        _scene = scene;
    }

    /// <summary>
    /// Set the callback that handles load requests. The callback receives the arguments and a completion action.
    /// The arguments are valid only during the call. Copy them to keep them for a deferred load.
    /// </summary>
    public void SetLoadRequestedCallback(Action<StringVariantMap, Action>? callback)
    {
        _onLoadRequested = callback;
    }

    public override void Dispose()
    {
        var connection = _connection;
        if (connection is not null)
            connection.OnMessageReceived -= OnMessageReceived;
        _connection = null;
        _disposed = true;
        base.Dispose();
    }

    protected virtual void OnLoadRequested(StringVariantMap args, Action onCompleted)
    {
        if (_onLoadRequested is not null)
            _onLoadRequested(args, onCompleted);
        else
            onCompleted();
    }

    private void OnMessageReceived(NetworkConnection connection, NetworkMessageId messageId,
        ReadOnlySpan<byte> message, ref bool handled)
    {
        if (_connection is null)
            return;

        if (!handled && messageId == NetworkMessageId.LoadScene)
        {
            var magic = SceneLoaderUtils.ReadLoadRequest(message, out var args);
            using (args)
            {
                Logging.Log.GetLogger().Info(
                    "Replicated peer #{} begins scene load on client side with parameters {}",
                    _replicatedPeer.ObjectId, SceneLoaderUtils.FormatArgs(args));
                _replicatedPeer.SetReplicationManager(null);

                // The completion action does nothing after Dispose.
                OnLoadRequested(args, () =>
                {
                    if (!_disposed)
                        OnSceneLoaded(magic);
                });
            }
        }
    }

    private void OnSceneLoaded(uint magic)
    {
        if (_connection is null)
            return;

        Logging.Log.GetLogger().Info("Replicated peer #{} completed scene load on client side",
            _replicatedPeer.ObjectId);
        _replicatedPeer.SetReplicationManager(_scene?.GetComponent<ReplicationManager>());

        using var message = new VectorBuffer();
        message.WriteUInt(magic);
        _connection.SendMessage(NetworkMessageId.SceneLoadResult, message.Buffer, PacketType.ReliableOrdered);
    }

    private NetworkConnection? _connection;
    private readonly ReplicatedPeer _replicatedPeer;
    private Scene? _scene;
    private Action<StringVariantMap, Action>? _onLoadRequested;
    private bool _disposed;
}

internal static class SceneLoaderUtils
{
    // Read the magic from the start of the payload.
    public static unsafe uint ReadUInt(ReadOnlySpan<byte> message)
    {
        fixed (byte* pointer = message)
        {
            using var reader = new MemoryBuffer((nint)pointer, (uint)message.Length, readOnly: true);
            return reader.ReadUInt();
        }
    }

    // Read the magic and the argument map from the payload.
    public static unsafe uint ReadLoadRequest(ReadOnlySpan<byte> message, out StringVariantMap args)
    {
        fixed (byte* pointer = message)
        {
            using var reader = new MemoryBuffer((nint)pointer, (uint)message.Length, readOnly: true);
            var magic = reader.ReadUInt();
            args = reader.ReadStringVariantMap();
            return magic;
        }
    }

    public static string FormatArgs(StringVariantMap args) =>
        "{" + string.Join(", ", args.Select(pair => $"\"{pair.Key}\": {pair.Value}")) + "}";
}
