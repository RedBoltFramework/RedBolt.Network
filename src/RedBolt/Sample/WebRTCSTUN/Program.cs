// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.WebRTCSTUN;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using RedBolt.Bindings.DearImGui;
using RedBolt.Bindings.LibDataChannel;
using RedBolt.Container;
using RedBolt.Core;
using RedBolt.DearImGui;
using RedBolt.Engine;
using RedBolt.IO;
using RedBolt.Input;
using RedBolt.Math;
using RedBolt.Network;

using static RedBolt.Bindings.LibDataChannel.LibDataChannel;

internal static class WebRTCSTUNProgram
{
    private static int Main()
    {
        try
        {
            using var application = new WebRTCSTUN();
            return application.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

/// <summary>WebRTC STUN/TURN connectivity test with chat and relay signaling.</summary>
public sealed class WebRTCSTUN : Application
{
    private const ushort DefaultPort = 2345;
    private const ushort DataPort = 2346;
    private const string DefaultRelayUrl = "ws://localhost:9876";
    private const int MaxRetries = 5;
    private const float RetryDelay = 2.0f;
    private const int InputBufferSize = 1024;
    private static readonly NetworkMessageId ChatMessage =
        (NetworkMessageId)((uint)NetworkMessageId.User + 0);
    private static readonly NetworkMessageId SystemMessage =
        (NetworkMessageId)((uint)NetworkMessageId.User + 1);
    private static readonly string[] IceServers =
    [
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302",
        "stun:stun2.l.google.com:19302",
        "stun:stun3.l.google.com:19302",
        "stun:stun4.l.google.com:19302"
    ];

    protected override void Setup(EngineParameters parameters)
    {
        parameters.ApplicationName = "RedBolt.Sample.WebRTCSTUN";
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
        parameters.ResourcePaths = "CoreData;SampleData";
        parameters.ResourcePrefixPaths = ".;..";
#if !REDBOLT_PLATFORM_BROWSER
            parameters.WindowWidth = 1280;
            parameters.WindowHeight = 720;
#endif
        parameters.WindowTitle = "RedBolt WebRTC STUN/TURN Test";
    }

    protected override void Start()
    {
        base.Start();
        Context.RegisterSubsystem(new RedBolt.Network.Network());
        Context.RegisterSubsystem(new DearImGui((ImGuiConfigFlags)Engine.Parameters.DearImGuiFlags));
        _textBuffer = Marshal.AllocHGlobal(InputBufferSize);
        Marshal.WriteByte(_textBuffer, 0, 0);
        for (var i = 0; i < 38; ++i)
            _chatHistory.Add(new CString());

        SubscribeToEvent<DearImGuiEvents.EndRenderingDearImGuiEvent>(
            (ref DearImGuiEvents.EndRenderingDearImGuiEvent _) => Draw());
        SubscribeToEvent<IOEvents.LogMessage>(HandleSampleLogMessage);
        Input.SetMouseMode(MouseMode.Free);
        Input.SetMouseVisible(true);

        Log("=== WebRTC STUN/TURN Test ===");
        Log($"Signaling port: {DefaultPort} (TCP) | Data port: {DataPort} (UDP, muxed)");
        Log("Direct: type IP[:port] -> Connect | Start Server [port]");
        Log($"Relay: room[@host:port] -> Start/Connect Relay (default: {DefaultRelayUrl})");
    }

    protected override void Update(float timeStep)
    {
        if (!_retrying || _retryTimer.GetMSec(false) < RetryDelay * 1000.0f)
            return;

        _retrying = false;
        if (_retryCount < MaxRetries && !_wasEverConnected)
        {
            if (_isRelayMode)
                TryConnectRelay(_retryAddress, _retryRelayUrl);
            else
                TryConnect(_retryAddress);
        }
    }

    protected override void Stop()
    {
        Disconnect();
        foreach (var socket in _relaySockets.ToArray())
            socket.Dispose();
        _relaySockets.Clear();
        _clientConnection?.Dispose();
        _clientConnection = null;
        _server?.Dispose();
        _server = null;
        foreach (var row in _chatHistory)
            row.Dispose();
        _chatHistory.Clear();
        if (_textBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_textBuffer);
            _textBuffer = IntPtr.Zero;
        }
        base.Stop();
    }

    private void Draw()
    {
        Vector2f viewportPosition;
        Vector2f viewportSize;
        uint viewportId;
        var viewport = ImGui.GetMainViewport();
        {
            viewportPosition = viewport.Pos;
            viewportSize = viewport.Size;
            viewportId = viewport.Id;
        }

        ImGui.SetNextWindowPos(viewportPosition);
        ImGui.SetNextWindowSize(viewportSize);
        ImGui.SetNextWindowViewport(viewportId);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarWindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarWindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarWindowPadding, Vector2f.Zero);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColWindowBg, DashboardBackground);
        var open = true;
        if (ImGui.Begin("WebRTC STUN/TURN Test"u8, ref open,
            ImGuiWindowFlags.ImGuiWindowFlagsNoDecoration |
            ImGuiWindowFlags.ImGuiWindowFlagsNoMove |
            ImGuiWindowFlags.ImGuiWindowFlagsNoSavedSettings |
            ImGuiWindowFlags.ImGuiWindowFlagsNoBringToFrontOnFocus))
            DrawDashboard(viewportPosition, viewportSize);
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
    }

