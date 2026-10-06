// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.Chat;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using RedBolt.Bindings.DearImGui;
using RedBolt.Core;
using RedBolt.DearImGui;
using RedBolt.Engine;
using RedBolt.IO;
using RedBolt.Input;
using RedBolt.Network;

internal static class ChatProgram
{
    private static int Main()
    {
        try
        {
            using var application = new Chat();
            return application.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

/// <summary>Chat example.</summary>
public sealed class Chat : Application
{
    private const ushort ChatServerPort = 2345;
    private const int InputBufferSize = 1024;
    private static readonly NetworkMessageId ChatMessage =
        (NetworkMessageId)((uint)NetworkMessageId.User + 0);

    protected override void Setup(EngineParameters parameters)
    {
        parameters.ApplicationName = "RedBolt.Sample.Chat";
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
        parameters.ResourcePaths = "CoreData;SampleData";
        parameters.ResourcePrefixPaths = ".;..";
#if !REDBOLT_PLATFORM_BROWSER
            parameters.WindowWidth = 1280;
            parameters.WindowHeight = 720;
#endif
        parameters.WindowTitle = "RedBolt Chat";
    }

    protected override void SetupCommandLine(CommandLineParser parser, EngineParameters parameters)
    {
        parser.AddFlag(["--start-server"], "Start the chat server on launch.",
            () => _startServerOnLaunch = true);
        parser.AddOption(["--connect"], "address", "Connect to a chat server on launch.",
            address => _connectOnLaunch = address);
    }

    protected override void Start()
    {
        base.Start();
        Context.RegisterSubsystem(new RedBolt.Network.Network());
        var dearImGui = new DearImGui((ImGuiConfigFlags)Engine.Parameters.DearImGuiFlags);
        Context.RegisterSubsystem(dearImGui);
        _headingFont = dearImGui.AddFont("Fonts/Anonymous Pro.ttf", size: 28.0f);
        _textBuffer = Marshal.AllocHGlobal(InputBufferSize);
        Marshal.WriteByte(_textBuffer, 0, 0);
        SubscribeToEvent<DearImGuiEvents.EndRenderingDearImGuiEvent>(
            (ref DearImGuiEvents.EndRenderingDearImGuiEvent _) => Draw());
        SubscribeToEvent<IOEvents.LogMessage>(HandleSampleLogMessage);
        Input.SetMouseMode(MouseMode.Free);
        Input.SetMouseVisible(true);

        if (_startServerOnLaunch)
            StartServer();
        else if (!string.IsNullOrWhiteSpace(_connectOnLaunch))
            Connect(_connectOnLaunch);
    }

    protected override void Stop()
    {
        Disconnect();
        _clientConnection?.Dispose();
        _clientConnection = null;
        _server?.Dispose();
        _server = null;
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
        if (ImGui.Begin("Chat"u8, ref open,
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
        {
            drawList.AddRectFilledMultiColor(position, position + size,
                ImGui.GetColorU32(DashboardBackground),
                ImGui.GetColorU32(new Vector4f(0.045f, 0.075f, 0.14f, 1.0f)),
                ImGui.GetColorU32(new Vector4f(0.018f, 0.025f, 0.055f, 1.0f)),
                ImGui.GetColorU32(new Vector4f(0.055f, 0.025f, 0.095f, 1.0f)));
        }

        var compact = size.X < 900.0f;
        var margin = compact ? 14.0f : 28.0f;
        var gap = compact ? 10.0f : 18.0f;
        var headerHeight = compact ? 58.0f : 70.0f;
        var contentWidth = Math.Max(1.0f, size.X - margin * 2.0f);
        var contentHeight = Math.Max(1.0f, size.Y - margin * 2.0f - headerHeight);
        var header = position + new Vector2f(margin, margin);
        DrawHeader(header, contentWidth);

        var main = header + new Vector2f(0.0f, headerHeight);
        var sidebarWidth = compact ? Math.Max(230.0f, contentWidth * 0.34f) : Math.Min(350.0f, contentWidth * 0.30f);
        DrawConnectionPanel(main, new Vector2f(sidebarWidth, contentHeight));
        DrawConversationPanel(main + new Vector2f(sidebarWidth + gap, 0.0f),
            new Vector2f(contentWidth - sidebarWidth - gap, contentHeight));
    }

    private void DrawHeader(Vector2f position, float width)
    {
        ImGui.SetCursorScreenPos(position);
        if (_headingFont is not null)
            ImGui.PushFont(_headingFont, 28.0f);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("NETWORK CHAT"u8);
        ImGui.PopStyleColor();
        if (_headingFont is not null)
            ImGui.PopFont();
        ImGui.SetCursorScreenPos(position + new Vector2f(0.0f, 34.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Realtime messages over RedBolt data channels"u8);
        ImGui.PopStyleColor();

        var (status, color) = GetStatus();
        var badgePosition = position + new Vector2f(Math.Max(0.0f, width - 170.0f), 8.0f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badgePosition, badgePosition + new Vector2f(170.0f, 34.0f),
            ImGui.GetColorU32(new Vector4f(color.X, color.Y, color.Z, 0.14f)), 17.0f);
        drawList.AddCircleFilled(badgePosition + new Vector2f(17.0f, 17.0f), 4.0f, ImGui.GetColorU32(color));
        ImGui.SetCursorScreenPos(badgePosition + new Vector2f(30.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(Encoding.UTF8.GetBytes(status));
        ImGui.PopStyleColor();
    }

    private void DrawConnectionPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Connection"u8, position, size);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentCyan);
        ImGui.TextUnformatted("CONNECTION"u8);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Join a host or listen for peers."u8);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var clientConnected = _clientConnection?.IsConnected == true;
        var serverRunning = _server?.IsListening == true;
        if (!clientConnected && !serverRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
            ImGui.TextUnformatted("SERVER ADDRESS"u8);
            ImGui.PopStyleColor();
            ImGui.SetNextItemWidth(-1.0f);
            ImGui.InputText("##Address"u8, _textBuffer, InputBufferSize);
            ImGui.Spacing();
            if (DrawAccentButton("CONNECT"u8, AccentCyan, new Vector2f(-1.0f, 36.0f)))
                Connect();
#if !REDBOLT_PLATFORM_BROWSER
            ImGui.Spacing();
            if (DrawAccentButton("START LOCAL SERVER"u8, AccentViolet, new Vector2f(-1.0f, 36.0f)))
                StartServer();
#endif
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, clientConnected ? AccentCyan : AccentViolet);
            ImGui.TextUnformatted(clientConnected ? "CONNECTED TO HOST"u8 : "LISTENING ON PORT 2345"u8);
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
            ImGui.TextUnformatted(clientConnected
                ? "Messages are delivered in realtime."u8
                : "Other clients can connect to this device."u8);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            if (DrawAccentButton(clientConnected ? "DISCONNECT"u8 : "STOP SERVER"u8,
                AccentRose, new Vector2f(-1.0f, 36.0f)))
                Disconnect();
        }
        EndPanel();
    }

    private void DrawConversationPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Conversation"u8, position, size);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentViolet);
        ImGui.TextUnformatted("CONVERSATION"u8);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("LIVE SESSION LOG"u8);
        ImGui.PopStyleColor();
        ImGui.Separator();

        var connected = _clientConnection?.IsConnected == true;
        var composerHeight = connected ? 74.0f : 30.0f;
        if (ImGui.BeginChild("##History"u8, new Vector2f(0.0f, Math.Max(50.0f, size.Y - composerHeight - 60.0f))))
        {
            if (_chatHistory.Count == 0)
                DrawEmptyConversation();
            else
            {
                foreach (var row in _chatHistory)
                {
                    ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
                    ImGui.TextWrapped(Encoding.UTF8.GetBytes(row));
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }
            }
        }
        ImGui.EndChild();

        if (connected)
        {
            ImGui.Separator();
            ImGui.SetNextItemWidth(Math.Max(80.0f, ImGui.GetContentRegionAvail().X - 92.0f));
            ImGui.InputText("##Message"u8, _textBuffer, InputBufferSize);
            ImGui.SameLine();
            if (DrawAccentButton("SEND"u8, AccentCyan, new Vector2f(80.0f, 0.0f)))
                Send();
        }
        EndPanel();
    }

    private static void DrawEmptyConversation()
    {
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + ImGui.GetContentRegionAvail() * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center - new Vector2f(0.0f, 18.0f), 27.0f,
            ImGui.GetColorU32(new Vector4f(0.14f, 0.11f, 0.27f, 0.75f)));
        drawList.AddCircle(center - new Vector2f(0.0f, 18.0f), 17.0f,
            ImGui.GetColorU32(AccentViolet), 0, 2.0f);
        drawList.AddCircleFilled(center - new Vector2f(0.0f, 18.0f), 4.0f, ImGui.GetColorU32(AccentCyan));
        ImGui.SetCursorScreenPos(center + new Vector2f(-92.0f, 20.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("NO MESSAGES YET"u8);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(center + new Vector2f(-122.0f, 43.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Connect to a peer to begin chatting."u8);
        ImGui.PopStyleColor();
    }

    private (string Text, Vector4f Color) GetStatus()
    {
        if (_clientConnection?.IsConnected == true)
            return ("CLIENT CONNECTED", AccentCyan);
        if (_server?.IsListening == true)
            return ($"SERVER  /  {_serverConnections.Count} PEERS", AccentViolet);
        if (_clientConnection is not null && !_clientConnection.IsDisconnected)
            return ("CONNECTING", AccentAmber);
        return ("OFFLINE", MutedText);
    }

    private static bool DrawAccentButton(ReadOnlySpan<byte> label, Vector4f accent, Vector2f size)
    {
        ImGui.PushStyleColor(ImGuiCol.ImGuiColButton, new Vector4f(accent.X, accent.Y, accent.Z, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColButtonHovered, new Vector4f(accent.X, accent.Y, accent.Z, 0.34f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColButtonActive, new Vector4f(accent.X, accent.Y, accent.Z, 0.46f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, accent);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static void BeginPanel(ReadOnlySpan<byte> id, Vector2f position, Vector2f size)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColBorder, PanelBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarChildRounding, 12.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarChildBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ImGuiStyleVarWindowPadding, new Vector2f(16.0f, 14.0f));
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
        _chatHistory.Add(row);
        if (_chatHistory.Count > 30)
            _chatHistory.RemoveAt(0);
    }

    private void HandleSampleLogMessage(ref IOEvents.LogMessage eventData) =>
        ShowChatText(eventData.Message);

    private void Send()
    {
        var text = ReadText();
        if (text.Length == 0)
            return;
        if (_clientConnection?.IsConnected == true)
        {
            using var data = new VectorBuffer();
            data.WriteString(text);
            _clientConnection.SendMessage(ChatMessage, data.Buffer);
            ClearText();
        }
    }

    private void Connect(string? address = null)
    {
        address ??= ReadText().Trim();
        if (address.Length == 0)
            address = "localhost";
        ClearText();

        if (_clientConnection is null)
        {
            _clientConnection = new DataChannelConnection();
            _clientConnection.OnConnected += HandleClientConnected;
            _clientConnection.OnDisconnected += HandleClientDisconnected;
            _clientConnection.OnMessageReceived += HandleNetworkMessage;
        }
        if (!_clientConnection.IsDisconnected)
            return;
        _clientConnection.Connect(new URL(address.Contains("://", StringComparison.Ordinal)
            ? address
            : $"ws://{address}:{ChatServerPort}"));
    }

    private void Disconnect()
    {
        if (_clientConnection is not null && !_clientConnection.IsDisconnected)
            _clientConnection.Disconnect();
        else if (_server?.IsListening == true)
        {
            foreach (var weakConnection in _serverConnections)
            {
                if (weakConnection.TryGetTarget(out var connection))
                    connection.Disconnect();
            }
            _serverConnections.Clear();
            _server.Stop();
        }
    }

    private void StartServer()
    {
        if (_server is null)
        {
            _server = new DataChannelServer();
            _server.OnConnected += HandleServerConnected;
            _server.OnDisconnected += HandleServerDisconnected;
        }
        _server.Listen(new URL($"ws://0.0.0.0:{ChatServerPort}"));
    }

    private void HandleServerConnected(NetworkServer server, NetworkConnection connection)
    {
        _serverConnections.Add(new WeakReference<NetworkConnection>(connection));
        connection.OnMessageReceived += HandleNetworkMessage;
    }

    private void HandleServerDisconnected(NetworkServer server, NetworkConnection connection)
    {
        _serverConnections.RemoveAll(weak =>
            !weak.TryGetTarget(out var value) || ReferenceEquals(value, connection));
    }

    private void HandleClientConnected(NetworkConnection connection)
    {
    }

    private void HandleClientDisconnected(NetworkConnection connection)
    {
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
        if (messageId != ChatMessage)
            return;
        var text = message.ReadString();
        if (_server?.IsListening == true && IsServerConnection(connection))
        {
            var senderLabel = connection.Address.Length != 0 ? connection.Address : "Unknown";
            if (connection.Port != 0)
                senderLabel = $"{senderLabel}:{connection.Port}";
            text = senderLabel + " " + text;

            using var data = new VectorBuffer();
            data.WriteString(text);
            foreach (var weakConnection in _serverConnections)
            {
                if (weakConnection.TryGetTarget(out var serverConnection))
                    serverConnection.SendMessage(ChatMessage, data.Buffer);
            }
        }
        ShowChatText(text);
        handled = true;
    }

    private bool IsServerConnection(NetworkConnection connection) =>
        _serverConnections.Any(weak =>
            weak.TryGetTarget(out var value) && ReferenceEquals(value, connection));

    private string ReadText() => Marshal.PtrToStringUTF8(_textBuffer) ?? string.Empty;
    private void ClearText() => Marshal.WriteByte(_textBuffer, 0, 0);

    private readonly List<string> _chatHistory = [];
    private ImFont? _headingFont;
    private IntPtr _textBuffer;
    private DataChannelServer? _server;
    private DataChannelConnection? _clientConnection;
    private readonly List<WeakReference<NetworkConnection>> _serverConnections = [];
    private bool _startServerOnLaunch;
    private string? _connectOnLaunch;

    private static readonly Vector4f DashboardBackground = new(0.015f, 0.022f, 0.05f, 1.0f);
    private static readonly Vector4f PanelBackground = new(0.048f, 0.06f, 0.11f, 0.94f);
    private static readonly Vector4f PanelBorder = new(0.18f, 0.23f, 0.4f, 0.8f);
    private static readonly Vector4f PrimaryText = new(0.91f, 0.95f, 1.0f, 1.0f);
    private static readonly Vector4f MutedText = new(0.51f, 0.58f, 0.71f, 1.0f);
    private static readonly Vector4f AccentCyan = new(0.16f, 0.93f, 1.0f, 1.0f);
    private static readonly Vector4f AccentViolet = new(0.68f, 0.46f, 1.0f, 1.0f);
    private static readonly Vector4f AccentAmber = new(1.0f, 0.68f, 0.26f, 1.0f);
    private static readonly Vector4f AccentRose = new(1.0f, 0.35f, 0.61f, 1.0f);
}
