// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

using RedBolt.Core;
using RedBolt.Replica;

public static class NetworkEvents
{
    /// <summary>Incoming network messages are processed on the client or server.</summary>
    public struct NetworkInputProcessedEvent
    {
        public float TimeStep;
    }

    /// <summary>Begin the frame of network update on the server.</summary>
    public struct BeginServerNetworkFrameEvent
    {
        public NetworkFrame Frame;
    }

    /// <summary>End the frame of network update on the server.</summary>
    public struct EndServerNetworkFrameEvent
    {
        public NetworkFrame Frame;
    }

    /// <summary>Begin the input frame on the client.</summary>
    public struct BeginClientNetworkFrameEvent
    {
        public NetworkFrame Frame;
    }

    /// <summary>End the input frame on the client.</summary>
    public struct EndClientNetworkFrameEvent
    {
        public NetworkFrame Frame;
    }

    /// <summary>About to send network update on the client or server.</summary>
    public struct NetworkUpdateEvent
    {
        public bool IsServer;
    }

    /// <summary>LAN discovery found a hosted server.</summary>
    public struct NetworkHostDiscoveredEvent
    {
        public string Address;
        public int Port;
        public VariantMap Beacon;
    }

    public struct ClientConnectedEvent
    {
        public string Address;
        public int Port;
        public NetworkConnection Connection;
    }

    public struct ClientDisconnectedEvent
    {
        public string Address;
        public int Port;
        public NetworkConnection Connection;
    }

    public struct ServerClientConnectedEvent
    {
        public NetworkServer Server;
        public NetworkConnection Connection;
        public string Address;
        public int Port;
    }

    public struct ServerClientDisconnectedEvent
    {
        public NetworkServer Server;
        public NetworkConnection Connection;
        public string Address;
        public int Port;
    }

    public struct ServerListenStartEvent
    {
        public NetworkServer Server;
    }

    public struct ServerListenStopEvent
    {
        public NetworkServer Server;
    }

    /// <summary>Data is received by the connection.</summary>
    /// <remarks>
    /// The event does not own the data. The data is valid only while the event handlers run.
    /// Copy the data to keep it. Upstream passes the same pointer and size pair.
    /// </remarks>
    public struct ClientDataEvent
    {
        public NetworkConnection Connection;
        public nint DataPointer;
        public int Size;
        public bool Handled;

        public readonly unsafe ReadOnlySpan<byte> Data => new((void*)DataPointer, Size);
    }

    /// <summary>Network message is received by the connection.</summary>
    /// <remarks>The data is the message payload. It is valid only while the event handlers run.</remarks>
    public struct ClientMessageEvent
    {
        public NetworkConnection Connection;
        public NetworkMessageId MessageId;
        public nint DataPointer;
        public int Size;
        public bool Handled;

        public readonly unsafe ReadOnlySpan<byte> Data => new((void*)DataPointer, Size);
    }

    /// <summary>Data is about to be sent by the connection.</summary>
    /// <remarks>The data is valid only while the event handlers run.</remarks>
    public struct ClientSendDataEvent
    {
        public NetworkConnection Connection;
        public PacketType Type;
        public nint DataPointer;
        public int Size;
        public bool Handled;

        public readonly unsafe ReadOnlySpan<byte> Data => new((void*)DataPointer, Size);
    }

    /// <summary>Network message is about to be sent by the connection.</summary>
    /// <remarks>The data is the message payload. It is valid only while the event handlers run.</remarks>
    public struct ClientSendMessageEvent
    {
        public NetworkConnection Connection;
        public NetworkMessageId MessageId;
        public PacketType Type;
        public nint DataPointer;
        public int Size;
        public bool Handled;

        public readonly unsafe ReadOnlySpan<byte> Data => new((void*)DataPointer, Size);
    }
}
