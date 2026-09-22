// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.HttpRequestDemo;

using System;
using System.Buffers.Text;

using RedBolt.Bindings.DearImGui;
using RedBolt.Container;
using RedBolt.Core;
using RedBolt.DearImGui;
using RedBolt.Engine;
using RedBolt.Input;
using RedBolt.Network;
using RedBolt.Resource;

internal static class HttpRequestDemoProgram
{
    private static int Main()
    {
        try
        {
            using var application = new HttpRequestDemo();
            return application.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

/// <summary>HTTP request example.</summary>
public sealed class HttpRequestDemo : Application
{
    private const string RequestUrl = "https://httpbin.org/ip";

    protected override void Setup(EngineParameters parameters)
    {
        parameters.ApplicationName = "RedBolt.Sample.HttpRequestDemo";
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
        parameters.ResourcePaths = "CoreData;SampleData";
        parameters.ResourcePrefixPaths = ".;..";
#if !REDBOLT_PLATFORM_BROWSER
            parameters.WindowWidth = 1280;
            parameters.WindowHeight = 720;
#endif
        parameters.WindowTitle = "RedBolt HTTP Request Demo";
    }

    protected override void Start()
    {
        base.Start();
        Context.RegisterSubsystem(new RedBolt.Network.Network());
        var dearImGui = new DearImGui((ImGuiConfigFlags)Engine.Parameters.DearImGuiFlags);
        Context.RegisterSubsystem(dearImGui);
        _headingFont = dearImGui.AddFont("Fonts/Anonymous Pro.ttf", size: 28.0f);
        SubscribeToEvent<DearImGuiEvents.EndRenderingDearImGuiEvent>(
            (ref DearImGuiEvents.EndRenderingDearImGuiEvent _) => Draw());
        Input.SetMouseMode(MouseMode.Free);
        Input.SetMouseVisible(true);
    }

    protected override void Update(float timeStep)
    {
        if (_httpRequest is null)
        {
            const string verb = "GET";
            string[] headers = ["hello: world"];
            _httpRequest = new HttpRequest(RequestUrl, verb, headers);
        }
        else if (_httpRequest.State == HttpRequestState.Initializing)
            return;
        else if (_httpRequest.State == HttpRequestState.Error)
        {
            SetText("An error has occurred: " + _httpRequest.Error);
            RedBolt.Logging.Log.GetLogger().Error("HttpRequest error: {} ({})",
                _httpRequest.Error, _httpRequest.StatusCode);
            _finished = true;
        }
        else if (_httpRequest.State == HttpRequestState.Open)
            SetText("Processing...");
        else if (_httpRequest.State == HttpRequestState.Closed && !_finished)
        {
            _message.FormatInPlace("{0}", _httpRequest.ReadString());
            RedBolt.Logging.Log.GetLogger().Info("HttpRequest success: {} ({})",
                _message.ToString(), _httpRequest.StatusCode);

            using var json = new JSONFile();
            json.FromString(_message.ToString());
            var value = json.Root.Get("origin");
            SetText(value.IsNull ? "Invalid JSON response retrieved!" : "Your IP is: " + value.GetString());
            _finished = true;
        }
    }

    protected override void Stop()
    {
        _httpRequest?.Dispose();
        _httpRequest = null;
        _message.Dispose();
        _text.Dispose();
        base.Stop();
    }

    private void SetText(string text) => _text.FormatInPlace("{0}", text);

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
        if (ImGui.Begin("HTTP Request"u8, ref open,
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
        var headerHeight = compact ? 62.0f : 74.0f;
        var width = Math.Max(1.0f, size.X - margin * 2.0f);
        var height = Math.Max(1.0f, size.Y - margin * 2.0f - headerHeight);
        var header = position + new Vector2f(margin, margin);
        DrawHeader(header, width);

        var main = header + new Vector2f(0.0f, headerHeight);
        var summaryHeight = compact ? 116.0f : 130.0f;
        DrawRequestPanel(main, new Vector2f(width, summaryHeight));
        var resultPosition = main + new Vector2f(0.0f, summaryHeight + gap);
        DrawResultPanel(resultPosition, new Vector2f(width, height - summaryHeight - gap));
    }

    private void DrawHeader(Vector2f position, float width)
    {
        ImGui.SetCursorScreenPos(position);
        if (_headingFont is not null)
            ImGui.PushFont(_headingFont, 28.0f);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
        ImGui.TextUnformatted("HTTP REQUEST"u8);
        ImGui.PopStyleColor();
        if (_headingFont is not null)
            ImGui.PopFont();
        ImGui.SetCursorScreenPos(position + new Vector2f(0.0f, 34.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted("Asynchronous network request and JSON response parsing"u8);
        ImGui.PopStyleColor();

        var color = GetStatusColor();
        var badge = position + new Vector2f(Math.Max(0.0f, width - 150.0f), 8.0f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badge, badge + new Vector2f(150.0f, 34.0f),
            ImGui.GetColorU32(new Vector4f(color.X, color.Y, color.Z, 0.14f)), 17.0f);
        drawList.AddCircleFilled(badge + new Vector2f(17.0f, 17.0f), 4.0f, ImGui.GetColorU32(color));
        ImGui.SetCursorScreenPos(badge + new Vector2f(30.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(GetStatusLabel());
        ImGui.PopStyleColor();
    }

    private void DrawRequestPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Request"u8, position, size);
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, AccentCyan);
        ImGui.TextUnformatted("REQUEST PIPELINE"u8);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        DrawRequestField("METHOD"u8, "GET"u8, AccentViolet, 76.0f);
        ImGui.SameLine();
        DrawRequestField("ENDPOINT"u8, RequestUrlBytes, PrimaryText,
            Math.Max(180.0f, size.X - 310.0f));
        ImGui.SameLine();
        Span<byte> code = stackalloc byte[16];
        var codeLength = 2;
        "--"u8.CopyTo(code);
        if (_httpRequest is not null && _httpRequest.StatusCode != 0)
            _ = Utf8Formatter.TryFormat(_httpRequest.StatusCode, code, out codeLength);
        DrawRequestField("STATUS"u8, code[..codeLength], GetStatusColor(), 112.0f);
        EndPanel();
    }

    private static void DrawRequestField(ReadOnlySpan<byte> label, ReadOnlySpan<byte> value,
        Vector4f accent, float width)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        {
            drawList.AddRectFilled(origin, origin + new Vector2f(width, 62.0f),
                ImGui.GetColorU32(FieldBackground), 8.0f);
            drawList.AddRectFilled(origin, origin + new Vector2f(3.0f, 62.0f), ImGui.GetColorU32(accent), 8.0f);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2f(13.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin + new Vector2f(13.0f, 31.0f));
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, accent);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2f(width, 62.0f));
    }

    private void DrawResultPanel(Vector2f position, Vector2f size)
    {
        BeginPanel("##Response"u8, position, size);
        var color = GetStatusColor();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
        ImGui.TextUnformatted(_finished ? "RESPONSE"u8 : "REQUEST IN FLIGHT"u8);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
        ImGui.TextUnformatted(_finished ? "PARSED RESULT  /  RAW PAYLOAD"u8 : "WAITING FOR REMOTE HOST"u8);
        ImGui.PopStyleColor();
        ImGui.Separator();

        var origin = ImGui.GetCursorScreenPos();
        if (!_finished)
        {
            var center = origin + ImGui.GetContentRegionAvail() * 0.5f;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddCircle(center - new Vector2f(0.0f, 20.0f), 28.0f, ImGui.GetColorU32(PanelBorder), 0, 3.0f);
            drawList.AddCircleFilled(center - new Vector2f(0.0f, 20.0f), 6.0f, ImGui.GetColorU32(AccentCyan));
            ImGui.SetCursorScreenPos(center + new Vector2f(-78.0f, 26.0f));
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
            ImGui.TextUnformatted("FETCHING RESPONSE"u8);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, color);
            if (_headingFont is not null)
                ImGui.PushFont(_headingFont, 24.0f);
            ImGui.TextWrapped(_text.ContentBytes);
            if (_headingFont is not null)
                ImGui.PopFont();
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ImGuiColText, MutedText);
            ImGui.TextUnformatted("RAW RESPONSE"u8);
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.ImGuiColChildBg, FieldBackground);
            if (ImGui.BeginChild("##Payload"u8, new Vector2f(0.0f, Math.Max(60.0f, size.Y - 112.0f))))
            {
                ImGui.PushStyleColor(ImGuiCol.ImGuiColText, PrimaryText);
                ImGui.TextWrapped(_message.ContentBytes);
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        EndPanel();
    }

    private ReadOnlySpan<byte> GetStatusLabel()
    {
        if (!_finished)
            return "IN PROGRESS"u8;
        if (_httpRequest?.State == HttpRequestState.Error || _httpRequest?.StatusCode >= 400)
            return "REQUEST FAILED"u8;
        return "COMPLETE"u8;
    }

    private Vector4f GetStatusColor()
    {
        if (!_finished)
            return AccentAmber;
        if (_httpRequest?.State == HttpRequestState.Error || _httpRequest?.StatusCode >= 400)
            return AccentRose;
        return AccentCyan;
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

    private readonly CString _message = new(string.Empty);
    private readonly CString _text = new("Starting request...");
    private HttpRequest? _httpRequest;
    private bool _finished;
    private ImFont? _headingFont;

    private static ReadOnlySpan<byte> RequestUrlBytes => "https://httpbin.org/ip"u8;

    private static readonly Vector4f DashboardBackground = new(0.015f, 0.022f, 0.05f, 1.0f);
    private static readonly Vector4f PanelBackground = new(0.048f, 0.06f, 0.11f, 0.94f);
    private static readonly Vector4f PanelBorder = new(0.18f, 0.23f, 0.4f, 0.8f);
    private static readonly Vector4f FieldBackground = new(0.025f, 0.038f, 0.078f, 0.9f);
    private static readonly Vector4f PrimaryText = new(0.91f, 0.95f, 1.0f, 1.0f);
    private static readonly Vector4f MutedText = new(0.51f, 0.58f, 0.71f, 1.0f);
    private static readonly Vector4f AccentCyan = new(0.16f, 0.93f, 1.0f, 1.0f);
    private static readonly Vector4f AccentViolet = new(0.68f, 0.46f, 1.0f, 1.0f);
    private static readonly Vector4f AccentAmber = new(1.0f, 0.68f, 0.26f, 1.0f);
    private static readonly Vector4f AccentRose = new(1.0f, 0.35f, 0.61f, 1.0f);
}
