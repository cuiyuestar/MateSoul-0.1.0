using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class PhysicsBody
    {
        private const int FollowCandidateCapacity = 256;
        private Collider[] followOverlaps;
        private FollowShape[] followShapes;
        private FollowCandidate[] followCandidates;
        private bool[] followPairs;
        private Pose[] followSamples;
        private int followShapeCount, followCandidateCount, followOverlapCount, followSampleCount;
        private Quaternion followBoundsRotation;

        private struct FollowShape
        {
            public Collider Collider;
            public Vector3 Position, BoundsCenter, BoundsExtents;
            public Quaternion Rotation;
            public float QueryRadius;
        }

        private struct FollowCandidate
        {
            public Collider Collider;
            public Vector3 Position;
            public Quaternion Rotation;
            public Bounds Bounds;
        }

        private void EnsureFollowScratch()
        {
            if (followOverlaps != null) return;
            followOverlaps = new Collider[FollowCandidateCapacity];
            followCandidates = new FollowCandidate[FollowCandidateCapacity];
            followSamples = new Pose[60];
            ResizeFollowShapes();
        }

        private void ResizeFollowShapes()
        {
            followShapes = new FollowShape[shapes.Length];
            followPairs = new bool[shapes.Length * FollowCandidateCapacity];
        }

        private void ClearFollowScratch()
        {
            if (followOverlaps == null) return;
            Array.Clear(followOverlaps, 0, followOverlapCount);
            Array.Clear(followCandidates, 0, followCandidateCount);
            Array.Clear(followShapes, 0, followShapeCount);
            followShapeCount = followCandidateCount = followOverlapCount = followSampleCount = 0;
        }

        private bool PrepareFollowCandidates(Pose from, Pose target, int count, float maximumAmount)
        {
            ClearFollowScratch();
            // Child geometry and ignored pairs may change between updates.
            // Snapshot them once per solve, rather than once per pose sample.
            var rootPosition = transform.position;
            followBoundsRotation = Quaternion.Inverse(transform.rotation);
            foreach (var shape in shapes)
            {
                if (!shape.enabled || !shape.gameObject.activeInHierarchy ||
                    shape.GetComponent<AvatarBodyPartCollider>() != null) continue;
                var bounds = shape.bounds;
                followShapes[followShapeCount++] = new FollowShape
                {
                    Collider = shape,
                    Position = followBoundsRotation * (shape.transform.position - rootPosition),
                    Rotation = followBoundsRotation * shape.transform.rotation,
                    BoundsCenter = followBoundsRotation * (bounds.center - rootPosition),
                    BoundsExtents = bounds.extents,
                    QueryRadius = bounds.extents.magnitude +
                                  Vector3.Distance(bounds.center, shape.transform.position) + .02f
                };
            }

            var sweptBounds = default(Bounds);
            var hasBounds = false;
            for (var index = 1; index <= count; index++)
            {
                var amount = Mathf.Min(index / (float)count, maximumAmount);
                var sample = new Pose(Vector3.Lerp(from.position, target.position, amount),
                    Quaternion.Slerp(from.rotation, target.rotation, amount));
                followSamples[followSampleCount++] = sample;
                for (var shapeIndex = 0; shapeIndex < followShapeCount; shapeIndex++)
                {
                    // Cover the original sphere queries as well as visible geometry.
                    // Physics query poses can differ from interpolated Transform poses.
                    var shape = followShapes[shapeIndex];
                    var bounds = new Bounds(sample.position + sample.rotation * shape.Position,
                        Vector3.one * (shape.QueryRadius * 2f));
                    if (!hasBounds) { sweptBounds = bounds; hasBounds = true; }
                    else sweptBounds.Encapsulate(bounds);
                }
                if (amount >= maximumAmount) break;
            }
            if (!hasBounds) return true;
            followOverlapCount = Physics.OverlapBoxNonAlloc(sweptBounds.center,
                sweptBounds.extents + Vector3.one * .002f, followOverlaps,
                Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            // Never move through an incompletely enumerated collision set.
            if (followOverlapCount == followOverlaps.Length) return false;
            var rb = Rigidbody;
            for (var index = 0; index < followOverlapCount; index++)
            {
                var other = followOverlaps[index];
                if (other.attachedRigidbody == rb) continue;
                if (stableCutting && other.GetComponentInParent<GameItem>() is GameItem item &&
                    item.Definition != null && item.Definition.Cuttable.enabled) continue;
                var candidateIndex = followCandidateCount++;
                followCandidates[candidateIndex] = new FollowCandidate
                {
                    Collider = other, Position = other.transform.position,
                    Rotation = other.transform.rotation, Bounds = FollowCandidateBounds(other)
                };
                for (var shapeIndex = 0; shapeIndex < followShapeCount; shapeIndex++)
                    followPairs[shapeIndex * FollowCandidateCapacity + candidateIndex] =
                        !Physics.GetIgnoreCollision(followShapes[shapeIndex].Collider, other);
            }
            return true;
        }

        private static Bounds FollowCandidateBounds(Collider collider)
        {
            var bounds = collider.bounds;
            var body = collider.attachedRigidbody;
            if (body == null) return bounds;
            // ComputePenetration uses the visible Transform pose, while bounds
            // describes the physics pose. Retain both for conservative pruning.
            var delta = body.transform.rotation * Quaternion.Inverse(body.rotation);
            var center = body.transform.position + delta * (bounds.center - body.position);
            var extents = Abs(delta * Vector3.right) * bounds.extents.x +
                          Abs(delta * Vector3.up) * bounds.extents.y +
                          Abs(delta * Vector3.forward) * bounds.extents.z;
            bounds.Encapsulate(new Bounds(center, extents * 2f));
            return bounds;
        }

        private Bounds FollowBounds(FollowShape shape, Pose sample)
        {
            // Rotate the current world AABB, not the collider's local dimensions:
            // this remains conservative for offset children and nonuniform scale.
            var delta = sample.rotation * followBoundsRotation;
            var extents = Abs(delta * Vector3.right) * shape.BoundsExtents.x +
                          Abs(delta * Vector3.up) * shape.BoundsExtents.y +
                          Abs(delta * Vector3.forward) * shape.BoundsExtents.z;
            return new Bounds(sample.position + sample.rotation * shape.BoundsCenter, extents * 2f);
        }

        private bool CanOccupyFollowSample(Pose sample)
        {
            for (var shapeIndex = 0; shapeIndex < followShapeCount; shapeIndex++)
            {
                var shape = followShapes[shapeIndex];
                var bounds = FollowBounds(shape, sample);
                bounds.Expand(.004f);
                var position = sample.position + sample.rotation * shape.Position;
                var rotation = sample.rotation * shape.Rotation;
                for (var index = 0; index < followCandidateCount; index++)
                {
                    var other = followCandidates[index];
                    if (!followPairs[shapeIndex * FollowCandidateCapacity + index] ||
                        !bounds.Intersects(other.Bounds)) continue;
                    if (Physics.ComputePenetration(shape.Collider, position, rotation,
                            other.Collider, other.Position, other.Rotation, out _, out var depth) &&
                        depth > .001f) return false;
                }
            }
            return true;
        }

        private static Vector3 Abs(Vector3 value)
            => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
