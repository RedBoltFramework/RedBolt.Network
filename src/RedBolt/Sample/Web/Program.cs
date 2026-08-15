namespace RedBolt.Sample.Web;

using RedBolt.Engine;

internal static class SampleCatalog
{
    internal static Application Create(string applicationTypeName) => applicationTypeName switch
    {
        "RedBolt.Sample.Chat.Chat" => new global::RedBolt.Sample.Chat.Chat(),
        "RedBolt.Sample.HttpRequestDemo.HttpRequestDemo" => new global::RedBolt.Sample.HttpRequestDemo.HttpRequestDemo(),
        "RedBolt.Sample.LANDiscovery.LANDiscovery" => new global::RedBolt.Sample.LANDiscovery.LANDiscovery(),
        _ => throw new System.InvalidOperationException($"Unknown RedBolt sample application: {applicationTypeName}"),
    };
}
