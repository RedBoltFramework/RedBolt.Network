// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

/// <summary>Known message IDs used by networking system. User-defined message IDs should start from User. Expect values of these message IDs to change between versions.</summary>
public enum NetworkMessageId : uint
{
    /// <summary>Message used to synchronize clock between client and server.</summary>
    ClockSync,

    /// <summary>Server-&gt;Client. ReplicationManager message. Deliver networking settings.</summary>
    Configure,
    /// <summary>Server-&gt;Client. ReplicationManager message. Send server time and dynamic properties of the client connection.</summary>
    SceneClock,
    /// <summary>Client-&gt;Server. ReplicationManager message. Notify server that the client is ready for replication.</summary>
    Synchronized,
    /// <summary>Server-&gt;Client. ReplicationManager message. Remove replicated NetworkObjects.</summary>
    RemoveObjects,
    /// <summary>Server-&gt;Client. ReplicationManager message. Create replicated NetworkObjects from snapshots.</summary>
    AddObjects,
    AddObjectsIncomplete,
    /// <summary>Server-&gt;Client. ReplicationManager message. Perform ordered and reliable update of NetworkObjects.</summary>
    UpdateObjectsReliable,
    UpdateObjectsReliableIncomplete,
    /// <summary>Server-&gt;Client. ReplicationManager message. Perform unordered and unreliable update of NetworkObjects.</summary>
    UpdateObjectsUnreliable,
    /// <summary>Client-&gt;Server. ReplicationManager message. Perform unordered and unreliable update of owned NetworkObjects from client to server.</summary>
    ObjectsFeedbackUnreliable,

    /// <summary>Server-&gt;Client. Request client to load scene file and report result.</summary>
    LoadScene,
    /// <summary>Client-&gt;Server. Report scene loading result.</summary>
    SceneLoadResult,

    /// <summary>Message IDs starting from User are reserved for the end user.</summary>
    User,

    /// <summary>Max message ID value.</summary>
    Max = uint.MaxValue,
}

public static class NetworkProtocol
{
    /// <summary>Conservative limit for the size of the packet transmitted over underlying transport. Real limit may be higher.</summary>
    /// <seealso cref="ReplicatedPeer.MaxPacketSize" />
    public const int MaxNetworkPacketSize = 1024;
    /// <summary>Maximum possible size of network message payload. Transport limitations are not considered. Real limit may be lower.</summary>
    /// <seealso cref="ReplicatedPeer.MaxMessageSize" />
    public const int MaxNetworkMessageSize = 0xFFFF;

    /// <summary>Max size of the message header (message id only).</summary>
    public const int NetworkMessageHeaderSize = 4;
}
