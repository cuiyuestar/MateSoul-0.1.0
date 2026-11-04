using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class RuntimeCuttingTool : MonoBehaviour
    {
        private const float MinimumStrokeDistance = .008f;
        private const float MinimumStrokeSpeed = .08f;
        private const float BladeSampleSpacing = .004f;
        private const float SweepPadding = .008f;
        private const float CutCooldown = .14f;

        private readonly Collider[] overlaps = new Collider[48];
        private readonly HashSet<GameItem> candidates = new HashSet<GameItem>();
        private GameItem item;
        private GameSession session;
        private ItemInteractionProfile profile;
        private Vector3 previousStart, previousEnd;
        private Vector3 strokeStart, strokeEnd;
        private bool hasPrevious;
        private float nextCutTime;

        public void Configure(GameItem owner, GameSession gameSession,
            ItemInteractionProfile interaction)
        {
            item = owner;
            session = gameSession;
            profile = interaction;
            hasPrevious = false;
            nextCutTime = 0;
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
                strokeStart = previousStart = currentStart;
                strokeEnd = previousEnd = currentEnd;
                hasPrevious = true;
                return;
            }

            var previousMidpoint = (previousStart + previousEnd) * .5f;
            var currentMidpoint = (currentStart + currentEnd) * .5f;
            var movement = currentMidpoint - previousMidpoint;
            var blade = currentEnd - currentStart;
            var speed = movement.magnitude / Mathf.Max(Time.fixedDeltaTime, .001f);
            var strokeMovement = currentMidpoint - (strokeStart + strokeEnd) * .5f;
            // Accumulate a deliberate stroke across frames. Reversals start a
            // new stroke so hand jitter cannot accumulate into a cut.
            if (Vector3.Dot(strokeMovement, movement) < 0)
            {
                strokeStart = previousStart;
                strokeEnd = previousEnd;
                strokeMovement = movement;
            }
            var distance = strokeMovement.magnitude;
            var normal = Vector3.Cross(blade, strokeMovement);
            var canStroke = Time.fixedTime >= nextCutTime && speed >= MinimumStrokeSpeed;
            if (canStroke &&
                distance >= MinimumStrokeDistance &&
                blade.sqrMagnitude >= .0004f &&
                normal.sqrMagnitude >= .000001f)
            {
                TryCut(currentStart, currentEnd, currentMidpoint, normal.normalized);
            }

            if (!canStroke || distance >= MinimumStrokeDistance)
            {
                strokeStart = currentStart;
                strokeEnd = currentEnd;
            }
            previousStart = currentStart;
            previousEnd = currentEnd;
        }

        private void TryCut(Vector3 currentStart, Vector3 currentEnd,
            Vector3 currentMidpoint, Vector3 planeNormal)
        {
            var minimum = Vector3.Min(Vector3.Min(strokeStart, strokeEnd),
                Vector3.Min(currentStart, currentEnd));
            var maximum = Vector3.Max(Vector3.Max(strokeStart, strokeEnd),
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
                    !candidates.Add(candidate) ||
                    !SweptBladeTouches(candidate, currentStart, currentEnd))
                    continue;
                // One touched item consumes the stroke even when preparation
                // fails. Air strokes do not delay the next real cut.
                nextCutTime = Time.fixedTime + CutCooldown;
                var planePoint = (currentMidpoint +
                                  (strokeStart + strokeEnd) * .5f) * .5f;
                session.TryCutItem(candidate, new Plane(planeNormal, planePoint), this);
                break;
            }
            candidates.Clear();
        }

        private bool SweptBladeTouches(GameItem candidate,
            Vector3 currentStart, Vector3 currentEnd)
        {
            // Fixed five-point sampling left 4 cm gaps on the production blade,
            // enough to miss a narrow piece after an earlier cut.
            var length = Mathf.Max(Vector3.Distance(strokeStart, strokeEnd),
                Vector3.Distance(currentStart, currentEnd));
            var samples = Mathf.Max(2, Mathf.CeilToInt(length / BladeSampleSpacing) + 1);
            for (var sample = 0; sample < samples; sample++)
            {
                var amount = sample / (float)(samples - 1);
                var from = Vector3.Lerp(strokeStart, strokeEnd, amount);
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

        private void OnDisable()
        {
            hasPrevious = false;
            candidates.Clear();
        }
    }
}
