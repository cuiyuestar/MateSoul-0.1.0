using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    /// <summary>Near-only grasp input; XRI owns selection and PhysicsBody owns object motion.</summary>
    public sealed class PicoDirectHandInteractor : XRBaseInteractor, IHandHoldPoseProvider
    {
        private const float TrackingGraceSeconds = .15f;
        private const float GrabRecoverySeconds = .35f;
        private const float RecoveryDistance = .12f;
        private const float RecoveryAngle = 45f;
        [SerializeField, Min(.01f)] private float reach = .13f;
        // A hollow cookware compound can contribute dozens of near colliders.
        private readonly Collider[] overlaps = new Collider[128];
        private PicoTrackedHand hand;
        private PlayerInteractionModeController mode;
        private IXRSelectInteractable candidate;
        private readonly List<IXRSelectInteractable> considered = new List<IXRSelectInteractable>(16);
        private readonly List<IXRInteractable> registeredTargets = new List<IXRInteractable>();
        private Transform graspAttach;
        private Vector3 surfaceOffset;
        private Pose previousGrasp;
        private bool candidatePinch, activePinch;
        private int gripFingers;
        private bool armed, selecting;
        private float acquisitionSeconds;
        private bool wasGripping, wasPinching, pinchArmed;
        private bool previouslyTracked;
        private bool requireOpenAfterTrackingLoss;
        private float trackingSuspendedAt = -1;
        private IXRSelectInteractable recoveryCandidate;
        private bool recoveryPinch;
        private float recoveryDeadline;
        private int proximityFrame = -1;
        private ItemGripAnchor stableGrip;
        private Quaternion stableFrame;
        public ItemGripAnchor StableGrip => stableGrip;
#region debug-point C:handhold-candidate
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private float debugNextHandHoldCandidate;
#endif
#endregion
        public GameItem NearbyItem { get; private set; }
        public bool HasNearbyTarget => candidate != null || NearbyItem != null;
        public bool CanPoke => hand != null && hand.TipTracked && !hasSelection;
        public bool IsPinchGrab => activePinch;
        public bool IntentionalRelease { get; private set; }
        public bool IsTrackingSuspended => trackingSuspendedAt >= 0;
        public bool HasFreshMotion => hand != null && hand.Tracked && hand.MotionValid &&
                                      !IsTrackingSuspended;

        // Reuse the same measured fingers that own selection; pinch is not a bottle squeeze.
        public bool TryGetSqueezeStrength(out float strength)
        {
            strength = 0;
            return hand != null && hand.Tracked && !IsTrackingSuspended &&
                   !activePinch && hand.TryGetGripStrength(gripFingers, out strength);
        }

        public void Configure(PicoTrackedHand source, XRInteractionManager manager,
            PlayerInteractionModeController owner)
        {
            hand = source;
            mode = owner;
            interactionManager = manager;
            keepSelectedTargetValid = true;
            if (graspAttach == null)
            {
                graspAttach = new GameObject("Surface Grasp Attach").transform;
                graspAttach.SetParent(transform, false);
            }
        }

        public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase phase)
        {
            base.PreprocessInteractor(phase);
            if (phase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            RefreshProximity();
            UpdateSelection(Time.unscaledDeltaTime);
            UpdateAttach();
        }

        private void UpdateSelection(float delta)
        {
            selecting = false;
            IntentionalRelease = false;
            if (hand == null || mode != null && mode.PlacementActive ||
                !hasSelection && hand.Poke != null && hand.Poke.HasControlCapture)
            {
                if (hand != null && hand.Poke != null && hand.Poke.HasControlCapture)
                {
                    wasPinching = hand.NativePinching;
                    wasGripping = hand.WorldGrip > .22f;
                    pinchArmed = false;
                }
                armed = false;
                acquisitionSeconds = 0;
                previouslyTracked = false;
                ClearGrabRecovery();
                return;
            }
            var now = Time.unscaledTime;
            var trackingLost = previouslyTracked && !hand.Tracked;
            previouslyTracked = hand.Tracked;
            var assisted = candidate is HandHoldInteractable ||
                candidate is PhysicsGrabInteractable held && held.isSelected ||
                candidate is Component component &&
                component.TryGetComponent<ItemGripAnchor>(out var anchor) &&
                anchor.IsInRange(hand.Palm.position);
            var gripping = hand.WorldGrip >= (assisted ? .32f : .42f);
            var clearlyOpen = hand.WorldGrip <= .22f && !hand.NativePinching;
            if (hand.Tracked && !hand.NativePinching) pinchArmed = true;
            if (hasSelection)
            {
                var strength = 0f;
                var signalValid = activePinch
                    ? hand.NativeTracked
                    : hand.TryGetGripStrength(gripFingers, out strength);
                var closed = activePinch ? hand.NativePinching : strength >= .26f;
                // A fresh release is never delayed by pose recovery or the grace timer.
                if (signalValid && !closed)
                {
                    trackingSuspendedAt = -1;
                    IntentionalRelease = true;
                    armed = true;
                    wasGripping = gripping;
                    wasPinching = hand.NativePinching;
                    acquisitionSeconds = 0;
                    return;
                }
                if (!hand.Tracked || !signalValid ||
                    !hand.TryGetGraspPose(activePinch, out var grasp))
                {
                    KeepSelectionDuringTrackingGap(now);
                    return;
                }
                if (IsTrackingSuspended)
                {
                    if (now - trackingSuspendedAt >= TrackingGraceSeconds ||
                        Vector3.Distance(previousGrasp.position, grasp.position) > RecoveryDistance ||
                        Quaternion.Angle(previousGrasp.rotation, grasp.rotation) > RecoveryAngle)
                    {
                        ReportTrackingRecovery("canceled");
                        return;
                    }
                    // The first restored sample has no trustworthy velocity.
                    if (!hand.MotionValid)
                    {
                        KeepSelectionDuringTrackingGap(now);
                        return;
                    }
                    ReportTrackingRecovery("resumed");
                    trackingSuspendedAt = -1;
                }
                else if (!PicoTrackedHand.IsContinuousMotion(previousGrasp, grasp, delta))
                {
                    KeepSelectionDuringTrackingGap(now);
                    return;
                }
                selecting = true;
                wasGripping = gripping;
                wasPinching = hand.NativePinching;
                acquisitionSeconds = 0;
                return;
            }
            if (recoveryCandidate != null && now >= recoveryDeadline) ClearGrabRecovery();
            if (!hand.Tracked)
            {
                if (!requireOpenAfterTrackingLoss && trackingLost && previousCandidate != null &&
                    (wasGripping || wasPinching || acquisitionSeconds > 0))
                {
                    recoveryCandidate = previousCandidate;
                    recoveryPinch = wasPinching;
                    recoveryDeadline = now + GrabRecoverySeconds;
                }
                armed = false;
                acquisitionSeconds = 0;
                return;
            }
            if (clearlyOpen)
            {
                armed = true;
                requireOpenAfterTrackingLoss = false;
                ClearGrabRecovery();
            }
            if (requireOpenAfterTrackingLoss) return;
            if (recoveryCandidate != null)
            {
                if (candidate != null && !ReferenceEquals(candidate, recoveryCandidate))
                    ClearGrabRecovery();
                else if (hand.MotionValid && ReferenceEquals(candidate, recoveryCandidate) &&
                         (recoveryPinch ? hand.NativePinching : gripping))
                {
                    candidatePinch = recoveryPinch;
                    selecting = HasGripContact(candidate);
                    if (selecting)
                    {
                        ClearGrabRecovery();
                        acquisitionSeconds = 0;
                        wasGripping = gripping;
                        wasPinching = hand.NativePinching;
                        return;
                    }
                }
            }
            if (hand.NativePinching && !wasPinching && pinchArmed ||
                gripping && !wasGripping && armed)
            {
                acquisitionSeconds = .22f;
                armed = false;
                pinchArmed = false;
            }
            wasGripping = gripping;
            wasPinching = hand.NativePinching;
            selecting = (gripping || hand.NativePinching) && (acquisitionSeconds > 0 || assisted) &&
                        candidate != null && HasGripContact(candidate);
            acquisitionSeconds = Mathf.Max(0, acquisitionSeconds - delta);
            if (selecting) acquisitionSeconds = 0;
        }

        private void KeepSelectionDuringTrackingGap(float now)
        {
            armed = false;
            // Character handholding has its own action lifecycle; stale poses
            // must not produce character traction.
            if (!(interactablesSelected[0] is PhysicsGrabInteractable)) return;
            if (!IsTrackingSuspended)
            {
                trackingSuspendedAt = now;
                ReportTrackingRecovery("suspended");
            }
            selecting = now - trackingSuspendedAt < TrackingGraceSeconds;
            if (!selecting) ReportTrackingRecovery("timed_out");
        }

        private void ClearGrabRecovery()
        {
            recoveryCandidate = null;
            recoveryDeadline = 0;
        }

        private void ReportTrackingRecovery(string phase)
            => Debug.Log($"MATESOUL_GRAB_TRACKING hand={name} phase={phase} " +
                         $"gapMs={(Time.unscaledTime - trackingSuspendedAt) * 1000:F0}");

        // UI runs before the XRI manager; use this frame's palm when arbitrating a pinch.
        public void RefreshProximity()
        {
            if (Application.isPlaying && proximityFrame == Time.frameCount) return;
            if (Application.isPlaying) proximityFrame = Time.frameCount;
            candidate = null;
            NearbyItem = null;
            if (hand == null || !hand.Tracked || mode != null && mode.PlacementActive ||
                !hasSelection && hand.Poke != null && hand.Poke.HasControlCapture) return;
            transform.SetPositionAndRotation(hand.Palm.position, hand.Palm.rotation);
            FindCandidate();
        }

        private void FindCandidate()
        {
            var previous = previousCandidate;
            considered.Clear();
            var pinch = hand.NativePinching && hand.TryGetGraspPose(true, out _);
            hand.TryGetGraspPose(pinch, out var grasp);
            var count = Physics.OverlapSphereNonAlloc(grasp.position, reach, overlaps,
                InteractionPhysicsLayers.DirectSelectionMask,
                QueryTriggerInteraction.Collide);
            if (count == overlaps.Length) return;
            var nearest = float.PositiveInfinity;
            var nearestItem = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var shape = overlaps[i];
                var mesh = shape as MeshCollider;
                var point = mesh != null && !mesh.convex
                    ? mesh.bounds.ClosestPoint(grasp.position)
                    : shape.ClosestPoint(grasp.position);
                var offset = point - grasp.position;
                var distance = offset.sqrMagnitude;
                if (distance > reach * reach) continue;
                // A tabletop/wall between the palm and an item must block selection.
                if (offset.sqrMagnitude > .000001f &&
                    Physics.Raycast(grasp.position, offset.normalized, out var hit,
                        offset.magnitude, InteractionPhysicsLayers.DirectSelectionMask,
                        QueryTriggerInteraction.Ignore) &&
                    hit.collider != shape && (shape.attachedRigidbody == null ||
                    hit.collider.attachedRigidbody != shape.attachedRigidbody))
                    continue;
                var item = shape.GetComponentInParent<GameItem>();
                if (item != null && !item.IsHeld && !item.IsReserved && distance < nearestItem)
                {
                    NearbyItem = item;
                    nearestItem = distance;
                }
                if (!interactionManager.TryGetInteractableForCollider(shape, out var interactable) ||
                    !(interactable is IXRSelectInteractable selectable) ||
                    !interactionManager.IsSelectPossible(this, selectable)) continue;
                if (considered.Contains(selectable)) continue;
                considered.Add(selectable);
                // Filter grasp compatibility before ranking: an unsuitable nearer object
                // must not conceal a valid grasp behind it.
                if (!(selectable is XRBaseInteractable target) ||
                    !HasContact(target, pinch, grasp.position)) continue;
                if (!HandGripEvaluator.TryClosestPoint(grasp.position, target.colliders, out point))
                    continue;
                var score = Vector3.Distance(grasp.position, point);
                // Keep only a sub-millimetre tie preference. A handle must not
                // outrank an adjacent object that is closer to the actual grasp.
                if (ReferenceEquals(selectable, previous)) score -= .001f;
                if (score >= nearest) continue;
                candidate = selectable;
                candidatePinch = pinch;
                nearest = score;
            }
            // Stable held tools disable rigidbody contacts to avoid pushing with
            // infinite tracked mass. PhysX overlap queries cannot see them either.
            // Search those selected tools through XRI's existing registry.
            interactionManager.GetRegisteredInteractables(registeredTargets);
            foreach (var value in registeredTargets)
            {
                if (!(value is PhysicsGrabInteractable target) || !target.isSelected ||
                    target.GetComponent<PhysicsBody>().Rigidbody.detectCollisions ||
                    considered.Contains(target) || !interactionManager.IsSelectPossible(this, target) ||
                    !HasContact(target, pinch, grasp.position) ||
                    !HandGripEvaluator.TryClosestPoint(grasp.position, target.colliders, out var point))
                    continue;
                var offset = point - grasp.position;
                var score = offset.magnitude;
                if (score > reach || score > .00001f &&
                    Physics.Raycast(grasp.position, offset / score, score,
                        InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (ReferenceEquals(target, previous)) score -= .001f;
                if (score >= nearest) continue;
                candidate = target;
                candidatePinch = pinch;
                nearest = score;
            }
            previousCandidate = candidate;
#region debug-point C:handhold-candidate
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (Time.unscaledTime >= debugNextHandHoldCandidate)
            {
                debugNextHandHoldCandidate = Time.unscaledTime + .25f;
                HandHoldInteractable nearestSocket = null;
                var nearestSocketDistance = float.PositiveInfinity;
                for (var i = 0; i < count; i++)
                {
                    var socket = overlaps[i] != null
                        ? overlaps[i].GetComponentInParent<HandHoldInteractable>() : null;
                    if (socket == null) continue;
                    var distance = Vector3.Distance(hand.Palm.position, socket.transform.position);
                    if (distance < nearestSocketDistance)
                    {
                        nearestSocketDistance = distance;
                        nearestSocket = socket;
                    }
                }
                var candidateComponent = candidate as Component;
                var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"C\",\"location\":\"PicoDirectHandInteractor.FindCandidate\"," +
                    $"\"msg\":\"[DEBUG] handhold candidate\",\"data\":{{" +
                    $"\"hand\":\"{name.Replace("\"", "'")}\"," +
                    $"\"tracked\":{hand.Tracked.ToString().ToLowerInvariant()}," +
                    $"\"nativePinch\":{hand.NativePinching.ToString().ToLowerInvariant()}," +
                    $"\"worldGrip\":{hand.WorldGrip}," +
                    $"\"candidate\":\"{(candidateComponent != null ? candidateComponent.name.Replace("\"", "'") : "none")}\"," +
                    $"\"selectActive\":{isSelectActive.ToString().ToLowerInvariant()}," +
                    $"\"hasSelection\":{hasSelection.ToString().ToLowerInvariant()}," +
                    $"\"socket\":\"{(nearestSocket != null ? nearestSocket.name.Replace("\"", "'") : "none")}\"," +
                    $"\"socketSide\":\"{(nearestSocket != null ? nearestSocket.Side.ToString() : "none")}\"," +
                    $"\"socketDistance\":{(nearestSocket != null ? nearestSocketDistance : -1f)}," +
                    $"\"socketInRange\":{(nearestSocket != null && nearestSocket.IsWithinHoldRange(this)).ToString().ToLowerInvariant()}," +
                    $"\"overlaps\":{count}}}}}";
                var request = UnityWebRequest.Post(
                    "http://127.0.0.1:7781/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
        }

        private IXRSelectInteractable previousCandidate;

        public override bool isSelectActive => selecting;

        public override bool CanSelect(IXRSelectInteractable interactable)
            => base.CanSelect(interactable) && (!hasSelection || IsSelecting(interactable));

        public override void GetValidTargets(List<IXRInteractable> targets)
        {
            targets.Clear();
            if (candidate != null) targets.Add(candidate);
        }

        public bool TryGetHandHoldPose(out Pose pose)
        {
            pose = hand == null ? default : hand.Palm;
            return isActiveAndEnabled && hand != null && hand.Tracked;
        }

        public bool HasGripContact(IXRSelectInteractable interactable)
        {
            if (hand == null || !hand.Tracked ||
                !(interactable is XRBaseInteractable target))
                return false;
            var pinch = hasSelection ? activePinch : candidatePinch;
            return hand.TryGetGraspPose(pinch, out var pose) &&
                   HasContact(target, pinch, pose.position);
        }

        private bool HasContact(XRBaseInteractable target, bool pinch, Vector3 point)
        {
            if (target is HandHoldInteractable socket) return socket.IsWithinHoldRange(this);
            if (target.TryGetComponent<ItemGripAnchor>(out var anchor) &&
                anchor.IsInRange(hand.Palm.position)) return true;
            return pinch
                ? HandGripEvaluator.Distance(point, target.colliders) <= .075f
                : hand.EvaluateGrip(target.colliders, .03f).IsGrip &&
                  HandGripEvaluator.Distance(point, target.colliders) <= .095f;
        }

        public override Transform GetAttachTransform(IXRInteractable interactable)
            => interactable is HandHoldInteractable || graspAttach == null
                ? transform : graspAttach;

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            trackingSuspendedAt = -1;
            ClearGrabRecovery();
            activePinch = candidatePinch;
            gripFingers = hand == null ? 0 : hand.CaptureGripFingers();
            surfaceOffset = Vector3.zero;
            IntentionalRelease = false;
            if (hand != null) hand.TryGetGraspPose(activePinch, out previousGrasp);
            UpdateAttach();
            base.OnSelectEntering(args);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (IsTrackingSuspended && !IntentionalRelease)
            {
                requireOpenAfterTrackingLoss = true;
                armed = pinchArmed = false;
                ClearGrabRecovery();
            }
            trackingSuspendedAt = -1;
            SetHeldBody(null);
            stableGrip = null;
            surfaceOffset = Vector3.zero;
            activePinch = false;
            gripFingers = 0;
        }

        private void UpdateAttach()
        {
            if (IsTrackingSuspended || hand == null || graspAttach == null ||
                !hand.TryGetGraspPose(activePinch, out var pose)) return;
            previousGrasp = pose;
            if (stableGrip != null)
            {
                graspAttach.SetPositionAndRotation(hand.Palm.position, hand.Palm.rotation * stableFrame);
                return;
            }
            graspAttach.SetPositionAndRotation(
                pose.position + pose.rotation * surfaceOffset, pose.rotation);
        }

        public Pose AttachToSurface(PhysicsBody body, ItemInteractionProfile profile)
        {
            stableGrip = body.GetComponent<ItemGripAnchor>();
            if (stableGrip != null && stableGrip.IsInRange(hand.Palm.position))
            {
                hand.TryGetStableGripPose(out var grip);
                stableFrame = Quaternion.Inverse(hand.Palm.rotation) * grip.rotation;
                surfaceOffset = Vector3.zero;
                UpdateAttach();
                return new Pose(graspAttach.position, graspAttach.rotation);
            }
            stableGrip = null;
            hand.TryGetGraspPose(activePinch, out var raw);
            var point = raw.position;
            HandGripEvaluator.TryClosestPoint(raw.position, body.Shapes, out point);
            if (profile != null && profile.gripKind != ItemGripKind.Generic)
            {
                var preferred = body.transform.TransformPoint(profile.gripPosition);
                if (Vector3.Distance(preferred, raw.position) < .03f &&
                    HandGripEvaluator.TryClosestPoint(preferred, body.Shapes, out var onSurface))
                    point = onSurface;
            }
            // Align the hand to a nearby surface; preserve the item's initial world pose.
            surfaceOffset = Quaternion.Inverse(raw.rotation) * (point - raw.position);
            UpdateAttach();
            return new Pose(graspAttach.position, graspAttach.rotation);
        }

        public void GetFollowMotion(Vector3 itemPosition, out Vector3 velocity, out Vector3 angular)
        {
            angular = HasFreshMotion ? hand.PalmAngularVelocity : Vector3.zero;
            velocity = HasFreshMotion ? hand.PalmVelocity +
                Vector3.Cross(angular, itemPosition - hand.Palm.position) : Vector3.zero;
        }

        public bool IsTouching(
            XRBaseInteractable interactable,
            float allowance = 0)
        {
            if (hand == null || !hand.Tracked || interactable == null)
                return false;
            var contact = hand.EvaluateGrip(
                interactable.colliders, Mathf.Max(0, allowance));
            return contact.ContactCount > 0 ||
                   contact.ClosestDistance <=
                   HandGripEvaluator.FallbackPalmDistance + allowance;
        }

        public void SetHeldBody(PhysicsBody body)
        {
            hand?.PhysicsProxy?.SetHeldBody(body);
            hand?.ContactVisual?.SetHeldBody(body,
                hand == null ? Vector3.zero : hand.Palm.rotation * surfaceOffset, activePinch,
                body != null ? stableGrip : null,
                graspAttach != null ? new Pose(graspAttach.position, graspAttach.rotation) : default);
        }

        protected override void OnDisable()
        {
            armed = selecting = false;
            wasGripping = wasPinching = pinchArmed = false;
            previouslyTracked = false;
            requireOpenAfterTrackingLoss = false;
            trackingSuspendedAt = -1;
            ClearGrabRecovery();
            acquisitionSeconds = 0;
            proximityFrame = -1;
            candidate = null;
            previousCandidate = null;
            NearbyItem = null;
            SetHeldBody(null);
            stableGrip = null;
            base.OnDisable();
        }
    }
}
