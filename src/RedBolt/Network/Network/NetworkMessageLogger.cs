// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.IO;
using RedBolt.Logging;

/// <summary>Helper for logging sent and received network messages.</summary>
public class NetworkMessageLogger
{
    public NetworkMessageLogger()
    {
    }

    public NetworkMessageLogger(NetworkConnection connection)
    {
        Attach(connection);
    }

    public void Attach(NetworkConnection? connection)
    {
        if (connection is null || ReferenceEquals(_connection, connection))
            return;

        _connection = connection;
        connection.OnSendMessage += HandleSendMessage;
        connection.OnMessageReceived += HandleMessageReceived;
    }

    public bool IsEnabled { get; set; } = true;

    public virtual LogLevel GetMessageLogLevel(NetworkMessageId messageId)
    {
        if (!IsEnabled)
            return LogLevel.None;

        return messageId is NetworkMessageId.Configure or NetworkMessageId.Synchronized
            ? LogLevel.Debug
            : LogLevel.Trace;
    }

    public virtual void LogSentMessage(NetworkMessageId messageId, MemoryBuffer data,
        PacketType packetType, string debugInfo = "")
    {
        var logLevel = GetMessageLogLevel(messageId);
        if (logLevel == LogLevel.None)
            return;

        Log.GetLogger().Write(logLevel, "Message #{} ({} bytes) sent{}{}{}{}",
            (uint)messageId, data.Size,
            packetType.HasFlag(PacketType.Reliable) ? ", reliable" : string.Empty,
            packetType.HasFlag(PacketType.Ordered) ? ", ordered" : string.Empty,
            string.IsNullOrEmpty(debugInfo) ? string.Empty : ": ", debugInfo);
    }

    public virtual void LogReceivedMessage(
        NetworkMessageId messageId, MemoryBuffer data, string debugInfo = "")
    {
        var logLevel = GetMessageLogLevel(messageId);
        if (logLevel == LogLevel.None)
            return;

        Log.GetLogger().Write(logLevel, "Message #{} ({} bytes) received{}{}",
            (uint)messageId, data.Size,
            string.IsNullOrEmpty(debugInfo) ? string.Empty : ": ", debugInfo);
    }

    private void HandleSendMessage(NetworkConnection sender, NetworkMessageId messageId,
        MemoryBuffer data, PacketType type, string debugInfo, ref bool handled)
    {
        if (!handled)
            LogSentMessage(messageId, data, type, debugInfo);
    }

    private void HandleMessageReceived(NetworkConnection sender, NetworkMessageId messageId,
        MemoryBuffer data, ref bool handled)
    {
        LogReceivedMessage(messageId, data);
    }

    private NetworkConnection? _connection;
}
