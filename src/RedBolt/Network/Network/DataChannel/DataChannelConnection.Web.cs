// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

#if REDBOLT_PLATFORM_BROWSER
namespace RedBolt.Network;

using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

using RedBolt.Core;

[Reflected(ObjectCategory.Category_Network, Factory = false, InheritAttributes = false)]
public partial class DataChannelConnection : NetworkConnection
{
    public DataChannelConnection()
    {
        _id = Interlocked.Increment(ref _nextId);
        lock (Connections)
            Connections[_id] = new WeakReference<DataChannelConnection>(this);
    }

    /// <summary>Has no effect in a browser: the browser WebSocket always checks the certificate.</summary>
    public bool SkipTlsVerification { get; set; }

    public override bool Connect(URL url)
    {
        if (!IsDisconnected)
        {
            RedBolt.Logging.Log.GetLogger().LogError(
                "DataChannelConnection::Connect called while not in Disconnected state.");
            return false;
        }

        if (string.IsNullOrEmpty(url.Scheme))
            url.Scheme = "ws";
        if (string.IsNullOrEmpty(url.Host))
            return false;

        _address = url.Host;
        _port = url.Port;
        _state = NetworkConnectionState.Connecting;
        try
        {
            if (BrowserDataChannelInterop.Connect(
                    _id, url.ToString(), string.Join("\n", _iceServers)))
                return true;
        }
        catch (Exception exception)
        {
            RedBolt.Logging.Log.GetLogger().LogError(exception,
                "Could not start the browser WebRTC connection.");
        }

        _state = NetworkConnectionState.Disconnected;
        return false;
    }

    public override void Disconnect()
    {
        if (_state is NetworkConnectionState.Disconnected or NetworkConnectionState.Disconnecting)
            return;

        base.Disconnect();
        _state = NetworkConnectionState.Disconnecting;
        BrowserDataChannelInterop.Close(_id);
    }

    public override bool SendData(ReadOnlySpan<byte> data,
        PacketType type = PacketType.ReliableOrdered)
    {
        if (_state != NetworkConnectionState.Connected)
        {
            RedBolt.Logging.Log.GetLogger().LogDebug(
                "Network message was not sent: connection is not connected.");
            return false;
        }

        var dataSize = (uint)data.Length;
        if (dataSize > MaxMessageSize)
        {
            RedBolt.Logging.Log.GetLogger().LogError(
                "DataChannel tried to send {DataSize} bytes, which is more than max allowed {MaxSize} bytes of data per message.",
                dataSize, MaxMessageSize);
            return false;
        }

        if (!base.SendData(data, type))
            return false;

        return BrowserDataChannelInterop.Send(
            _id, (int)type, Convert.ToBase64String(data));
    }

    public override uint MaxMessageSize => _state == NetworkConnectionState.Connected
        ? base.MaxMessageSize
        : 0;

    public void SetIceServers(IEnumerable<string> servers)
    {
        _iceServers.Clear();
        _iceServers.AddRange(servers);
    }

    public void SetPortRange(ushort begin, ushort end) { }
    public void SetIceUdpMux(bool enable) { }
    public void SetBindAddress(string address) { }
    public void SetMtu(nuint mtu) { }

    public override void Dispose()
    {
        lock (Connections)
            Connections.Remove(_id);
        _state = NetworkConnectionState.Disconnected;
        BrowserDataChannelInterop.Close(_id);
        base.Dispose();
    }

    internal static void OnBrowserConnected(int id)
    {
        if (GetConnection(id) is { _state: NetworkConnectionState.Connecting } connection)
            connection.DispatchConnected();
    }

    internal static void OnBrowserData(int id, string payload)
    {
        if (GetConnection(id) is not { } connection)
            return;

        // Decode into a pooled buffer. The buffer is valid during the dispatch, which copies the data when needed.
        var buffer = ArrayPool<byte>.Shared.Rent((payload.Length + 3) / 4 * 3);
        try
        {
            if (!Convert.TryFromBase64String(payload, buffer, out var size))
            {
                RedBolt.Logging.Log.GetLogger().LogError(
                    "Browser WebRTC received an invalid payload.");
                return;
            }

            connection.DispatchDataReceived(buffer.AsSpan(0, size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static void OnBrowserDisconnected(int id, string error)
    {
        if (GetConnection(id) is not { } connection
            || connection._state == NetworkConnectionState.Disconnected)
            return;
        if (!string.IsNullOrEmpty(error))
            RedBolt.Logging.Log.GetLogger().LogError("Browser WebRTC connection failed: {Error}", error);
        connection._state = NetworkConnectionState.Disconnecting;
        connection.DispatchDisconnected();
    }

    private static DataChannelConnection? GetConnection(int id)
    {
        lock (Connections)
            return Connections.TryGetValue(id, out var weak) && weak.TryGetTarget(out var connection)
                ? connection
                : null;
    }

    private static int _nextId;
    private static readonly Dictionary<int, WeakReference<DataChannelConnection>> Connections = [];

    private readonly int _id;
    private readonly List<string> _iceServers = [];
}

internal static partial class BrowserDataChannelInterop
{
    [JSImport("network.connect", "main.js")]
    internal static partial bool Connect(int id, string url, string iceServers);

    [JSImport("network.send", "main.js")]
    internal static partial bool Send(int id, int packetType, string payload);

    [JSImport("network.close", "main.js")]
    internal static partial void Close(int id);

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static void Connected(int id) => DataChannelConnection.OnBrowserConnected(id);

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static void Data(int id, string payload) =>
        DataChannelConnection.OnBrowserData(id, payload);

    [SupportedOSPlatform("browser")]
    [JSExport]
    internal static void Disconnected(int id, string error) =>
        DataChannelConnection.OnBrowserDisconnected(id, error);
}
#endif
