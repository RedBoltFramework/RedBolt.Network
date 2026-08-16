// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.HttpRequestDemo;

using System;

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
    protected override void Setup(EngineParameters parameters)
    {
        parameters.ApplicationName = "RedBolt.Sample.HttpRequestDemo";
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
        parameters.ResourcePaths = "CoreData;Data";
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
        Context.RegisterSubsystem(new DearImGui((ImGuiConfigFlags)Engine.Parameters.DearImGuiFlags));
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
            _httpRequest = new HttpRequest("https://httpbin.org/ip", verb, headers);
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
            _message = _httpRequest.ReadString();
            RedBolt.Logging.Log.GetLogger().Info("HttpRequest success: {} ({})",
                _message, _httpRequest.StatusCode);

            using var json = new JSONFile();
            json.FromString(_message);
            var value = json.Root.Get("origin");
            SetText(value.IsNull ? "Invalid JSON response retrieved!" : "Your IP is: " + value.GetString());
            _finished = true;
        }
    }

    protected override void Stop()
    {
        _httpRequest?.Dispose();
        _httpRequest = null;
        _text.Dispose();
        base.Stop();
    }

    private void SetText(string text) => _text.FormatInPlace("{0}", text);

    private void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2f(480.0f, 100.0f),
            ImGuiCond.ImGuiCondFirstUseEver);
        var open = true;
        if (ImGui.Begin("HTTP Request"u8, ref open,
            ImGuiWindowFlags.ImGuiWindowFlagsNoSavedSettings))
            ImGui.TextUnformatted(_text.ContentBytes);
        ImGui.End();
    }

    private string _message = string.Empty;
    private readonly CString _text = new("Starting request...");
    private HttpRequest? _httpRequest;
    private bool _finished;
}
