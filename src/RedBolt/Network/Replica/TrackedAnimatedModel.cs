// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.Replica;

using RedBolt.Core;
using RedBolt.Graphics;
using RedBolt.Math;
using RedBolt.Network;

/// <summary>Behavior that tracks bone transforms of AnimatedModel on server. Not implemented on client.</summary>
[Reflected(ObjectCategory.Category_Network)]
public partial class TrackedAnimatedModel : NetworkBehavior
{
    public TrackedAnimatedModel()
        : base(NetworkCallbackFlags.None)
    {
    }

    [Serialized("Track On Client")]
    public bool TrackOnClient { get; set; }

    public override void InitializeOnServer()
    {
        _animatedModel = Node!.GetDerivedComponent<AnimatedModel>();
        if (_animatedModel is null)
            return;

        var traceDuration = NetworkObject!.ReplicationManager!.TraceDurationInFrames;
        _nodeTransformTrace.Resize(traceDuration);
        _boundingBoxTrace.Resize(traceDuration);
        SubscribeToEvent<NetworkEvents.EndServerNetworkFrameEvent>(HandleServerFrameEnd);
    }

    public Vector3d SampleTemporalBonePosition(NetworkTime time, uint index)
    {
        var result = _boneTransformsTrace.SampleValid(time);
        return index < result.Count ? result[checked((int)index)].Position : Vector3d.Zero;
    }

    public Quaternion SampleTemporalBoneRotation(NetworkTime time, uint index)
    {
        var result = _boneTransformsTrace.SampleValid(time);
        return index < result.Count ? result[checked((int)index)].Rotation : Quaternion.IDENTITY;
    }

    public void ProcessTemporalRayQuery(NetworkTime time, RayOctreeQuery query,
        List<RayQueryResult> results)
    {
        if (_animatedModel is null)
            return;

        var bones = _animatedModel.GetSkeleton().GetBones();
        var numBones = bones.Count;
        if (numBones != _boneTransformsTrace.Size)
            return;

        var scene = Scene!;
        var nodeTransform = scene.ToRelativeWorldTransform(_nodeTransformTrace.SampleValid(time));
        var worldTransform = nodeTransform.ToMatrix3x4();

        var worldBoundingBox = _boundingBoxTrace.GetClosestRaw(time.Frame);
        worldBoundingBox.Min += nodeTransform.Position;
        worldBoundingBox.Max += nodeTransform.Position;

        var globalBoneTransforms = _boneTransformsTrace.SampleValid(time);
        var boneTransforms = new Matrix3x4[numBones];
        for (var i = 0; i < numBones; ++i)
            boneTransforms[i] = scene.ToRelativeWorldTransform(globalBoneTransforms[i]).ToMatrix3x4();

        _animatedModel.ProcessCustomRayQuery(
            query, worldBoundingBox, worldTransform, boneTransforms, results);
    }

    private void HandleServerFrameEnd(ref NetworkEvents.EndServerNetworkFrameEvent eventData) =>
        OnServerFrameEnd(eventData.Frame);

    private void OnServerFrameEnd(NetworkFrame frame)
    {
        if (_animatedModel is null)
            return;

        _animatedModel.ApplyAnimation();

        var bones = _animatedModel.GetSkeleton().GetBones();
        var numBones = bones.Count;
        if (_boneTransformsTrace.Size != numBones)
        {
            var traceDuration = NetworkObject!.ReplicationManager!.TraceDurationInFrames;
            _boneTransformsTrace.Resize(checked((uint)numBones), traceDuration);
        }

        var scene = Scene!;
        var nodeTransform = Transform.FromMatrix3x4(Node!.GetWorldTransform());
        var boundingBox = _animatedModel.GetWorldBoundingBox();
        boundingBox.Min -= nodeTransform.Position;
        boundingBox.Max -= nodeTransform.Position;

        _nodeTransformTrace.Set(frame, scene.ToAbsoluteWorldTransform(nodeTransform));
        _boundingBoxTrace.Set(frame, boundingBox);

        var transforms = _boneTransformsTrace.SetUninitialized(frame);
        for (var i = 0; i < numBones; ++i)
        {
            var boneTransform = new Transform();
            if (bones[i].Node is { } boneNode)
            {
                boneTransform.Position = boneNode.GetWorldPosition();
                boneTransform.Rotation = boneNode.GetWorldRotation();
                boneTransform.Scale = boneNode.GetWorldScale();
            }
            transforms[i] = scene.ToAbsoluteWorldTransform(boneTransform);
        }
    }

    private AnimatedModel? _animatedModel;
    private readonly NetworkValue<DoubleTransform> _nodeTransformTrace = new();
    private readonly NetworkValue<BoundingBox> _boundingBoxTrace = new();
    private readonly NetworkValueVector<DoubleTransform> _boneTransformsTrace = new();
}
