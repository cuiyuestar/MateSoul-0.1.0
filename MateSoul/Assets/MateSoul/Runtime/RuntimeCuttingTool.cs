using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class RuntimeCuttingTool : MonoBehaviour
    {
        private const float MinimumStrokeDistance = .008f;
        private const float MinimumStrokeSpeed = .28f;
        private const float SweepPadding = .008f;
        private const float CutCooldown = .14f;

        private readonly Collider[] overlaps = new Collider[48];
        private readonly HashSet<GameItem> candidates = new HashSet<GameItem>();
        private GameItem item;
        private GameSession session;
        private ItemInteractionProfile profile;
        private Vector3 previousStart, previousEnd;
        private bool hasPrevious;
        private float nextCutTime;

        public void Configure(GameItem owner, GameSession gameSession,
            ItemInteractionProfile interaction)
        {
            item = owner;
            session = gameSession;
            profile = interaction;
            hasPrevious = false;
        }

        private void FixedUpdate()
        {
            if (item == null || session == null || profile == null ||
                item.Instance == null ||
                item.Instance.ControlState != ItemControlState.PlayerHeld ||
                item.Body == null || item.Body.IsPaused)
            {
                hasPrevious = false;
                return;
            }

            var currentStart = transform.TransformPoint(profile.bladeStart);
            var currentEnd = transform.TransformPoint(profile.bladeEnd);
            if (!hasPrevious)
            {
                previousStart = currentStart;
                previousEnd = currentEnd;
                hasPrevious = true;
                return;
            }

            var previousMidpoint = (previousStart + previousEnd) * .5f;
            var currentMidpoint = (currentStart + currentEnd) * .5f;
            var movement = currentMidpoint - previousMidpoint;
            var blade = currentEnd - currentStart;
            var distance = movement.magnitude;
            var speed = distance / Mathf.Max(Time.fixedDeltaTime, .001f);
            var normal = Vector3.Cross(blade, movement);
            if (Time.fixedTime >= nextCutTime &&
                distance >= MinimumStrokeDistance &&
                speed >= MinimumStrokeSpeed &&
                blade.sqrMagnitude >= .0004f &&
                normal.sqrMagnitude >= .000001f)
            {
                TryCut(currentStart, currentEnd, currentMidpoint, normal.normalized);
            }

            previousStart = currentStart;
            previousEnd = currentEnd;
        }

        private void TryCut(Vector3 currentStart, Vector3 currentEnd,
            Vector3 currentMidpoint, Vector3 planeNormal)
        {
            var minimum = Vector3.Min(Vector3.Min(previousStart, previousEnd),
                Vector3.Min(currentStart, currentEnd));
            var maximum = Vector3.Max(Vector3.Max(previousStart, previousEnd),
                Vector3.Max(currentStart, currentEnd));
            var center = (minimum + maximum) * .5f;
            var extents = (maximum - minimum) * .5f + Vector3.one * SweepPadding;
            var count = Physics.OverlapBoxNonAlloc(center, extents, overlaps,
                Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            candidates.Clear();
            for (var index = 0; index < count; index++)
            {
                var candidate = overlaps[index]?.GetComponentInParent<GameItem>();
                if (candidate == null || candidate == item ||
                    candidate.Definition == null ||
                    !candidate.Definition.Cuttable.enabled ||
                    candidate.Instance == null || candidate.Instance.IsOccupied ||
                    candidate.IsHeld || candidate.Body == null ||
                    candidate.Body.IsPaused ||
                    !SweptBladeTouches(candidate, currentStart, currentEnd))
                    continue;
                candidates.Add(candidate);
            }

            var planePoint = (currentMidpoint +
                              (previousStart + previousEnd) * .5f) * .5f;
            var plane = new Plane(planeNormal, planePoint);
            foreach (var candidate in candidates)
            {
                if (!session.TryCutItem(candidate, plane, this)) continue;
                nextCutTime = Time.fixedTime + CutCooldown;
                break;
            }
        }

        private bool SweptBladeTouches(GameItem candidate,
            Vector3 currentStart, Vector3 currentEnd)
        {
            const int samples = 5;
            for (var sample = 0; sample < samples; sample++)
            {
                var amount = sample / (float)(samples - 1);
                var from = Vector3.Lerp(previousStart, previousEnd, amount);
                var to = Vector3.Lerp(currentStart, currentEnd, amount);
                var movement = to - from;
                var distance = movement.magnitude;
                foreach (var shape in candidate.Body.Shapes)
                {
                    if (shape == null || !shape.enabled) continue;
                    if ((shape.ClosestPoint(from) - from).sqrMagnitude <
                        .00000025f ||
                        (shape.ClosestPoint(to) - to).sqrMagnitude <
                        .00000025f)
                        return true;
                    if (distance > .00001f &&
                        shape.Raycast(new Ray(from, movement / distance),
                            out _, distance + SweepPadding))
                        return true;
                }
            }
            return false;
        }

        private void OnDisable() => hasPrevious = false;
    }
}
