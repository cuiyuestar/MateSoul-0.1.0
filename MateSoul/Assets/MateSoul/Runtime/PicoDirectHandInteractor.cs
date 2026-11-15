using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    /// <summary>Near-only grasp input; XRI owns selection and PhysicsBody owns object motion.</summary>
    public sealed class PicoDirectHandInteractor : XRBaseInteractor, IHandHoldPoseProvider
    {
        private const float TrackingGraceSeconds = .3f;
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
        private Vector3 pendingSnap;
        private Pose previousGrasp;
        private bool candidatePinch, activePinch;
        private int gripFingers;
        private bool armed, selecting;
        private float acquisitionSeconds;
        private bool wasGripping, wasPinching, pinchArmed;
        private bool previouslyTracked;
        private bool requireOpenAfterTrackingLoss;
        private bool requireOpenAfterRelease;
        private string releaseReason = "none";
        private float nextRejectLog;
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
            // A loose curl has to stay a loose curl: only a real grasp closes the
            // hand far enough to pick something up.
            var gripping = hand.WorldGrip >= (assisted ? .45f : .55f);
            var clearlyOpen = hand.WorldGrip <= .35f && !hand.NativePinching;
            if (hand.Tracked && !hand.NativePinching) pinchArmed = true;
            if (hasSelection)
            {
                var strength = 0f;
                var signalValid = activePinch
                    ? hand.NativeTracked
                    : hand.TryGetGripStrength(gripFingers, out strength);
                // The held item can occlude the fingers that were captured at grab
                // time. Fall back to the aggregate grip instead of holding the
                // release back until those joints are visible again.
                if (!signalValid && hand.Tracked && !activePinch)
                {
                    strength = hand.WorldGrip;
                    signalValid = true;
                }
                var closed = activePinch ? hand.NativePinching : strength >= .26f;
                // A fresh release is never delayed by pose recovery or the grace timer.
                if (hand.Tracked && signalValid && !closed)
                {
                    trackingSuspendedAt = -1;
                    IntentionalRelease = true;
                    armed = true;
                    requireOpenAfterRelease = true;
                    releaseReason = activePinch ? "pinch-open" : "grip-open";
                    wasGripping = gripping;
                    wasPinching = hand.NativePinching;
                    acquisitionSeconds = 0;
                    Debug.Log($"MATESOUL_GRAB_RELEASE hand={name} item={SelectedName()} " +
                              $"reason={releaseReason} strength={strength:F2} " +
                              $"grip={hand.WorldGrip:F2} pinch={hand.NativePinching} " +
                              $"motion={hand.MotionValid}");
                    return;
                }
                if (!hand.Tracked || !signalValid ||
                    !hand.TryGetGraspPose(activePinch, out var grasp))
                {
                    releaseReason = "tracking-gap";
                    KeepSelectionDuringTrackingGap(now);
                    return;
                }
                if (IsTrackingSuspended)
                {
                    if (now - trackingSuspendedAt >= TrackingGraceSeconds ||
                        Vector3.Distance(previousGrasp.position, grasp.position) > RecoveryDistance ||
                        Quaternion.Angle(previousGrasp.rotation, grasp.rotation) > RecoveryAngle)
                    {
                        releaseReason = "tracking-timeout";
                        ReportTrackingRecovery("canceled");
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
            if (!gripping && !hand.NativePinching) requireOpenAfterRelease = false;
            if (requireOpenAfterTrackingLoss || requireOpenAfterRelease) return;
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
            // A pinch that lands on a radio knob belongs to the knob. Without this
            // guard the same gesture picked the radio up and the knob could never
            // be turned while it sat on a table.
            if (hand.NativePinching && PinchTurnsKnob())
            {
                armed = false;
                pinchArmed = false;
                wasGripping = gripping;
                wasPinching = true;
                acquisitionSeconds = 0;
                return;
            }
            // The player is usually already half-closed when the hand reaches an
            // item, so a held pickup gesture on contact is the grab intent. It still
            // has to follow a relaxed hand: a hand that stays curled and merely
            // drifts past something must not snap it up.
            var holding = (gripping || hand.NativePinching) && candidate != null &&
                          (armed || assisted) && HasGripContact(candidate);
            var pinchEdge = hand.NativePinching && !wasPinching && pinchArmed;
            var gripEdge = gripping && !wasGripping && armed;
            if (pinchEdge || gripEdge || holding)
            {
                acquisitionSeconds = .22f;
                armed = false;
                pinchArmed = false;
                var reason = pinchEdge ? "pinch-edge" : gripEdge ? "grip-edge" : "hold-contact";
                var target = candidate as Component;
                Debug.Log($"MATESOUL_GRAB_TRIGGER hand={Side} " +
                          $"item={(target == null ? "none" : target.name)} reason={reason} " +
                          $"grip={hand.WorldGrip:F2} pinch={hand.NativePinching} " +
                          $"assisted={assisted}");
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

        private string SelectedName()
            => interactablesSelected.Count > 0 ? NameOf(interactablesSelected[0]) : "none";

        private static string NameOf(IXRSelectInteractable value)
            => value is Component component ? component.name : "none";

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
            var rejected = "none";
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
                var item = shape.GetComponentInParent<GameItem>();
                if (item != null && !item.IsHeld && !item.IsReserved && distance < nearestItem)
                {
                    // Record the hint before occlusion filtering so an item sunk
                    // into a scanned table can still report why it is unreachable.
                    NearbyItem = item;
                    nearestItem = distance;
                }
                // A tabletop/wall between the palm and an item must block
                // selection, unless the item is embedded in that surface.
                if (IsOccluded(grasp.position, shape, offset))
                {
                    rejected = "occluded";
                    continue;
                }
                if (!interactionManager.TryGetInteractableForCollider(shape, out var interactable) ||
                    !(interactable is IXRSelectInteractable selectable) ||
                    !interactionManager.IsSelectPossible(this, selectable))
                {
                    rejected = "not-selectable";
                    continue;
                }
                if (considered.Contains(selectable)) continue;
                considered.Add(selectable);
                // Filter grasp compatibility before ranking: an unsuitable nearer object
                // must not conceal a valid grasp behind it.
                if (!(selectable is XRBaseInteractable target) ||
                    !HasContact(target, pinch, grasp.position))
                {
                    rejected = "no-contact";
                    continue;
                }
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
            if (candidate == null && NearbyItem != null && Time.unscaledTime >= nextRejectLog)
            {
                nextRejectLog = Time.unscaledTime + .5f;
                Debug.Log($"MATESOUL_GRAB_REJECT hand={name} item={NearbyItem.name} " +
                          $"reason={rejected} grip={hand.WorldGrip:F2} " +
                          $"pinch={hand.NativePinching} overlaps={count}");
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

        // Scanned room surfaces are coarse: a small item can sit slightly inside a
        // tabletop. Do not let that static surface hide an item that is already
        // touching or embedded in it, otherwise the item becomes ungrabble.
        private static bool IsOccluded(Vector3 origin, Collider shape, Vector3 offset)
        {
            var distance = offset.magnitude;
            if (distance <= .000001f) return false;
            if (!Physics.Raycast(origin, offset / distance, out var hit, distance,
                    InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider == shape) return false;
            if (shape.attachedRigidbody != null &&
                hit.collider.attachedRigidbody == shape.attachedRigidbody) return false;
            // A movable blocker is real geometry, not a coarse scan artefact.
            if (hit.collider.attachedRigidbody != null) return true;
            if (distance <= .06f) return false;
            var convex = shape is BoxCollider || shape is SphereCollider ||
                         shape is CapsuleCollider || shape is MeshCollider mesh && mesh.convex;
            return !(convex && Physics.ComputePenetration(shape, shape.transform.position,
                       shape.transform.rotation, hit.collider, hit.collider.transform.position,
                       hit.collider.transform.rotation, out _, out var depth) && depth > .001f);
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
            // The fingers decide what is being picked up, not the palm: at least two
            // contact points (a finger plus the thumb or the palm) have to sit on the
            // item itself. That is what stops a loosely curled hand from snapping up
            // whatever happens to be next to it.
            var distance = HandGripEvaluator.Distance(point, target.colliders);
            if (pinch) return distance <= .075f;
            var contact = hand.EvaluateGrip(target.colliders);
            return contact.IsGrip && contact.ContactCount >= 2 && distance <= .09f;
        }

        // A pinching hand over a radio knob is turning it, not picking the radio up.
        private bool PinchTurnsKnob()
            => hand != null && hand.TryGetGraspPose(true, out var pinch) &&
               RadioKnobControl.FindNearby(pinch.position, -1) != null;

        private string Side => hand != null && hand.IsLeft ? "Left" : "Right";

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
            releaseReason = "none";
            if (hand != null) hand.TryGetGraspPose(activePinch, out previousGrasp);
            UpdateAttach();
            base.OnSelectEntering(args);
            Debug.Log($"MATESOUL_GRAB_SELECT hand={name} item={NameOf(args.interactableObject)} " +
                      $"pinch={activePinch} grip={(hand == null ? 0 : hand.WorldGrip):F2} " +
                      $"stable={stableGrip != null} tracked={hand != null && hand.Tracked}");
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            var suspended = IsTrackingSuspended;
            base.OnSelectExited(args);
            Debug.Log($"MATESOUL_GRAB_EXIT hand={name} item={NameOf(args.interactableObject)} " +
                      $"reason={releaseReason} intentional={IntentionalRelease} " +
                      $"suspended={suspended}");
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
                pendingSnap = Vector3.zero;
                UpdateAttach();
                return new Pose(graspAttach.position, graspAttach.rotation);
            }
            stableGrip = null;
            pendingSnap = Vector3.zero;
            // Without a live grasp pose there is nothing to align to; never derive a
            // snap from a default pose or the item would fly to the world origin.
            if (!hand.TryGetGraspPose(activePinch, out var raw))
            {
                surfaceOffset = Vector3.zero;
                UpdateAttach();
                return new Pose(graspAttach.position, graspAttach.rotation);
            }
            var point = raw.position;
            HandGripEvaluator.TryClosestPoint(raw.position, body.Shapes, out point);
            if (profile != null && profile.gripKind != ItemGripKind.Generic)
            {
                var preferred = body.transform.TransformPoint(profile.gripPosition);
                if (Vector3.Distance(preferred, raw.position) < .03f &&
                    HandGripEvaluator.TryClosestPoint(preferred, body.Shapes, out var onSurface))
                    point = onSurface;
            }
            // Pickup pulls the item into the hand instead of asking the fingers to
            // reach out to it: the grabbed surface point lands on the grip point and
            // the item keeps its world orientation. The frozen relative pose is what
            // the hand then carries, so no per-frame fitting is needed. The actual
            // move is deferred until the hand ignores the body, otherwise the hand's
            // own colliders read as blockers.
            pendingSnap = body.IsFollowing ? Vector3.zero : raw.position - point;
            surfaceOffset = Vector3.zero;
            UpdateAttach();
            return new Pose(graspAttach.position, graspAttach.rotation);
        }

        /// <summary>Pull the grabbed item into the hand after the hand ignores it.</summary>
        public void ApplyPendingSnap(PhysicsBody body)
        {
            var snap = pendingSnap;
            pendingSnap = Vector3.zero;
            if (body == null || body.IsFollowing || snap.sqrMagnitude <= .000001f) return;
            var snapped = new Pose(body.Pose.position + snap, body.Pose.rotation);
            // A coarse room scan can sink an item into a table. A blocked
            // destination is only rejected when the item is not already blocked
            // where it stands, so an embedded item can still be lifted out.
            if (!body.CanOccupy(snapped) && body.CanOccupy(body.Pose)) return;
            body.Place(snapped);
            Debug.Log($"MATESOUL_GRAB_SNAP hand={name} item={body.name} " +
                      $"offset={snap.magnitude:F3}");
        }

        public void GetFollowMotion(Vector3 itemPosition, out Vector3 velocity, out Vector3 angular)
        {
            if (HasFreshMotion)
            {
                angular = hand.PalmAngularVelocity;
                velocity = hand.PalmVelocity + Vector3.Cross(
                    angular, itemPosition - hand.Palm.position);
                return;
            }
            // A dropped tracking sample must not freeze the held item mid-air.
            if (hand != null && hand.TryGetRecentMotion(.1f, out var recent, out var recentAngular))
            {
                angular = recentAngular;
                velocity = recent + Vector3.Cross(angular, itemPosition - hand.Palm.position);
                return;
            }
            velocity = angular = Vector3.zero;
        }

        /// <summary>Release motion, including the last trustworthy sample before a tracking gap.</summary>
        public bool TryGetReleaseMotion(Vector3 itemPosition,
            out Vector3 velocity, out Vector3 angular)
        {
            velocity = angular = Vector3.zero;
            if (hand == null) return false;
            // The throw speed comes from a short recency-weighted window, not the
            // single release frame, so a swing keeps its real momentum.
            if (hand.TryGetThrowMotion(out velocity, out angular))
            {
                velocity += Vector3.Cross(angular, itemPosition - hand.Palm.position);
                return true;
            }
            if (!hand.TryGetRecentMotion(.18f, out var recent, out var recentAngular))
                return false;
            angular = recentAngular;
            velocity = recent + Vector3.Cross(angular, itemPosition - hand.Palm.position);
            return true;
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
            requireOpenAfterRelease = false;
            releaseReason = "none";
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
