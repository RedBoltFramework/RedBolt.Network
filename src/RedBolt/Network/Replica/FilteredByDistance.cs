// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

using RedBolt.Core;
using RedBolt.Network;

/// <summary>Filters a NetworkObject by minimum distance to the client.</summary>
[Reflected(ObjectCategory.Category_Network)]
public partial class FilteredByDistance : NetworkBehavior
{
    public new const NetworkCallbackFlags CallbackMask = NetworkCallbackFlags.GetRelevanceForClient;
    public const float DefaultDistance = 100.0f;

    public FilteredByDistance()
        : base(CallbackMask)
    {
    }

    [Serialized("Is Relevant")]
    public bool IsRelevant { get; set; } = true;

    [Serialized("Update Period")]
    public uint UpdatePeriod { get; set; }

    [Serialized("Distance")]
    public float Distance { get; set; } = DefaultDistance;

    public override NetworkObjectRelevance? GetRelevanceForClient(ReplicatedPeer connection)
    {
        if (ReferenceEquals(NetworkObject!.OwnerConnection, connection))
            return null;

        var replicationManager = NetworkObject.ReplicationManager!;
        var serverReplicator = replicationManager.ServerReplicator!;
        var ownedObjects = serverReplicator.GetNetworkObjectsOwnedByConnection(connection);

        var distanceToConnectionObjects = float.MaxValue;
        foreach (var networkObject in ownedObjects)
        {
            if (networkObject.CalculateDistanceForFiltering(NetworkObject) is { } distance)
                distanceToConnectionObjects = System.Math.Min(distanceToConnectionObjects, distance);
        }

        if (distanceToConnectionObjects < Distance)
            return null;
        if (!IsRelevant)
            return NetworkObjectRelevance.Irrelevant;

        return (NetworkObjectRelevance)System.Math.Min(UpdatePeriod,
            (uint)NetworkObjectRelevance.MaxPeriod);
    }
}
