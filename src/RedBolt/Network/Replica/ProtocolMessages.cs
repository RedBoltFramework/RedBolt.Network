// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

using RedBolt.Core;
using RedBolt.IO;
using RedBolt.Network;

public struct MsgConfigure : INetworkSerializableMessage
{
    public MsgConfigure()
    {
        Settings = new VariantMap();
    }

    public readonly void Save(VectorBuffer destination)
    {
        destination.WriteUInt(Magic);
        destination.WriteVariantMap(Settings);
    }

    public void Load(MemoryBuffer source)
    {
        Magic = source.ReadUInt();
        Settings = source.ReadVariantMap();
    }

    public override readonly string ToString() => $"{{magic={Magic}, settings...}}";

    public uint Magic;
    public VariantMap Settings;
}

public struct MsgSynchronized : INetworkSerializableMessage
{
    public readonly void Save(VectorBuffer destination)
    {
        destination.WriteUInt(Magic);
    }

    public void Load(MemoryBuffer source)
    {
        Magic = source.ReadUInt();
    }

    public override readonly string ToString() => $"{{magic={Magic}}}";

    public uint Magic;
}

public struct MsgSceneClock : INetworkSerializableMessage
{
    public readonly void Save(VectorBuffer destination)
    {
        destination.WriteInt64((long)LatestFrame);
        destination.WriteUInt(LatestFrameTime);
        destination.WriteVLE(InputDelay);
    }

    public void Load(MemoryBuffer source)
    {
        LatestFrame = (NetworkFrame)source.ReadInt64();
        LatestFrameTime = source.ReadUInt();
        InputDelay = source.ReadVLE();
    }

    public override readonly string ToString() =>
        $"{{latestFrame={LatestFrame} at {LatestFrameTime}, inputDelay={InputDelay}}}";

    public NetworkFrame LatestFrame;
    public uint LatestFrameTime;
    public uint InputDelay;
}