    private void DrawDashboard(Vector2f position, Vector2f size)
    {
        var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilledMultiColor(position, position + size,
                ImGui.GetColorU32(DashboardBackground), ImGui.GetColorU32(new Vector4f(0.035f, 0.08f, 0.13f, 1.0f)),
                ImGui.GetColorU32(new Vector4f(0.012f, 0.025f, 0.05f, 1.0f)),
                ImGui.GetColorU32(new Vector4f(0.045f, 0.025f, 0.09f, 1.0f)));

        var margin = size.X < 900.0f ? 14.0f : 28.0f;
        var gap = size.X < 900.0f ? 10.0f : 18.0f;
        var headerHeight = 70.0f;
        var footerHeight = 150.0f;
        var contentWidth = Math.Max(1.0f, size.X - margin * 2.0f);
        var contentHeight = Math.Max(180.0f, size.Y - margin * 2.0f - headerHeight - footerHeight - gap * 2.0f);
        var headerPosition = position + new Vector2f(margin, margin);

        ImGui.SetCursorScreenPos(headerPosition);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("WEBRTC CONNECTIVITY LAB"u8);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Inspect direct, STUN, and relay-assisted data channels."u8);
        ImGui.PopStyleColor();

        var clientConnected = _clientConnection?.IsConnected == true;
        var serverRunning = _server is not null && (_server.IsListening || _relayConnections.Count != 0);
        var canChat = clientConnected || serverRunning && _serverConnections.Count != 0;
        var idle = !clientConnected && !serverRunning;
        DrawStatusBadge(headerPosition + new Vector2f(contentWidth - 175.0f, 2.0f),
            idle ? "READY"u8 : clientConnected ? "CONNECTED"u8 : "LISTENING"u8,
            idle ? AccentAmber : AccentCyan);

        var contentPosition = headerPosition + new Vector2f(0.0f, headerHeight);
        var overviewWidth = Math.Min(355.0f, contentWidth * 0.32f);
        DrawOverviewPanel(contentPosition, new Vector2f(overviewWidth, contentHeight), clientConnected, serverRunning);
        DrawActivityPanel(contentPosition + new Vector2f(overviewWidth + gap, 0.0f),
            new Vector2f(contentWidth - overviewWidth - gap, contentHeight));
        DrawCommandPanel(contentPosition + new Vector2f(0.0f, contentHeight + gap),
            new Vector2f(contentWidth, footerHeight), idle, canChat);
    }

