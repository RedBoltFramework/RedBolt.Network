// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.LANDiscovery;

using System;
using System.Collections.Generic;
using System.Text;

using RedBolt.Bindings.DearImGui;
using RedBolt.Container;
using RedBolt.Core;
using RedBolt.DearImGui;
using RedBolt.Engine;
using RedBolt.Input;
using RedBolt.Network;

internal static class LANDiscoveryProgram
{
    private static int Main()
    {
        try
        {
            using var application = new LANDiscovery();
            return application.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

/// <summary>LAN discovery example.</summary>
public sealed class LANDiscovery : Application
{
    private const ushort ServerPort = 54654;

    protected override void Setup(EngineParameters parameters)
    {
        parameters.ApplicationName = "RedBolt.Sample.LANDiscovery";
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
        parameters.ResourcePaths = "CoreData;SampleData";
        parameters.ResourcePrefixPaths = ".;..";
#if !REDBOLT_PLATFORM_BROWSER
            parameters.WindowWidth = 1280;
            parameters.WindowHeight = 720;
#endif
        parameters.WindowTitle = "RedBolt LAN Discovery";
    }

    protected override void Start()
    {
        base.Start();
        Context.RegisterSubsystem(new RedBolt.Network.Network());
        var dearImGui = new DearImGui((ImGuiConfigFlags)Engine.Parameters.DearImGuiFlags);
        Context.RegisterSubsystem(dearImGui);
        _headingFont = dearImGui.AddFont("Fonts/Anonymous Pro.ttf", size: 28.0f);
        _lanDiscovery = new LANDiscoveryManager();
        SubscribeToEvent<NetworkEvents.NetworkHostDiscoveredEvent>(HandleNetworkHostDiscovered);
        SubscribeToEvent<DearImGuiEvents.EndRenderingDearImGuiEvent>(
            (ref DearImGuiEvents.EndRenderingDearImGuiEvent _) => Draw());
        Input.SetMouseMode(MouseMode.Free);
        Input.SetMouseVisible(true);
    }

    protected override void Update(float timeStep)
    {
        for (var iterator = _serverListItems.GetEnumerator(); iterator.MoveNext();)
        {
            var item = iterator.Current.Value;
            if (Time.GetTimeSinceEpoch() - item.LastSeen <= 10)
                continue;

            RedBolt.Logging.Log.GetLogger().Info("Server {} expired!", item.Name);
            _expiredServers.Add(iterator.Current.Key);
        }

        foreach (var name in _expiredServers)
            _serverListItems.Remove(name);
        _expiredServers.Clear();
    }

    protected override void Stop()
    {
        _lanDiscovery?.Dispose();
        _lanDiscovery = null;
        base.Stop();
    }

    private void Draw()
    {
        Vector2f viewportPosition;
        Vector2f viewportSize;
        uint viewportId;
        using (var viewport = ImGui.GetMainViewport())
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
        if (ImGui.Begin("LAN Discovery"u8, ref open,
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
        using (var drawList = ImGui.GetWindowDrawList())
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
        var headerHeight = compact ? 62.0f : 74.0f;
        var width = Math.Max(1.0f, size.X - margin * 2.0f);
        var height = Math.Max(1.0f, size.Y - margin * 2.0f - headerHeight);
        var header = position + new Vector2f(margin, margin);
        DrawHeader(header, width);

        var main = header + new Vector2f(0.0f, headerHeight);
        var controlsWidth = compact ? Math.Max(235.0f, width * 0.35f) : Math.Min(360.0f, width * 0.30f);
        DrawControlsPanel(main, new Vector2f(controlsWidth, height));
        DrawServerPanel(main + new Vector2f(controlsWidth + gap, 0.0f),
            new Vector2f(width - controlsWidth - gap, height));
    }

    private void DrawHeader(Vector2f position, float width)
    {
        ImGui.SetCursorScreenPos(position);
        if (_headingFont is not null)
            ImGui.PushFont(_headingFont, 28.0f);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("LAN DISCOVERY"u8);
        ImGui.PopStyleColor();
        if (_headingFont is not null)
            ImGui.PopFont();
        ImGui.SetCursorScreenPos(position + new Vector2f(0.0f, 34.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Advertise and find RedBolt sessions on your local network"u8);
        ImGui.PopStyleColor();

        var badge = position + new Vector2f(Math.Max(0.0f, width - 170.0f), 8.0f);
        var active = _serverRunning || _lastDiscoveryTime != 0;
        var color = active ? AccentCyan : MutedText;
        using var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badge, badge + new Vector2f(170.0f, 34.0f),
            ImGui.GetColorU32(new Vector4f(color.X, color.Y, color.Z, 0.14f)), 17.0f);
        drawList.AddCircleFilled(badge + new Vector2f(17.0f, 17.0f), 4.0f, ImGui.GetColorU32(color));
        ImGui.SetCursorScreenPos(badge + new Vector2f(30.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(active ? "DISCOVERY ACTIVE"u8 : "READY TO SCAN"u8);
        ImGui.PopStyleColor();
    }

    private void DrawControlsPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Controls"u8, position, size);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentViolet);
        ImGui.TextUnformatted("LOCAL BEACON"u8);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextWrapped("Broadcast a sample server on UDP port 54654."u8);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        DrawStateCard(_serverRunning ? "BROADCASTING"u8 : "BEACON OFFLINE"u8,
            _serverRunning ? "Test server  /  100 players"u8 : "No local session is being advertised."u8,
            _serverRunning ? AccentViolet : MutedText);
        ImGui.Spacing();
        if (DrawAccentButton(_serverRunning ? "STOP SERVER"u8 : "START SERVER"u8,
            _serverRunning ? AccentRose : AccentViolet, new Vector2f(-1.0f, 38.0f)))
        {
            if (_serverRunning)
                StopServer();
            else
                StartServer();
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentCyan);
        ImGui.TextUnformatted("DISCOVERY SCAN"u8);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextWrapped("Listen for nearby beacons. Results expire after 10 seconds."u8);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        if (DrawAccentButton("SCAN LOCAL NETWORK"u8, AccentCyan, new Vector2f(-1.0f, 38.0f)))
            DoNetworkDiscovery();
        EndPanel();
    }

    private void DrawServerPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Servers"u8, position, size);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentCyan);
        ImGui.TextUnformatted("NEARBY SERVERS"u8);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(Encoding.UTF8.GetBytes($"{_serverListItems.Count} DISCOVERED  /  LIVE UPDATES"));
        ImGui.PopStyleColor();
        ImGui.Separator();

        if (ImGui.BeginChild("##ServerRows"u8, ImGui.GetContentRegionAvail()))
        {
            if (_serverListItems.Count == 0)
                DrawEmptyState();
            else
            {
                foreach (var item in _serverListItems.Values)
                    DrawServerRow(item);
            }
        }
        ImGui.EndChild();
        EndPanel();
    }

    private static void DrawStateCard(ReadOnlySpan<byte> title, ReadOnlySpan<byte> detail, Vector4f accent)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(80.0f, ImGui.GetContentRegionAvail().X);
        using (var drawList = ImGui.GetWindowDrawList())
        {
            drawList.AddRectFilled(origin, origin + new Vector2f(width, 68.0f),
                ImGui.GetColorU32(RowBackground), 8.0f);
            drawList.AddCircleFilled(origin + new Vector2f(17.0f, 20.0f), 4.0f, ImGui.GetColorU32(accent));
        }
        ImGui.SetCursorScreenPos(origin + new Vector2f(30.0f, 10.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, accent);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin + new Vector2f(14.0f, 38.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(detail);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2f(width, 68.0f));
    }

    private void DrawServerRow(ServerListEntry item)
    {
        const float rowHeight = 74.0f;
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(120.0f, ImGui.GetContentRegionAvail().X);
        var age = Time.GetTimeSinceEpoch() - item.LastSeen;
        using (var drawList = ImGui.GetWindowDrawList())
        {
            drawList.AddRectFilled(origin, origin + new Vector2f(width, rowHeight - 8.0f),
                ImGui.GetColorU32(RowBackground), 8.0f);
            drawList.AddRectFilled(origin, origin + new Vector2f(3.0f, rowHeight - 8.0f),
                ImGui.GetColorU32(AccentCyan), 8.0f);
            drawList.AddCircleFilled(origin + new Vector2f(width - 18.0f, 18.0f), 4.0f,
                ImGui.GetColorU32(AccentCyan));
        }
        ImGui.SetCursorScreenPos(origin + new Vector2f(15.0f, 9.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted(Encoding.UTF8.GetBytes(item.Name));
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin + new Vector2f(15.0f, 35.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(Encoding.UTF8.GetBytes($"{item.Address}:{item.Port}  /  {item.Players} players  /  seen {age}s ago"));
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2f(width, rowHeight));
    }

    private static void DrawEmptyState()
    {
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + ImGui.GetContentRegionAvail() * 0.5f;
        using var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center - new Vector2f(0.0f, 20.0f), 29.0f,
            ImGui.GetColorU32(new Vector4f(0.08f, 0.16f, 0.24f, 0.75f)));
        drawList.AddCircle(center - new Vector2f(0.0f, 20.0f), 19.0f,
            ImGui.GetColorU32(AccentCyan), 0, 2.0f);
        drawList.AddCircleFilled(center - new Vector2f(0.0f, 20.0f), 5.0f, ImGui.GetColorU32(AccentViolet));
        ImGui.SetCursorScreenPos(center + new Vector2f(-96.0f, 24.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("NO SERVERS FOUND"u8);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(center + new Vector2f(-139.0f, 48.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Start a server or scan your local network."u8);
        ImGui.PopStyleColor();
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

    private void HandleNetworkHostDiscovered(ref NetworkEvents.NetworkHostDiscoveredEvent eventData)
    {
        var data = eventData.Beacon;
        var name = data[new StringHash("Name")].GetString();
        if (_serverListItems.TryGetValue(name, out var item))
        {
            RedBolt.Logging.Log.GetLogger().Info("Server {} reannounced itself!", name);
            item.LastSeen = Time.GetTimeSinceEpoch();
            _serverListItems[name] = item;
        }
        else
        {
            RedBolt.Logging.Log.GetLogger().Info("Server {} discovered!", name);
            _serverListItems[name] = new ServerListEntry
            {
                Name = name,
                Players = data[new StringHash("Players")].GetInt(),
                Address = eventData.Address,
                Port = (ushort)eventData.Port,
                LastSeen = Time.GetTimeSinceEpoch()
            };
        }
    }

    private void StartServer()
    {
        using var data = new VariantMap
        {
            [new StringHash("Name")] = "Test server",
            [new StringHash("Players")] = 100
        };
        _lanDiscovery!.SetBroadcastData(data);
        _serverRunning = _lanDiscovery.Start(ServerPort);
    }

    private void StopServer()
    {
        _lanDiscovery!.Stop();
        _serverRunning = false;
    }

    private void DoNetworkDiscovery()
    {
        _lanDiscovery!.Start(ServerPort);
        _serverListItems.Clear();
        _lastDiscoveryTime = Time.GetTimeSinceEpoch();
    }

    private struct ServerListEntry
    {
        public string Name;
        public int Players;
        public string Address;
        public ushort Port;
        public uint LastSeen;
    }

    private readonly SortedDictionary<string, ServerListEntry> _serverListItems = [];
    private readonly List<string> _expiredServers = [];
    private LANDiscoveryManager? _lanDiscovery;
    private bool _serverRunning;
    private uint _lastDiscoveryTime;
    private ImFont? _headingFont;

    private static readonly Vector4f DashboardBackground = new(0.015f, 0.022f, 0.05f, 1.0f);
    private static readonly Vector4f PanelBackground = new(0.048f, 0.06f, 0.11f, 0.94f);
    private static readonly Vector4f PanelBorder = new(0.18f, 0.23f, 0.4f, 0.8f);
    private static readonly Vector4f RowBackground = new(0.025f, 0.038f, 0.078f, 0.9f);
    private static readonly Vector4f PrimaryText = new(0.91f, 0.95f, 1.0f, 1.0f);
    private static readonly Vector4f MutedText = new(0.51f, 0.58f, 0.71f, 1.0f);
    private static readonly Vector4f AccentCyan = new(0.16f, 0.93f, 1.0f, 1.0f);
    private static readonly Vector4f AccentViolet = new(0.68f, 0.46f, 1.0f, 1.0f);
    private static readonly Vector4f AccentRose = new(1.0f, 0.35f, 0.61f, 1.0f);
}
