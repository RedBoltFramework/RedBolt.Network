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
