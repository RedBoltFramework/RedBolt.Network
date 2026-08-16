// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Network;

/// <summary>Packet types for outgoing buffers. Outgoing messages are grouped by their type.</summary>
[Flags]
public enum PacketType : ushort
{
    Reliable = 1 << 0,
    Ordered = 1 << 1,
    UnreliableUnordered = 0,
    UnreliableOrdered = Ordered,
    ReliableUnordered = Reliable,
    ReliableOrdered = Reliable | Ordered,
}

// static_assert(sizeof(PacketType) == sizeof(NetworkMessageId));