    private void DrawOverviewPanel(Vector2f position, Vector2f size, bool clientConnected, bool serverRunning)
    {
        BeginPanel("##Overview"u8, position, size);
        DrawPanelTitle("CONNECTION"u8, AccentViolet);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Local signaling and ICE state"u8);
        ImGui.PopStyleColor();
        ImGui.Separator();
        DrawStateLine("CLIENT"u8, clientConnected, clientConnected ? "CONNECTED"u8 : "IDLE"u8);
        DrawStateLine("SERVER"u8, serverRunning, serverRunning ? "LISTENING"u8 : "STOPPED"u8);
        DrawStateLine("RELAY"u8, _isRelayMode, _isRelayMode ? "ENABLED"u8 : "DIRECT"u8);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentAmber);
        ImGui.TextUnformatted("ENDPOINTS"u8);
        ImGui.PopStyleColor();
        DrawValueLine("TCP SIGNALING"u8, "2345"u8);
        DrawValueLine("UDP DATA"u8, "2346"u8);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Five public STUN endpoints configured"u8);
        ImGui.PopStyleColor();
        EndPanel();
    }

    private void DrawActivityPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Activity"u8, position, size);
        DrawPanelTitle("SESSION ACTIVITY"u8, AccentCyan);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Signaling, ICE candidates, and chat messages"u8);
        ImGui.PopStyleColor();
        ImGui.Separator();
        if (ImGui.BeginChild("##ActivityRows"u8, ImGui.GetContentRegionAvail()))
        {
            foreach (var row in _chatHistory)
            {
                if (row.ByteLength != 0)
                    ImGui.TextUnformatted(row.ContentBytes);
            }
        }
        ImGui.EndChild();
        EndPanel();
    }

    private void DrawCommandPanel(Vector2f position, Vector2f size, bool idle, bool canChat)
    {
        BeginPanel("##Commands"u8, position, size);
        DrawPanelTitle(canChat ? "MESSAGE"u8 : "CONNECT OR HOST"u8, AccentAmber);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(canChat
            ? "Send a UTF-8 message over the active data channel."u8
            : "Enter IP[:port], port, or room[@relay-url]."u8);
        ImGui.PopStyleColor();
        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputText("##Endpoint"u8, _textBuffer, InputBufferSize);
        if (idle)
        {
            if (ImGui.Button("START SERVER"u8)) StartServer();
            ImGui.SameLine();
            if (ImGui.Button("START RELAY"u8)) StartRelay();
            ImGui.SameLine();
            if (ImGui.Button("CONNECT DIRECT"u8)) Connect();
            ImGui.SameLine();
            if (ImGui.Button("CONNECT RELAY"u8)) ConnectRelay();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.ImGuiColButton, DangerButton);
            if (ImGui.Button("DISCONNECT"u8)) Disconnect();
            ImGui.PopStyleColor();
            if (canChat)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.ImGuiColButton, SendButton);
                if (ImGui.Button("SEND MESSAGE"u8)) Send();
                ImGui.PopStyleColor();
            }
        }
        EndPanel();
    }

    private static void DrawPanelTitle(ReadOnlySpan<byte> title, Vector4f color)
    {
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
    }

    private static void DrawStateLine(ReadOnlySpan<byte> label, bool active, ReadOnlySpan<byte> state)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(124.0f);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, active ? AccentCyan : MutedText);
        ImGui.TextUnformatted(state);
        ImGui.PopStyleColor();
    }

    private static void DrawValueLine(ReadOnlySpan<byte> label, ReadOnlySpan<byte> value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(124.0f);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }

    private static void DrawStatusBadge(Vector2f position, ReadOnlySpan<byte> label, Vector4f color)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + new Vector2f(175.0f, 34.0f),
            ImGui.GetColorU32(new Vector4f(color.X * 0.14f, color.Y * 0.14f, color.Z * 0.14f, 0.95f)), 17.0f);
        drawList.AddCircleFilled(position + new Vector2f(17.0f, 17.0f), 4.0f, ImGui.GetColorU32(color));
        ImGui.SetCursorScreenPos(position + new Vector2f(30.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    private static void BeginPanel(ReadOnlySpan<byte> id, Vector2f position, Vector2f size)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColBorder, PanelBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarChildRounding, 12.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarChildBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarWindowPadding, new Vector2f(16.0f, 12.0f));
        ImGui.BeginChild(id, size, ImGuiChildFlags.ImGuiChildFlagsBorders);
    }

    private static void EndPanel()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void ShowChatText(string row)
    {
        if (_chatHistory.Count == 0)
            return;
        var oldest = _chatHistory[0];
        _chatHistory.RemoveAt(0);
        oldest.Set(row);
        _chatHistory.Add(oldest);
    }

    private void HandleSampleLogMessage(ref IOEvents.LogMessage eventData) =>
        ShowChatText(eventData.Message);

    private void Send()
    {
        var text = ReadText();
        if (text.Length == 0)
            return;

        using var data = new VectorBuffer();
        data.WriteString(text);
        if (_clientConnection?.IsConnected == true)
        {
            _clientConnection.SendMessage(ChatMessage, data.Buffer);
            ShowChatText("[You -> Server] " + text);
        }
        else if (_server is not null &&
            (_server.IsListening || _relayConnections.Count != 0))
        {
            foreach (var weakConnection in _serverConnections)
            {
                if (weakConnection.TryGetTarget(out var connection))
                    connection.SendMessage(ChatMessage, data.Buffer);
            }
            ShowChatText("[Server -> All] " + text);
        }
        ClearText();
    }

    private void Connect()
    {
        var address = ReadText().Trim();
        if (address.Length == 0)
            address = "localhost";
        ClearText();

        if (_clientConnection is not null && !_clientConnection.IsDisconnected)
            _clientConnection.Disconnect();
        _retryAddress = address;
        _retryCount = 0;
        _wasEverConnected = false;
        _isRelayMode = false;
        TryConnect(address);
    }

    private void TryConnect(string address)
    {
        EnsureClientConnection();
        var host = address;
        var port = DefaultPort;
        var colonPosition = address.IndexOf(':');
        if (colonPosition >= 0)
        {
            host = address[..colonPosition];
            if (ushort.TryParse(address[(colonPosition + 1)..], out var parsedPort))
                port = parsedPort;
        }

        var url = $"ws://{host}:{port}";
        Log($"Connecting to {url} ... (attempt {_retryCount + 1})");
        _clientConnection!.Connect(new URL(url));
        HookIceLogging(_clientConnection);
    }

    private void StartServer()
    {
        EnsureServer();
        var signalingPort = DefaultPort;
        var dataPort = DataPort;
        var input = ReadText().Trim();
        if (ushort.TryParse(input, out var parsedPort) && parsedPort != 0)
        {
            signalingPort = parsedPort;
            dataPort = unchecked((ushort)(signalingPort + 1));
        }
        ClearText();

        _server!.SetIceUdpMux(true);
        _server.SetPortRange(dataPort, dataPort);
        var listenUrl = new URL($"ws://0.0.0.0:{signalingPort}");
        Log($"Signaling: {listenUrl} | WebRTC data: UDP :{dataPort} (single port, muxed)");
        Log($"Port-forward TCP {signalingPort} and UDP {dataPort} for direct connections.");
        _server.Listen(listenUrl);
    }

    private void StartRelay()
    {
        var (roomId, relayUrl) = ParseRelay(ReadText().Trim());
        ClearText();
        EnsureServer();
        Log($"Joining relay room '{roomId}' as host via {relayUrl}...");

        RelayWebSocket? socket = null;
        socket = new RelayWebSocket(roomId, relayUrl, webSocket =>
        {
            Log("Relay: Paired!");
            var connection = new DataChannelConnection();
            connection.SetIceServers(IceServers);
            connection.InitializeWithWebSocket(webSocket, _server);
            HookIceLogging(connection);
            _relayConnections.Add(connection);
            if (socket is not null)
                _relaySockets.Remove(socket);
        });
        _relaySockets.Add(socket);
    }

    private void ConnectRelay()
    {
        var (roomId, relayUrl) = ParseRelay(ReadText().Trim());
        ClearText();
        if (_clientConnection is not null && !_clientConnection.IsDisconnected)
            _clientConnection.Disconnect();
        _retryAddress = roomId;
        _retryRelayUrl = relayUrl;
        _retryCount = 0;
        _wasEverConnected = false;
        _isRelayMode = true;
        TryConnectRelay(roomId, relayUrl);
    }

    private void TryConnectRelay(string roomId, string relayUrl)
    {
        EnsureClientConnection();
        Log($"Joining relay room '{roomId}' as client via {relayUrl}... (attempt {_retryCount + 1})");

        RelayWebSocket? socket = null;
        socket = new RelayWebSocket(roomId, relayUrl, webSocket =>
        {
            Log("Relay: Paired!");
            _clientConnection!.InitializeWithWebSocket(webSocket);
            HookIceLogging(_clientConnection);
            if (socket is not null)
                _relaySockets.Remove(socket);
        });
        _relaySockets.Add(socket);
    }

    private void ScheduleRetry()
    {
        if (_retryCount >= MaxRetries || _wasEverConnected)
            return;
        ++_retryCount;
        Log($"Connection failed. Retrying in {RetryDelay:0}s... ({_retryCount}/{MaxRetries})");
        _retrying = true;
        _retryTimer.Reset();
    }

    private void Disconnect()
    {
        _retryCount = MaxRetries;
        _retrying = false;
        if (_clientConnection is not null && !_clientConnection.IsDisconnected)
        {
            Log("Disconnecting client...");
            _clientConnection.Disconnect();
        }

        if (_server?.IsListening == true)
        {
            Log("Stopping server...");
            foreach (var weakConnection in _serverConnections.ToArray())
            {
                if (weakConnection.TryGetTarget(out var connection))
                    connection.Disconnect();
            }
            _serverConnections.Clear();
            _serverClientIds.Clear();
            _server.Stop();
        }

        foreach (var connection in _relayConnections)
        {
            if (!connection.IsDisconnected)
                connection.Disconnect();
            connection.Dispose();
        }
        _relayConnections.Clear();
        foreach (var socket in _relaySockets.ToArray())
            socket.Dispose();
        _relaySockets.Clear();
    }

    private void EnsureServer()
    {
        if (_server is not null)
            return;
        _server = new DataChannelServer();
        _server.SetIceServers(IceServers);
        _server.OnListenStart += _ => HandleServerListenStart();
        _server.OnListenStop += _ => HandleServerListenStop();
        _server.OnConnected += HandleServerConnected;
        _server.OnDisconnected += HandleServerDisconnected;
    }

    private void EnsureClientConnection()
    {
        if (_clientConnection is not null)
            return;
        _clientConnection = new DataChannelConnection();
        _clientConnection.SetIceServers(IceServers);
        _clientConnection.OnConnected += HandleClientConnected;
        _clientConnection.OnDisconnected += HandleClientDisconnected;
        _clientConnection.OnMessageReceived += HandleNetworkMessage;
    }

    private void HandleServerListenStart()
    {
        Log("Server is listening. Give this machine's IP to the client.");
        Log("For direct connections, forward TCP signaling port and UDP data port.");
    }

    private void HandleServerListenStop() => Log("Server stopped listening.");

    private void HandleServerConnected(NetworkServer server, NetworkConnection connection)
    {
        var clientId = _nextClientId++;
        _serverConnections.Add(new WeakReference<NetworkConnection>(connection));
        _serverClientIds.Add(clientId);
        connection.OnMessageReceived += HandleNetworkMessage;

        if (connection is DataChannelConnection dataChannelConnection)
        {
            HookIceLogging(dataChannelConnection);
            LogSelectedCandidatePair(dataChannelConnection.Peer);
        }

        var type = ClassifyAddress(connection.Address);
        Log(connection.Address.Length != 0
            ? $"Server: Client {clientId} connected! {connection.Address} ({type}) ({_serverConnections.Count} total)"
            : $"Server: Client {clientId} connected! ({_serverConnections.Count} total)");

        using var data = new VectorBuffer();
        data.WriteString($"Client {clientId} joined from {type} ({_serverConnections.Count} online)");
        foreach (var weakConnection in _serverConnections)
        {
            if (weakConnection.TryGetTarget(out var other) && !ReferenceEquals(other, connection))
                other.SendMessage(SystemMessage, data.Buffer);
        }
    }

    private void HandleServerDisconnected(NetworkServer server, NetworkConnection connection)
    {
        uint disconnectedId = 0;
        for (var i = 0; i < _serverConnections.Count;)
        {
            if (_serverConnections[i].TryGetTarget(out var item) && !ReferenceEquals(item, connection))
            {
                ++i;
                continue;
            }
            disconnectedId = _serverClientIds[i];
            _serverConnections.RemoveAt(i);
            _serverClientIds.RemoveAt(i);
        }

        Log($"Server: Client {disconnectedId} disconnected. ({_serverConnections.Count} remaining)");
        if (_serverConnections.Count == 0)
            return;

        using var data = new VectorBuffer();
        data.WriteString($"Client {disconnectedId} left ({_serverConnections.Count} online)");
        foreach (var weakConnection in _serverConnections)
        {
            if (weakConnection.TryGetTarget(out var other))
                other.SendMessage(SystemMessage, data.Buffer);
        }
    }

    private void HandleClientConnected(NetworkConnection connection)
    {
        _wasEverConnected = true;
        _retryCount = MaxRetries;
        Log(connection.Address.Length != 0
            ? $"Connected! Remote peer: {connection.Address} ({ClassifyAddress(connection.Address)})"
            : "Connected! (Web platform - address unavailable)");
        if (connection is DataChannelConnection dataChannelConnection)
            LogSelectedCandidatePair(dataChannelConnection.Peer);
    }

    private void HandleClientDisconnected(NetworkConnection connection)
    {
        if (!_wasEverConnected)
            ScheduleRetry();
        else
            Log("Disconnected from server.");
    }

    private unsafe void HandleNetworkMessage(NetworkConnection connection,
        NetworkMessageId messageId, ReadOnlySpan<byte> messagePayload, ref bool handled)
    {
        fixed (byte* pointer = messagePayload)
        {
            using var message = new MemoryBuffer((nint)pointer, (uint)messagePayload.Length, true);
            ProcessNetworkMessage(connection, messageId, message, ref handled);
        }
    }

    private void ProcessNetworkMessage(NetworkConnection connection,
        NetworkMessageId messageId, MemoryBuffer message, ref bool handled)
    {
        if (messageId == SystemMessage)
        {
            ShowChatText(message.ReadString());
            handled = true;
            return;
        }
        if (messageId != ChatMessage)
            return;

        var text = message.ReadString();
        var isServer = _server is not null &&
            (_server.IsListening || _relayConnections.Count != 0);
        if (isServer)
        {
            uint senderId = 0;
            for (var i = 0; i < _serverConnections.Count; ++i)
            {
                if (_serverConnections[i].TryGetTarget(out var item) && ReferenceEquals(item, connection))
                {
                    senderId = _serverClientIds[i];
                    break;
                }
            }

            ShowChatText($"Client {senderId}: {text}");
            using var data = new VectorBuffer();
            data.WriteString($"Client {senderId}: {text}");
            foreach (var weakConnection in _serverConnections)
            {
                if (weakConnection.TryGetTarget(out var other) && !ReferenceEquals(other, connection))
                    other.SendMessage(ChatMessage, data.Buffer);
            }
        }
        else
            ShowChatText("[Server] " + text);
        handled = true;
    }

    private static (string RoomId, string RelayUrl) ParseRelay(string input)
    {
        if (input.Length == 0)
            input = "default";
        var roomId = input;
        var relayUrl = DefaultRelayUrl;
        var atPosition = input.IndexOf('@');
        if (atPosition >= 0)
        {
            roomId = input[..atPosition];
            var hostPort = input[(atPosition + 1)..];
            if (!hostPort.Contains(':'))
                hostPort += ":9876";
            relayUrl = "ws://" + hostPort;
        }
        return (roomId, relayUrl);
    }

    private static string ClassifyAddress(string address)
    {
        if (address.Length == 0)
            return "?";
        if (address == "::1" || address.StartsWith("::1%") || address.StartsWith("127."))
            return "localhost";
        if (address.StartsWith("10.") || address.StartsWith("192.168."))
            return "LAN";
        if (address.StartsWith("172."))
        {
            var parts = address[4..].Split('.');
            if (parts.Length != 0 && int.TryParse(parts[0], out var value) && value is >= 16 and <= 31)
                return "LAN";
        }
        if (address.StartsWith("fc") || address.StartsWith("fd") || address.StartsWith("fe80"))
            return "LAN";
        return "WAN";
    }

    private static void HookIceLogging(DataChannelConnection connection)
    {
        if (connection.Peer < 0)
            return;
        RtcSetStateChangeCallback(connection.Peer, IceStateCallbackPointer);
        RtcSetGatheringStateChangeCallback(connection.Peer, IceGatheringCallbackPointer);
    }

    private static void LogSelectedCandidatePair(int peer)
    {
        if (peer < 0)
            return;
        var local = ReadNativeString((buffer, size) =>
            RtcGetSelectedCandidatePair(peer, buffer, size, [], 0));
        var remote = ReadNativeString((buffer, size) =>
            RtcGetSelectedCandidatePair(peer, [], 0, buffer, size));
        if (local.Length != 0 && remote.Length != 0)
            Log($"ICE selected pair: local={CandidateType(local)}, remote={CandidateType(remote)}");
    }

    private static string CandidateType(string candidate)
    {
        if (candidate.Contains(" typ host")) return "Host";
        if (candidate.Contains(" typ srflx")) return "ServerReflexive";
        if (candidate.Contains(" typ prflx")) return "PeerReflexive";
        if (candidate.Contains(" typ relay")) return "Relayed";
        return "?";
    }

    private delegate int NativeStringGetter(Span<sbyte> buffer, int size);

    private static string ReadNativeString(NativeStringGetter getter)
    {
        var required = getter([], 0);
        if (required <= 0)
            return string.Empty;
        var buffer = new sbyte[required];
        if (getter(buffer, buffer.Length) < 0)
            return string.Empty;
        var bytes = MemoryMarshal.AsBytes(buffer.AsSpan());
        var length = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(length >= 0 ? bytes[..length] : bytes);
    }

    private string ReadText() => Marshal.PtrToStringUTF8(_textBuffer) ?? string.Empty;
    private void ClearText() => Marshal.WriteByte(_textBuffer, 0, 0);

    private static void Log(string message) =>
        RedBolt.Logging.Log.GetLogger().Info("{}", message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IceStateCallback(int peer, RtcState state, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IceGatheringCallback(int peer, RtcGatheringState state, nint userData);

    private static void HandleIceState(int peer, RtcState state, nint userData) =>
        Log($"ICE state: {state.ToString().Replace("Rtc", string.Empty)} ({(int)state})");

    private static void HandleIceGathering(int peer, RtcGatheringState state, nint userData) =>
        Log($"ICE gathering: {state.ToString().Replace("RtcGathering", string.Empty)} ({(int)state})");

    private static readonly IceStateCallback IceStateChanged = HandleIceState;
    private static readonly nint IceStateCallbackPointer = Marshal.GetFunctionPointerForDelegate(IceStateChanged);
    private static readonly IceGatheringCallback IceGatheringChanged = HandleIceGathering;
    private static readonly nint IceGatheringCallbackPointer = Marshal.GetFunctionPointerForDelegate(IceGatheringChanged);

    private readonly List<CString> _chatHistory = [];
    private readonly List<WeakReference<NetworkConnection>> _serverConnections = [];
    private readonly List<uint> _serverClientIds = [];
    private readonly List<DataChannelConnection> _relayConnections = [];
    private readonly List<RelayWebSocket> _relaySockets = [];
    private readonly Timer _retryTimer = new();
    private IntPtr _textBuffer;
    private DataChannelServer? _server;
    private DataChannelConnection? _clientConnection;
    private uint _nextClientId = 1;
    private string _retryAddress = string.Empty;
    private string _retryRelayUrl = string.Empty;
    private int _retryCount;
    private bool _wasEverConnected;
    private bool _retrying;
    private bool _isRelayMode;
    private static readonly Vector4f DashboardBackground = new(0.012f, 0.02f, 0.045f, 1.0f);
    private static readonly Vector4f PanelBackground = new(0.045f, 0.058f, 0.105f, 0.95f);
    private static readonly Vector4f PanelBorder = new(0.18f, 0.24f, 0.4f, 0.85f);
    private static readonly Vector4f PrimaryText = new(0.92f, 0.96f, 1.0f, 1.0f);
    private static readonly Vector4f MutedText = new(0.52f, 0.59f, 0.72f, 1.0f);
    private static readonly Vector4f AccentCyan = new(0.16f, 0.93f, 1.0f, 1.0f);
    private static readonly Vector4f AccentViolet = new(0.68f, 0.46f, 1.0f, 1.0f);
    private static readonly Vector4f AccentAmber = new(1.0f, 0.68f, 0.26f, 1.0f);
    private static readonly Vector4f DangerButton = new(0.58f, 0.16f, 0.24f, 1.0f);
    private static readonly Vector4f SendButton = new(0.08f, 0.45f, 0.52f, 1.0f);

    private sealed class RelayWebSocket : IDisposable
    {
        public RelayWebSocket(string roomId, string url, Action<int> paired)
        {
            _roomId = roomId;
            _paired = paired;
            _webSocket = RtcCreateWebSocket(Encoding.UTF8.GetBytes(url + '\0'));
            if (_webSocket < 0)
                return;
            _selfHandle = GCHandle.Alloc(this);
            RtcSetUserPointer(_webSocket, GCHandle.ToIntPtr(_selfHandle));
            RtcSetOpenCallback(_webSocket, OpenCallbackPointer);
            RtcSetMessageCallback(_webSocket, MessageCallbackPointer);
            RtcSetClosedCallback(_webSocket, ClosedCallbackPointer);
        }

        public void Dispose()
        {
            var webSocket = Detach();
            if (webSocket >= 0)
            {
                RtcClose(webSocket);
                RtcDeleteWebSocket(webSocket);
            }
        }

        private int Detach()
        {
            var webSocket = _webSocket;
            _webSocket = -1;
            if (webSocket >= 0)
            {
                RtcSetUserPointer(webSocket, 0);
                RtcSetOpenCallback(webSocket, 0);
                RtcSetMessageCallback(webSocket, 0);
                RtcSetClosedCallback(webSocket, 0);
            }
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
            return webSocket;
        }

        private void OnOpen()
        {
            var json = JsonSerializer.Serialize(new { type = "join", room = _roomId });
            var message = Encoding.UTF8.GetBytes(json);
            RtcSendMessage(_webSocket, message, message.Length);
        }

        private void OnMessage(nint message, int size)
        {
            var json = size < 0
                ? Marshal.PtrToStringUTF8(message) ?? string.Empty
                : Encoding.UTF8.GetString(CopyBytes(message, size));
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("type", out var type) &&
                type.GetString() == "paired")
            {
                var webSocket = Detach();
                if (webSocket >= 0)
                    _paired(webSocket);
            }
        }

        private static byte[] CopyBytes(nint source, int size)
        {
            var result = new byte[size];
            if (size != 0)
                Marshal.Copy(source, result, 0, size);
            return result;
        }

        private static RelayWebSocket? GetTarget(nint userData)
        {
            if (userData == 0)
                return null;
            try
            {
                return GCHandle.FromIntPtr(userData).Target as RelayWebSocket;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static void HandleOpen(int webSocket, nint userData)
        {
            try { GetTarget(userData)?.OnOpen(); }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
        }

        private static void HandleMessage(int webSocket, nint message, int size, nint userData)
        {
            try { GetTarget(userData)?.OnMessage(message, size); }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
        }

        private static void HandleClosed(int webSocket, nint userData)
        {
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OpenCallback(int webSocket, nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MessageCallback(int webSocket, nint message, int size, nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ClosedCallback(int webSocket, nint userData);

        private static readonly OpenCallback Opened = HandleOpen;
        private static readonly nint OpenCallbackPointer = Marshal.GetFunctionPointerForDelegate(Opened);
        private static readonly MessageCallback MessageReceived = HandleMessage;
        private static readonly nint MessageCallbackPointer = Marshal.GetFunctionPointerForDelegate(MessageReceived);
        private static readonly ClosedCallback Closed = HandleClosed;
        private static readonly nint ClosedCallbackPointer = Marshal.GetFunctionPointerForDelegate(Closed);

        private readonly string _roomId;
        private readonly Action<int> _paired;
        private int _webSocket = -1;
        private GCHandle _selfHandle;
    }
}
