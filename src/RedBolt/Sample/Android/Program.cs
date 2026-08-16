// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Sample.Android;

internal static class SampleCatalog
{
    internal static readonly SampleEntry[] Entries =
    [
        new("chat", "Chat", static () => new global::RedBolt.Sample.Chat.Chat()),
        new("http-request", "HTTP Request", static () => new global::RedBolt.Sample.HttpRequestDemo.HttpRequestDemo()),
        new("lan-discovery", "LAN Discovery", static () => new global::RedBolt.Sample.LANDiscovery.LANDiscovery()),
    ];
}
