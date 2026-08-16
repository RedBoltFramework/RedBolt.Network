// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

using System.Diagnostics;

/// <summary>
/// Relevance of the NetworkObject.
/// If greater than 0, indicates the period of unreliable updates of the NetworkObject.
/// Therefore, it's safe to use any positive number as NetworkObjectRelevance.
/// </summary>
public enum NetworkObjectRelevance : sbyte
{
    Irrelevant = -1,
    NoUpdates = 0,
    NormalUpdates = 1,

    MaxPeriod = 127
}

/// <summary>Network frame that represents discrete time on the server. It's usually non-negative, but it's signed for simpler maths.</summary>
public readonly struct NetworkFrame : IEquatable<NetworkFrame>
{
    public static readonly NetworkFrame Min = new(long.MinValue);
    public static readonly NetworkFrame Max = new(long.MaxValue);

    public NetworkFrame(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static explicit operator NetworkFrame(long value) => new(value);
    public static explicit operator long(NetworkFrame frame) => frame.Value;

    public static NetworkFrame operator ++(NetworkFrame frame)
    {
        Debug.Assert(frame != Max);
        return new NetworkFrame(unchecked(frame.Value + 1));
    }

    public static NetworkFrame operator --(NetworkFrame frame)
    {
        Debug.Assert(frame != Min);
        return new NetworkFrame(unchecked(frame.Value - 1));
    }

    public static long operator -(NetworkFrame lhs, NetworkFrame rhs) => unchecked(lhs.Value - rhs.Value);

    public static NetworkFrame operator +(NetworkFrame lhs, long rhs) => new(unchecked(lhs.Value + rhs));

    public static NetworkFrame operator -(NetworkFrame lhs, long rhs) => new(unchecked(lhs.Value - rhs));

    public bool Equals(NetworkFrame other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NetworkFrame other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(NetworkFrame lhs, NetworkFrame rhs) => lhs.Equals(rhs);

    public static bool operator !=(NetworkFrame lhs, NetworkFrame rhs) => !lhs.Equals(rhs);

    public static bool operator <(NetworkFrame lhs, NetworkFrame rhs) => lhs.Value < rhs.Value;

    public static bool operator >(NetworkFrame lhs, NetworkFrame rhs) => lhs.Value > rhs.Value;

    public static bool operator <=(NetworkFrame lhs, NetworkFrame rhs) => lhs.Value <= rhs.Value;

    public static bool operator >=(NetworkFrame lhs, NetworkFrame rhs) => lhs.Value >= rhs.Value;
}

/// <summary>Marker used to synchronize the beginning of the fixed-step event (like physical world update) and the network frame.</summary>
public struct NetworkFrameSync
{
    /// <summary>Synchronized network frame.</summary>
    public NetworkFrame NetworkFrame;
    /// <summary>Index of the fixed-step event that corresponds to the beginning of the synchronized network frame. If network update rate is the same as fixed-step event rate, index is always zero.</summary>
    public uint Offset;
}
