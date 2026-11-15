using System;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    // One free hand transports one item at a time; already staged items remain reserved.
    public sealed class ItemTransferStep : IItemActionStep
    {
        public const float MaximumPickupSnapDistance = .25f;
        public const float FallbackPickupSnapDistance = .65f;
        private const float ApproachSampleDistance = .18f;
        private const float ApproachCollisionTolerance = .04f;
        private const float ApproachArrivalDistance = .18f;
        private const float DockPositionTolerance = .15f;
        private const float PlacementContactTolerance = .04f;

        public enum PickupSnapLayer { None, Contact, Primary, Fallback }

        public static PickupSnapLayer SelectPickupSnapLayer(float primaryError, float secondaryError = 0)
        {
            var error = Mathf.Max(primaryError, secondaryError);
            if (error <= PickupAnimationSet.ContactTolerance) return PickupSnapLayer.Contact;
            if (error <= MaximumPickupSnapDistance) return PickupSnapLayer.Primary;
            if (error <= FallbackPickupSnapDistance) return PickupSnapLayer.Fallback;
            return PickupSnapLayer.None;
        }

        public static bool CanSnapPickup(float primaryError, float secondaryError = 0)
            => SelectPickupSnapLayer(primaryError, secondaryError) != PickupSnapLayer.None;

        private enum Phase { Approach, Dock, Reach, Lift, PickupBlend, PickupUse, PickupSettle, Hold, Carry, DockTarget, Place, Release, Retract }
        private readonly GameSession session;
        private readonly AvatarMotor motor;
        private readonly AvatarItemRig rig;
        private readonly ItemActionBinding binding;
        private Func<Pose> destination;
        private readonly Vector3 destinationDock;
        private readonly bool naturalRelease;
        private readonly bool pickupOnly;
        private readonly bool finishAfterPickup;
        private readonly bool retainWhenDone;
        private bool alreadyHeld;
        private readonly ItemUseAnimation pickupUseAnimation;
        private bool fromHeld;
        private readonly Guid run;
        private Phase phase;
        private MoveAction move;
        private Pose source, drivenGrip;
        private Vector3 sourceDock, approachItemPosition;
        private Quaternion sourceFacing, targetFacing;
        private ItemMotion reachMotion;
        private PickupPerformance pickup;
        private float elapsed;
        private float nextMotionReport;
        private float debugNextCarryReport;
#region debug-point C-D-E:held-follow
#if UNITY_EDITOR
        private float debugNextHeldFollowReport;
#endif
#endregion
#region debug-point B-C-D-E:item-action-jitter
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextPlacementJitterReport;
#endif
#endregion
        private bool active, contactSettled, fallbackPending, hasDrivenGrip;
        private bool releasedForAlignment;
        private bool stationaryAttention;
        private readonly string label;
        private readonly string heldLabel;
        public string Label => phase == Phase.Hold
            ? heldLabel
            : phase == Phase.PickupUse && pickupUseAnimation != null
                ? pickupUseAnimation.actionLabel : label;
        public bool IsHolding => active && phase == Phase.Hold;

        // Continues the running pickup instead of cancelling it: releasing the
        // lease would drop the item, so the hold is retargeted in place.
        public bool TryRetarget(Func<Pose> newDestination, out string error)
        {
            error = null;
            if (!IsHolding || newDestination == null)
            {
                error = "物品当前无法重新放置。";
                return false;
            }
            if (binding.Lease.State != ItemControlState.HeldByCharacter)
            {
                error = "物品已不在冷月手中。";
                return false;
            }
            var previousDestination = destination;
            var previousPickup = pickup;
            var previousDock = sourceDock;
            var previousFacing = sourceFacing;
            var previousFromHeld = fromHeld;
            var previousActive = active;
            try
            {
                destination = newDestination;
                var target = newDestination();
                var returnHeight = binding.Profile.WorldGrip(target).position.y -
                    session.Navigation.FloorHeight;
                pickup = returnHeight < .8f
                    ? rig.PickupAnimations.Select(returnHeight, rig.transform.lossyScale.y)
                    : null;
                fromHeld = true;
                sourceDock = FindApproach(target);
                rig.Motion(ItemMotion.Carry, 1);
                BeginWalk(sourceDock);
                Next(Phase.Carry);
                return true;
            }
            catch (Exception exception)
            {
                destination = previousDestination;
                pickup = previousPickup;
                sourceDock = previousDock;
                sourceFacing = previousFacing;
                fromHeld = previousFromHeld;
                active = previousActive;
                phase = Phase.Hold;
                error = exception.Message;
                return false;
            }
        }

        public ItemTransferStep(string label, GameSession session, AvatarItemRig rig,
            ItemActionBinding binding, Guid run, bool finishAfterPickup = false,
            bool retainWhenDone = false)
            : this(label, session, rig, binding, run, () => binding.View.Body.Pose, Vector3.zero)
        {
            pickupOnly = true;
            this.finishAfterPickup = finishAfterPickup;
            this.retainWhenDone = retainWhenDone;
            var animation = binding.View.Definition.UseAnimation;
            // Only the indefinite hold pickup performs the authored pickup
            // animation; use-item and radio actions drive their own use step.
            pickupUseAnimation = !finishAfterPickup && animation != null &&
                animation.playOnPickup && animation.clip != null ? animation : null;
        }

        public ItemTransferStep(string label, GameSession session, AvatarItemRig rig,
            ItemActionBinding binding, Guid run, Func<Pose> destination,
            Vector3 destinationDock, bool naturalRelease = false, bool fromHeld = false)
        {
            this.label = label; this.session = session; motor = (AvatarMotor)session.Motor;
            heldLabel = "手里拿着" + binding.View.Definition.DisplayName;
            this.rig = rig; this.binding = binding; this.run = run;
            this.destination = destination; this.destinationDock = destinationDock;
            this.naturalRelease = naturalRelease;
            this.fromHeld = fromHeld;
        }

        public void Start()
        {
            if (binding.View.IsFixedAfterPlacement)
                throw new InvalidOperationException(
                    $"{binding.View.Definition.DisplayName}放置后不可搬动。");
            // A previous command already put this item in her hand, so the pickup
            // phase is skipped and the action continues where it left off.
            if (pickupOnly && !fromHeld && rig.IsHoldingFor(run, binding.View.Body))
            {
                alreadyHeld = true;
                active = true;
                phase = Phase.Hold;
                // The item is already carried; mirror the state a pickup would set.
                if (binding.Lease != null &&
                    binding.Lease.State != ItemControlState.HeldByCharacter)
                    binding.Lease.Transition(binding.Lease.State, ItemControlState.HeldByCharacter);
                if (retainWhenDone) rig.RetainOnEnd(run);
                return;
            }
            if (fromHeld)
            {
                if (binding.Lease.State != ItemControlState.HeldByCharacter)
                    throw new InvalidOperationException("物品已不在冷月手中。");
                var returnHeight = binding.Profile.WorldGrip(destination()).position.y -
                    session.Navigation.FloorHeight;
                // Low returns need the authored crouch; surfaces in normal arm
                // range use the existing placement posture and torso reach. A bed
                // or low table sits below standing arm reach once the hand offset
                // is added, so it needs the crouch.
                pickup = returnHeight < .8f
                    ? rig.PickupAnimations.Select(returnHeight, rig.transform.lossyScale.y)
                    : null;
                drivenGrip = rig.HandGrip;
                hasDrivenGrip = true;
                sourceDock = FindApproach(destination());
                BeginWalk(sourceDock);
                phase = Phase.Carry;
                active = true;
                return;
            }
            if (!binding.Profile.RequiresTwoHands)
            {
                if (!rig.PickupAnimations)
                    throw new InvalidOperationException("角色缺少拾取动画配置。");
                pickup = rig.PickupAnimations.Select(
                    binding.Profile.WorldGrip(binding.View.Body.Pose).position.y -
                    session.Navigation.FloorHeight, rig.transform.lossyScale.y);
            }
            else if (binding.View.Body.Pose.position.y - session.Navigation.FloorHeight > 1.15f)
                throw new InvalidOperationException("物品过高，冷月无法取到。");
            sourceDock = FindApproach();
#region debug-point D-E:transfer-start
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugDestination = destination();
            var debugStartPayload =
                $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"D-E\",\"location\":\"ItemTransferStep.Start\"," +
                $"\"msg\":\"[DEBUG] transfer step start\",\"data\":{{" +
                $"\"label\":\"{Label}\",\"item\":\"{binding.View.Definition.Id}\"," +
                $"\"source\":{JsonUtility.ToJson(binding.View.Body.Pose.position)}," +
                $"\"destination\":{JsonUtility.ToJson(debugDestination.position)}," +
                $"\"positionError\":{Vector3.Distance(binding.View.Body.Pose.position, debugDestination.position)}," +
                $"\"rotationError\":{Quaternion.Angle(binding.View.Body.Pose.rotation, debugDestination.rotation)}}}}}";
            var debugStartRequest = UnityEngine.Networking.UnityWebRequest.Post(
                Application.isEditor
                    ? "http://127.0.0.1:7781/event"
                    : "http://127.0.0.1:7781/event",
                debugStartPayload, "application/json");
            debugStartRequest.SendWebRequest().completed += _ => debugStartRequest.Dispose();
#endif
#endregion
            BeginWalk(sourceDock);
            phase = Phase.Approach;
            active = true;
        }

        private Vector3 FindApproach(Pose? itemPose = null)
        {
            var poseToApproach = itemPose ?? binding.View.Body.Pose;
            approachItemPosition = poseToApproach.position;
            var position = pickup != null
                ? binding.Profile.WorldGrip(poseToApproach).position
                : approachItemPosition;
            var initial = Vector3.ProjectOnPlane(motor.Position - position, Vector3.up).normalized;
            if (initial.sqrMagnitude < .01f) initial = Vector3.back;
            var sampled = 0;
            var offStand = 0;
            var outOfReach = 0;
            var pathRejected = 0;
            var occupancyRejected = 0;
            // Preserve the original authored stand positions first. Only after
            // every one fails, project to a nearby walkable point whose actual
            // grasp error fits the existing final pickup fallback. Returning an
            // item sweeps a wider ring: the destination may sit in the middle of
            // a large surface, so the floor is further out than a pickup needs.
            var rings = fromHeld ? 10 : pickup != null ? 7 : 5;
            for (var pass = 0; pass < (pickup != null && !fromHeld ? 2 : 1); pass++)
            for (var ring = 0; ring < rings; ring++)
                for (var i = 0; i < 16; i++)
                {
                    var direction = Quaternion.Euler(0, i * 22.5f, 0) * initial;
                    var facing = Quaternion.LookRotation(-direction);
                    var candidate = pickup != null
                        ? position - facing * Vector3.Scale(pickup.contactGrip, rig.transform.lossyScale) +
                          direction * (ring * .075f)
                        : position + direction * (.44f + ring * .1f);
                    candidate.y = session.Navigation.FloorHeight;
                    // Returning an item only needs a reachable stand point beside
                    // the destination; the navmesh is eroded around furniture, so a
                    // tight pickup-sized tolerance rejects every candidate.
                    var sampleRange = fromHeld
                        ? FallbackPickupSnapDistance
                        : pass == 0 ? ApproachSampleDistance : FallbackPickupSnapDistance;
                    if (!session.Navigation.TrySample(candidate, sampleRange, out var floor)) continue;
                    sampled++;
                    if (Vector3.Distance(floor, candidate) > sampleRange)
                    {
                        offStand++;
                        continue;
                    }
                    if (pass != 0)
                    {
                        var toGrip = Vector3.ProjectOnPlane(position - floor, Vector3.up);
                        if (toGrip.sqrMagnitude < .0001f) continue;
                        var local = Vector3.Scale(pickup.contactGrip, rig.transform.lossyScale);
                        facing = Quaternion.LookRotation(toGrip) *
                            Quaternion.Euler(0, -Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, 0);
                        if (!CanReachFromApproach(floor, facing, position, pickup, rig.transform.lossyScale))
                        {
                            outOfReach++;
                            continue;
                        }
                    }
                    if (!session.Navigation.TryPath(motor.Position, floor, out _))
                    {
                        pathRejected++;
                        continue;
                    }
                    var pose = new Pose(floor + Vector3.up * .025f,
                        facing);
                    if (!motor.Body.CanOccupy(pose, ApproachCollisionTolerance,
                        ignoredRoot: fromHeld ? binding.View.transform : null))
                    {
                        occupancyRejected++;
                        continue;
                    }
                    sourceFacing = facing;
                    return floor;
                }
            Debug.LogWarning($"MATESOUL_ITEM_APPROACH failed item={binding.View.Definition.Id} " +
                $"sampled={sampled} offStand={offStand} handOutOfRange={outOfReach} " +
                $"pathRejected={pathRejected} occupancyRejected={occupancyRejected}");
            throw new InvalidOperationException(fromHeld
                ? "无法走到放置点旁边。"
                : $"无法走到{binding.View.Definition.DisplayName}旁边。");
        }

        public static bool CanReachFromApproach(Vector3 floor, Quaternion facing,
            Vector3 grip, PickupPerformance animation, Vector3 characterScale)
        {
            var expectedGrip = floor + Vector3.up * .025f +
                facing * Vector3.Scale(animation.contactGrip, characterScale);
            // The contact solver runs before the three snap gates and can
            // correct up to 15cm. Admission estimates its residual; the actual
            // solved hand must still pass the unchanged 65cm final snap gate.
            return Mathf.Max(0, Vector3.Distance(expectedGrip, grip) -
                PickupAnimationSet.MaximumCorrection) <= FallbackPickupSnapDistance;
        }

        private void BeginWalk(Vector3 point)
        {
            SetStationaryAttention(false);
            move?.Dispose();
            move = new MoveAction(motor, session.Navigation, () => point, ApproachArrivalDistance);
            var error = move.Check();
#region debug-point F:navigation-path
#if UNITY_EDITOR
            session.Navigation.TryPath(motor.Position, point, out var debugCorners);
            var corners = debugCorners == null
                ? "null"
                : string.Join(",", Array.ConvertAll(debugCorners, value => JsonUtility.ToJson(value)));
            var payload = $"{{\"sessionId\":\"item-reach-layer\",\"runId\":\"pre-nav-fix\",\"hypothesisId\":\"F\",\"location\":\"ItemTransferStep.BeginWalk\",\"msg\":\"[DEBUG] navigation path\",\"data\":{{\"phase\":\"{phase}\",\"origin\":{JsonUtility.ToJson(motor.Position)},\"requested\":{JsonUtility.ToJson(point)},\"error\":\"{error ?? ""}\",\"corners\":[{(corners == "null" ? "" : corners)}]}}}}";
            var request = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", payload, "application/json");
            request.SendWebRequest().completed += _ => request.Dispose();
#endif
#endregion
            if (error != null) throw new InvalidOperationException("物品搬运路线不可达。");
            move.Start();
        }

        private void Next(Phase value)
        {
#region debug-point A-C-E:attention-phase-transition
#if UNITY_EDITOR
            var debugAttentionPhasePayload =
                $"{{\"sessionId\":\"item-attention-head-snap\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"A-C-E\",\"location\":\"ItemTransferStep.Next\"," +
                $"\"msg\":\"[DEBUG] item transfer phase transition\",\"data\":{{" +
                $"\"frame\":{Time.frameCount},\"item\":\"{binding.View.Definition.Id}\"," +
                $"\"from\":\"{phase}\",\"to\":\"{value}\"," +
                $"\"stationaryAttention\":{stationaryAttention.ToString().ToLowerInvariant()}," +
                $"\"bodyRotation\":{JsonUtility.ToJson(motor.Body.Pose.rotation)}}}}}";
            var debugAttentionPhaseRequest = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7779/event", debugAttentionPhasePayload, "application/json");
            debugAttentionPhaseRequest.SendWebRequest().completed +=
                _ => debugAttentionPhaseRequest.Dispose();
#endif
#endregion
#region debug-point B-C-E:phase-transition
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugBody = binding.View.Body;
            var debugPhasePayload =
                $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"B-C-E\",\"location\":\"ItemTransferStep.Next\"," +
                $"\"msg\":\"[DEBUG] transfer phase transition\",\"data\":{{" +
                $"\"item\":\"{binding.View.Definition.Id}\",\"from\":\"{phase}\",\"to\":\"{value}\"," +
                $"\"position\":{JsonUtility.ToJson(debugBody.Pose.position)}," +
                $"\"rotation\":{JsonUtility.ToJson(debugBody.Pose.rotation)}," +
                $"\"velocity\":{JsonUtility.ToJson(debugBody.Rigidbody.velocity)}," +
                $"\"angularVelocity\":{JsonUtility.ToJson(debugBody.Rigidbody.angularVelocity)}," +
                $"\"controlled\":{debugBody.IsInteractionControlled.ToString().ToLowerInvariant()}," +
                $"\"collisions\":{debugBody.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                $"\"kinematic\":{debugBody.Rigidbody.isKinematic.ToString().ToLowerInvariant()}}}}}";
            var debugPhaseRequest = UnityEngine.Networking.UnityWebRequest.Post(
                Application.isEditor
                    ? "http://127.0.0.1:7781/event"
                    : "http://127.0.0.1:7781/event",
                debugPhasePayload, "application/json");
            debugPhaseRequest.SendWebRequest().completed += _ => debugPhaseRequest.Dispose();
#endif
#endregion
            // Phase progression is the only way to see where an item action stopped
            // once it leaves the log-friendly startup window.
            Debug.Log($"MATESOUL_ITEM_PHASE item={binding.View.Definition.Id} " +
                $"from={phase} to={value} elapsed={elapsed:F2} " +
                $"pickupOnly={pickupOnly} finishAfterPickup={finishAfterPickup} " +
                $"use={(pickupUseAnimation != null && pickupUseAnimation.clip != null ? pickupUseAnimation.clip.name : "none")}");
            phase = value;
            elapsed = 0;
            contactSettled = false;
            SetStationaryAttention(
                value != Phase.Approach &&
                value != Phase.Carry &&
                value != Phase.Hold &&
                !(pickup != null && (value == Phase.Reach ||
                    value == Phase.Lift || value == Phase.PickupBlend)));
        }

        private void SetStationaryAttention(bool value)
        {
            if (stationaryAttention == value) return;
            stationaryAttention = value;
            session.SetItemActionAttention(
                value ? binding.View.transform : null, value);
        }

        private Pose CarryPose()
        {
            var rotation = motor.transform.rotation * Quaternion.Euler(binding.Profile.carryEuler);
            var grip = motor.Position + motor.transform.rotation * new Vector3(.21f, 1.28f, .43f);
            return new Pose(grip - rotation * binding.Profile.gripPosition, rotation);
        }

        private void Drive(Pose desired, float dt)
        {
            var body = binding.View.Body;
            var current = hasDrivenGrip ? drivenGrip : rig.HandGrip;
            var desiredGrip = binding.Profile.RequiresTwoHands
                ? binding.Profile.WorldGrip(desired)
                : rig.GripForHeldPosition(desired.position);
            var speed = phase == Phase.Carry || phase == Phase.DockTarget ? 1.6f : .7f;
            var position = phase == Phase.Carry || phase == Phase.DockTarget
                ? desiredGrip.position
                : Vector3.MoveTowards(current.position, desiredGrip.position, speed * dt);
            drivenGrip = new Pose(position, rig.HandGrip.rotation);
            hasDrivenGrip = true;
            rig.Reach(drivenGrip);
            if (binding.Profile.RequiresTwoHands)
                rig.ReachSecondary(
                    binding.Profile.WorldSecondaryGrip(desired));
#region debug-point C-D-E:held-follow
#if UNITY_EDITOR
            if (Time.unscaledTime >= debugNextHeldFollowReport)
            {
                debugNextHeldFollowReport = Time.unscaledTime + .1f;
                var actualGrip = binding.Profile.WorldGrip(body.Pose);
                var handGrip = rig.HandGrip;
                var debugPayload =
                    $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"C-D-E\",\"location\":\"ItemTransferStep.Drive\"," +
                    $"\"msg\":\"[DEBUG] held item follow\",\"data\":{{\"item\":\"{binding.View.Definition.Id}\"," +
                    $"\"phase\":\"{phase}\",\"dt\":{dt},\"speedLimit\":{speed}," +
                    $"\"currentPosition\":{JsonUtility.ToJson(current.position)}," +
                    $"\"desiredPosition\":{JsonUtility.ToJson(desiredGrip.position)}," +
                    $"\"movedPosition\":{JsonUtility.ToJson(body.Pose.position)}," +
                    $"\"handPosition\":{JsonUtility.ToJson(handGrip.position)}," +
                    $"\"positionToDesired\":{Vector3.Distance(handGrip.position, desiredGrip.position)}," +
                    $"\"gripToHand\":{Vector3.Distance(actualGrip.position, handGrip.position)}," +
                    $"\"gripAngleToHand\":{Quaternion.Angle(actualGrip.rotation, handGrip.rotation)}," +
                    $"\"heldAlignmentError\":{rig.HeldAlignmentError}," +
                    $"\"rotationToDesired\":{Quaternion.Angle(body.Pose.rotation, desired.rotation)}}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7786/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
#region debug-point E:held-body-contact
#if UNITY_EDITOR
            if (phase == Phase.Carry && Time.unscaledTime >= debugNextCarryReport)
            {
                debugNextCarryReport = Time.unscaledTime + 1;
                var penetration = 0f;
                foreach (var shape in body.Shapes)
                    foreach (var actorShape in motor.Body.Shapes)
                        if (Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                            actorShape, actorShape.transform.position, actorShape.transform.rotation, out _, out var depth))
                            penetration = Mathf.Max(penetration, depth);
                var payload = FormattableString.Invariant($"{{\"sessionId\":\"item-reach-layer\",\"runId\":\"post-collision-fix\",\"hypothesisId\":\"E\",\"location\":\"ItemTransferStep.Drive\",\"msg\":\"[DEBUG] held collider contact\",\"data\":{{\"penetration\":{penetration},\"actorVelocity\":{JsonUtility.ToJson(motor.Body.Rigidbody.velocity)},\"actorPosition\":{JsonUtility.ToJson(motor.Position)},\"itemPosition\":{JsonUtility.ToJson(body.Pose.position)}}}}}");
                var request = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
        }

        private bool NearGrip(
            Pose item, float tolerance = .012f,
            float angleTolerance = 180f)
        {
            var targetGrip = binding.Profile.RequiresTwoHands
                ? binding.Profile.WorldGrip(item)
                : rig.GripForHeldPosition(item.position);
            if (Vector3.Distance(
                    rig.HandGrip.position,
                    targetGrip.position) >= tolerance)
                return false;
            if (angleTolerance < 179f &&
                Quaternion.Angle(
                    rig.HandGrip.rotation,
                    targetGrip.rotation) >= angleTolerance)
                return false;
            return !binding.Profile.RequiresTwoHands ||
                   Vector3.Distance(
                       rig.LeftHandGrip.position,
                       binding.Profile.WorldSecondaryGrip(item).position) <
                   tolerance;
        }

        private void ReachItem(Pose item, float weight)
        {
            rig.Reach(binding.Profile.WorldGrip(item), weight);
            if (binding.Profile.RequiresTwoHands)
                rig.ReachSecondary(
                    binding.Profile.WorldSecondaryGrip(item), weight);
        }

        private Pose NaturalReleasePose(Pose support)
        {
            var body = binding.View.Body;
            var bottom = body.Pose.position.y;
            foreach (var shape in body.Shapes)
                if (shape != null && shape.enabled)
                    bottom = Mathf.Min(bottom, shape.bounds.min.y);
            var pose = body.Pose;
            pose.position = support.position;
            pose.position.y += body.Pose.position.y - bottom + .005f;
            return pose;
        }

        public bool Tick(float dt)
        {
            // The item was already in her hand when this step began; nothing to do.
            if (alreadyHeld) return true;
            elapsed += dt;
            if (Time.unscaledTime >= nextMotionReport)
            {
                nextMotionReport = Time.unscaledTime + .25f;
                var motionBody = motor.Body;
                // The rise is position-based (velocity stays at zero), which is the
                // signature of depenetration. Name whatever the torso is overlapping.
                var overlapCount = 0;
                var overlapNames = "none";
                var probes = Physics.OverlapSphere(motor.Position + Vector3.up * .9f, .35f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                foreach (var probe in probes)
                {
                    if (probe.transform.IsChildOf(motor.transform)) continue;
                    overlapCount++;
                    if (overlapCount > 3) continue;
                    overlapNames = overlapNames == "none"
                        ? probe.name : overlapNames + "," + probe.name;
                }
                Debug.Log($"MATESOUL_ITEM_MOTION item={binding.View.Definition.Id} " +
                    $"phase={phase} elapsed={elapsed:F2} " +
                    $"root={motor.Position} velY={motionBody.Rigidbody.velocity.y:F2} " +
                    $"following={motionBody.IsFollowing} contact={motionBody.IsContact} " +
                    $"docked={motionBody.IsDocked} paused={motionBody.IsPaused} " +
                    $"kinematic={motionBody.Rigidbody.isKinematic} " +
                    $"overlaps={overlapCount} on={overlapNames} " +
                    motor.MotionDiagnostics);
            }
            if (phase != Phase.Hold && elapsed > 35) throw new InvalidOperationException("搬运物品超时。");
            var view = binding.View;
            switch (phase)
            {
                case Phase.Approach:
                    // Follow live item position before contact, including player movement before reservation.
                    if (Vector3.Distance(view.Body.Pose.position, approachItemPosition) > .15f)
                    {
                        sourceDock = FindApproach();
                        BeginWalk(sourceDock);
                    }
                    if (move.Tick(dt) != ActionProgress.Succeeded) return false;
                    if (pickup == null)
                        sourceFacing = Facing(sourceDock, view.Body.Pose.position);
                    Next(Phase.Dock);
                    break;
                case Phase.Dock:
                    if (!motor.Dock(sourceDock, sourceFacing, dt, DockPositionTolerance)) return false;
                    source = view.Body.Pose;
                    reachMotion = ItemMotion.Reach;
#region debug-point A-B:reach-selection
#if UNITY_EDITOR
                    var reachPayload =
                        $"{{\"sessionId\":\"item-posture-cut\",\"runId\":\"posture-post-fix\"," +
                        $"\"hypothesisId\":\"A-B\",\"location\":\"ItemTransferStep.Tick.Dock\"," +
                        $"\"msg\":\"[DEBUG] reach motion selected\",\"data\":{{" +
                        $"\"item\":\"{view.Definition.Id}\",\"motion\":\"{reachMotion}\"," +
                        $"\"targetHeight\":{source.position.y - session.Navigation.FloorHeight}," +
                        $"\"horizontalDistance\":{Vector3.ProjectOnPlane(source.position - motor.Position, Vector3.up).magnitude}," +
                        $"\"target\":{JsonUtility.ToJson(source.position)}," +
                        $"\"root\":{JsonUtility.ToJson(motor.Position)}}}}}";
                    var reachRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        "http://127.0.0.1:7787/event", reachPayload, "application/json");
                    reachRequest.SendWebRequest().completed += _ => reachRequest.Dispose();
#endif
#endregion
                    Next(Phase.Reach);
                    break;
                case Phase.Reach:
                    if (pickup != null)
                    {
                        rig.PickupMotion(pickup, Mathf.Min(elapsed, pickup.contactTime));
                        ReachItem(view.Body.Pose, Mathf.SmoothStep(0, 1,
                            Mathf.InverseLerp(pickup.contactTime - .25f, pickup.contactTime, elapsed)));
                        if (elapsed < pickup.contactTime) return false;
                    }
                    else
                    {
                        rig.Motion(reachMotion, Mathf.Min(1, elapsed / 1.1f));
                        ReachItem(view.Body.Pose, Mathf.SmoothStep(0, 1, elapsed / 1.1f));
                        if (elapsed < 1.3f) return false;
                    }
                    // Animator and arm IK finish in LateUpdate after the Action tick.
                    if (!contactSettled)
                    {
                        contactSettled = true;
                        return false;
                    }
                    var snapLayer = SelectPickupSnapLayer(rig.ContactError, rig.LeftContactError);
#region debug-point I:pickup-snap-gate
#if UNITY_EDITOR
                    var pickupPayload =
                        $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"post-fix\"," +
                        $"\"hypothesisId\":\"A-B-D\",\"location\":\"ItemTransferStep.Tick.PickupGate\"," +
                        $"\"msg\":\"[DEBUG] pickup snap gate\",\"data\":{{\"item\":\"{view.Definition.Id}\"," +
                        $"\"contactError\":{rig.ContactError},\"hand\":{JsonUtility.ToJson(rig.HandGrip.position)}," +
                        $"\"grip\":{JsonUtility.ToJson(binding.Profile.WorldGrip(view.Body.Pose).position)}," +
                        $"\"itemPosition\":{JsonUtility.ToJson(view.Body.Pose.position)}}}}}";
                    var pickupRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        "http://127.0.0.1:7786/event", pickupPayload, "application/json");
                    pickupRequest.SendWebRequest().completed += _ => pickupRequest.Dispose();
#endif
#endregion
#region debug-point B:pickup-ranges
#if UNITY_EDITOR
                    var debugPickupError = Mathf.Max(
                        rig.ContactError, rig.LeftContactError);
                    var debugRangePayload =
                        $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                        $"\"hypothesisId\":\"B\",\"location\":\"ItemTransferStep.Tick.PickupGate\"," +
                        $"\"msg\":\"[DEBUG] pickup range classification\",\"data\":{{" +
                        $"\"item\":\"{view.Definition.Id}\",\"error\":{debugPickupError}," +
                        $"\"contactRange\":{PickupAnimationSet.ContactTolerance}," +
                        $"\"primaryRange\":{MaximumPickupSnapDistance}," +
                        $"\"fallbackRange\":{FallbackPickupSnapDistance}," +
                        $"\"classification\":\"{snapLayer.ToString().ToLowerInvariant()}\"}}}}";
                    var debugRangeRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        "http://127.0.0.1:7777/event", debugRangePayload, "application/json");
                    debugRangeRequest.SendWebRequest().completed += _ => debugRangeRequest.Dispose();
#endif
#endregion
                    if (snapLayer == PickupSnapLayer.None)
                    {
                        throw new InvalidOperationException(
                            "物品仍超出冷月手部可吸附范围，请将物品放近一些。");
                    }
                    // Recheck contact and normal snap after another solved pose before using the last resort.
                    if (snapLayer == PickupSnapLayer.Fallback && !fallbackPending)
                    {
                        fallbackPending = true;
                        return false;
                    }
                    if (!view.Body.IsInteractionControlled) view.Stabilize(binding.Lease);
                    var snapPose = binding.Profile.RequiresTwoHands
                        ? binding.Profile.ItemFromHands(
                            rig.HandGrip, rig.LeftHandGrip)
                        : binding.Profile.ItemFromGrip(rig.HandGrip);
                    if (pickup != null && binding.Profile.gripKind == ItemGripKind.ThinEdge)
                    {
                        // A plate's rim is already on its support. Tilting the
                        // entire plate at contact can put its far edge below it.
                        snapPose = view.Body.Pose;
                        snapPose.position += rig.HandGrip.position -
                            binding.Profile.WorldGrip(snapPose).position;
                    }
#region debug-point C-D-E:pickup-snap
#if UNITY_EDITOR
                    var debugBeforeSnap = view.Body.Pose;
                    var debugHandGrip = rig.HandGrip;
#endif
#endregion
                    if (!view.Body.TryMoveInteraction(run, snapPose))
                        throw new InvalidOperationException("物品吸附控制权已失效。");
                    rig.AttachHeld(run, view.Body, binding.Profile);
                    drivenGrip = rig.HandGrip;
                    hasDrivenGrip = true;
#region debug-point C-D-E:pickup-snap
#if UNITY_EDITOR
                    var debugAfterSnapGrip = binding.Profile.WorldGrip(view.Body.Pose);
                    var debugSnapPayload =
                        $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"post-fix\"," +
                        $"\"hypothesisId\":\"C-D-E\",\"location\":\"ItemTransferStep.Tick.Reach\"," +
                        $"\"msg\":\"[DEBUG] pickup snap\",\"data\":{{\"item\":\"{view.Definition.Id}\"," +
                        $"\"beforePosition\":{JsonUtility.ToJson(debugBeforeSnap.position)}," +
                        $"\"afterPosition\":{JsonUtility.ToJson(view.Body.Pose.position)}," +
                        $"\"handPosition\":{JsonUtility.ToJson(debugHandGrip.position)}," +
                        $"\"snapDistance\":{Vector3.Distance(debugBeforeSnap.position, view.Body.Pose.position)}," +
                        $"\"gripToHand\":{Vector3.Distance(debugAfterSnapGrip.position, debugHandGrip.position)}," +
                        $"\"gripAngleToHand\":{Quaternion.Angle(debugAfterSnapGrip.rotation, debugHandGrip.rotation)}}}}}";
                    var debugSnapRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        "http://127.0.0.1:7786/event", debugSnapPayload, "application/json");
                    debugSnapRequest.SendWebRequest().completed += _ => debugSnapRequest.Dispose();
#endif
#endregion
                    if (!binding.Lease.Transition(binding.Lease.State, ItemControlState.HeldByCharacter))
                        throw new InvalidOperationException("物品抓取权已失效。");
                    source = view.Body.Pose;
                    Next(Phase.Lift);
                    break;
                case Phase.Lift:
                    if (pickup != null)
                    {
                        rig.PickupMotion(pickup, pickup.contactTime + elapsed);
                        // The held item follows the authored hand in LateUpdate,
                        // retaining and fading the contact offset during stand-up.
                        rig.Reach(new Pose(rig.HandGrip.position, rig.HandGrip.rotation), 0);
                        if (elapsed < pickup.Duration - pickup.contactTime) return false;
                        drivenGrip = rig.HandGrip;
                        Next(Phase.PickupBlend);
                        break;
                    }
                    rig.Motion(reachMotion, 1 - Mathf.Clamp01((elapsed - .35f) / 1.1f));
                    var lifted = source;
                    lifted.position += Vector3.up * .15f;
                    var liftTarget = elapsed < .4f ? lifted : CarryPose();
                    Drive(liftTarget, dt);
                    if (elapsed < 1.6f ||
                        !NearGrip(CarryPose(), .08f)) return false;
                    rig.Motion(ItemMotion.Carry, 1);
                    BeginWalk(destinationDock);
                    Next(Phase.Carry);
                    break;
                case Phase.PickupBlend:
                    if (pickupOnly && !finishAfterPickup)
                    {
                        if (pickupUseAnimation != null)
                        {
                            // Settle into the carry pose first so the gesture
                            // starts without a visible snap.
                            rig.BlendPickupToCarry(elapsed / .3f);
                            Drive(CarryPose(), dt);
                            if (elapsed < .3f || !NearGrip(CarryPose(), .08f)) return false;
                            rig.ClearReach();
                            rig.UseMotion(pickupUseAnimation, 0, 0);
                            Next(Phase.PickupUse);
                            break;
                        }
                        // The item stays in the hand without the authored carry
                        // pose, so she stands and walks with her arms hanging.
                        rig.HoldNaturally(elapsed / .3f);
                        if (elapsed < .3f) return false;
                        if (retainWhenDone)
                        {
                            rig.RetainOnEnd(run);
                            return true;
                        }
                        Next(Phase.Hold);
                        break;
                    }
                    rig.BlendPickupToCarry(elapsed / .3f);
                    Drive(CarryPose(), dt);
                    if (elapsed < .3f || !NearGrip(CarryPose(), .08f)) return false;
                    if (pickupOnly)
                    {
                        if (retainWhenDone)
                        {
                            // Keep the carry state but let the action finish, so the
                            // control lease is released for the next command.
                            rig.RetainOnEnd(run);
                            return true;
                        }
                        if (finishAfterPickup) return true;
                        Next(Phase.Hold);
                        break;
                    }
                    BeginWalk(destinationDock);
                    Next(Phase.Carry);
                    break;
                case Phase.PickupUse:
                {
                    var useClip = pickupUseAnimation.clip;
                    const float fade = .25f;
                    var weight = Mathf.SmoothStep(0, 1, elapsed / fade) *
                        (1 - Mathf.SmoothStep(0, 1, (elapsed - useClip.length) / fade));
                    rig.UseMotion(pickupUseAnimation, Mathf.Min(elapsed, useClip.length), weight);
                    if (elapsed < useClip.length + fade) return false;
                    Next(Phase.PickupSettle);
                    break;
                }
                case Phase.PickupSettle:
                {
                    // Hand the arms back to locomotion while the gesture fades.
                    var settle = Mathf.Clamp01(elapsed / .25f);
                    rig.UseMotion(pickupUseAnimation, pickupUseAnimation.clip.length,
                        1 - settle);
                    rig.HoldNaturally(settle);
                    if (settle < 1) return false;
                    rig.ClearUse();
                    if (retainWhenDone)
                    {
                        rig.RetainOnEnd(run);
                        return true;
                    }
                    Next(Phase.Hold);
                    break;
                }
                case Phase.Hold:
                    // Only idle03 and walking play while the item stays held.
                    rig.HoldNaturally(1);
                    return false;
                case Phase.Carry:
                    rig.Motion(ItemMotion.Carry, 1);
                    Drive(CarryPose(), dt);
                    if (move.Tick(dt) != ActionProgress.Succeeded) return false;
                    targetFacing = fromHeld ? sourceFacing : Facing(destinationDock, destination().position);
                    Next(Phase.DockTarget);
                    break;
                case Phase.DockTarget:
                    Drive(CarryPose(), dt);
                    if (!motor.Dock(fromHeld ? sourceDock : destinationDock, targetFacing, dt,
                        fromHeld && pickup != null
                            ? PickupAnimationSet.ContactTolerance : DockPositionTolerance)) return false;
                    Next(Phase.Place);
                    break;
                case Phase.Place:
                    if (fromHeld && pickup != null)
                    {
                        var lowerSeconds = pickup.Duration - pickup.contactTime;
                        rig.PickupPlaceMotion(pickup,
                            Mathf.Max(pickup.contactTime, pickup.Duration - elapsed),
                            rig.GripForHeldPosition(destination().position),
                            Mathf.SmoothStep(0, 1, (elapsed - lowerSeconds + .25f) / .25f),
                            elapsed / .25f);
                        // The authored descent is relative to where she happens to
                        // stand, so its contact frame can miss the original spot by
                        // more than the contact tolerance. Close the residual with
                        // the reach solve instead of waiting forever for a
                        // placement that can never land.
                        if (elapsed >= lowerSeconds)
                        {
                            // Seed the drive from the solved hand so the arm does
                            // not jump back to the last carry pose.
                            drivenGrip = rig.HandGrip;
                            hasDrivenGrip = true;
                            Drive(destination(), dt);
                        }
                        if (elapsed < lowerSeconds) return false;
                        // Release aligns the item to the exact pose itself, so a
                        // residual gap must never block the transfer forever.
                        if (Vector3.Distance(view.Body.Pose.position,
                                destination().position) > PlacementContactTolerance &&
                            elapsed < lowerSeconds + 4f)
                            return false;
                        if (!contactSettled) { contactSettled = true; return false; }
                        Next(Phase.Release);
                        break;
                    }
                    rig.Motion(ItemMotion.Reach, Mathf.Min(1, elapsed));
                    var authoredTarget = destination();
                    var target = naturalRelease
                        ? NaturalReleasePose(authoredTarget)
                        : authoredTarget;
                    var raised = target;
                    raised.position += Vector3.up * .18f;
                    // Move over the destination before descending onto its support.
                    Drive(elapsed < 1 || !NearGrip(raised, .03f) && elapsed < 2
                        ? raised : target, dt);
#region debug-point B-C:placement-pose-writers
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (Time.unscaledTime >= debugNextPlacementJitterReport)
                    {
                        debugNextPlacementJitterReport = Time.unscaledTime + .05f;
                        var debugBody = view.Body;
                        var debugGrip = binding.Profile.WorldGrip(debugBody.Pose);
                        var debugPlacePayload =
                            $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                            $"\"hypothesisId\":\"B-C\",\"location\":\"ItemTransferStep.Tick.Place\"," +
                            $"\"msg\":\"[DEBUG] placement pose sample\",\"data\":{{" +
                            $"\"item\":\"{view.Definition.Id}\",\"elapsed\":{elapsed}," +
                            $"\"bodyPosition\":{JsonUtility.ToJson(debugBody.Pose.position)}," +
                            $"\"bodyRotation\":{JsonUtility.ToJson(debugBody.Pose.rotation)}," +
                            $"\"targetPosition\":{JsonUtility.ToJson(authoredTarget.position)}," +
                            $"\"releasePosition\":{JsonUtility.ToJson(target.position)}," +
                            $"\"targetRotation\":{JsonUtility.ToJson(authoredTarget.rotation)}," +
                            $"\"handPosition\":{JsonUtility.ToJson(rig.HandGrip.position)}," +
                            $"\"gripPosition\":{JsonUtility.ToJson(debugGrip.position)}," +
                            $"\"positionError\":{Vector3.Distance(debugBody.Pose.position, target.position)}," +
                            $"\"rotationError\":{Quaternion.Angle(debugBody.Pose.rotation, target.rotation)}," +
                            $"\"controlled\":{debugBody.IsInteractionControlled.ToString().ToLowerInvariant()}," +
                            $"\"collisions\":{debugBody.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}}}}}";
                        var debugPlaceRequest = UnityEngine.Networking.UnityWebRequest.Post(
                            Application.isEditor
                                ? "http://127.0.0.1:7781/event"
                                : "http://127.0.0.1:7781/event",
                            debugPlacePayload, "application/json");
                        debugPlaceRequest.SendWebRequest().completed += _ => debugPlaceRequest.Dispose();
                    }
#endif
#endregion
                    var settled = NearGrip(target, .04f) &&
                        Vector3.Distance(view.Body.Pose.position, target.position) <= .005f;
                    if (elapsed < 2f || (!settled && elapsed < 6f))
                    {
                        contactSettled = false;
                        return false;
                    }
                    // Reach is solved in LateUpdate, after this action Tick.
                    if (!contactSettled) { contactSettled = true; return false; }
#region debug-point H:place-gate
#if UNITY_EDITOR
                    var gatePayload = FormattableString.Invariant($"{{\"sessionId\":\"item-reach-layer\",\"runId\":\"pre-place-gate-fix\",\"hypothesisId\":\"H\",\"location\":\"ItemTransferStep.Tick\",\"msg\":\"[DEBUG] placement gate\",\"data\":{{\"label\":\"{Label}\",\"item\":\"{view.Definition.Id}\",\"contactError\":{rig.ContactError},\"hand\":{JsonUtility.ToJson(rig.HandGrip.position)},\"grip\":{JsonUtility.ToJson(binding.Profile.WorldGrip(view.Body.Pose).position)},\"itemPosition\":{JsonUtility.ToJson(view.Body.Pose.position)},\"targetPosition\":{JsonUtility.ToJson(target.position)},\"root\":{JsonUtility.ToJson(motor.Position)}}}}}");
                    var gateRequest = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", gatePayload, "application/json");
                    gateRequest.SendWebRequest().completed += _ => gateRequest.Dispose();
#endif
#endregion
                    // A low surface can sit outside standing arm reach. The release
                    // phase aligns the item itself, so missing hand contact must not
                    // block the transfer until the step timeout.
                    if (!settled)
                        Debug.Log($"MATESOUL_ITEM_PLACE fallback item={view.Definition.Id} " +
                            $"elapsed={elapsed:F2} contactError={rig.ContactError:F3} " +
                            $"hand={rig.HandGrip.position} target={target.position}");
                    Next(Phase.Release);
                    break;
                case Phase.Release:
#region debug-point B-C:release-order
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    var debugBeforeRelease = view.Body.Pose;
#endif
#endregion
                    if (!releasedForAlignment)
                    {
                        rig.DetachHeld(run);
                        rig.ClearReach();
                        hasDrivenGrip = false;
                        releasedForAlignment = true;
                        binding.Lease.Transition(
                            ItemControlState.HeldByCharacter,
                            ItemControlState.StagedForAction);
                    }
                    if (fromHeld && pickup != null)
                        rig.PickupMotion(pickup, pickup.contactTime);
                    else
                        rig.Motion(ItemMotion.Reach,
                            1 - Mathf.Clamp01(elapsed / .5f),
                            1 - Mathf.Clamp01(elapsed / .5f));
                    if (naturalRelease)
                    {
                        view.ReleaseInteractionToPhysics(binding.Lease);
                    }
                    else
                    {
                        var releaseTarget = destination();
                        var releasePose = view.Body.Pose;
                        releasePose.position = Vector3.MoveTowards(
                            releasePose.position,
                            releaseTarget.position, .7f * dt);
                        releasePose.rotation = Quaternion.RotateTowards(
                            releasePose.rotation,
                            releaseTarget.rotation, 180f * dt);
                        if (!view.Body.TryMoveInteraction(run, releasePose))
                            throw new InvalidOperationException(
                                "物品释放对齐控制权已失效。");
                        if (Vector3.Distance(
                                releasePose.position,
                                releaseTarget.position) > .002f ||
                            Quaternion.Angle(
                                releasePose.rotation,
                                releaseTarget.rotation) > 2f)
                            return false;
                        if (!view.Body.SetInteractionCollisions(run, true))
                            throw new InvalidOperationException(
                                "物品释放前无法恢复碰撞状态。");
                        view.CheckpointInteraction();
                    }
#region debug-point B-C:release-order
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    var debugAfterRelease = view.Body.Pose;
                    var debugReleasePayload =
                        $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                        $"\"hypothesisId\":\"B-C\",\"location\":\"ItemTransferStep.Tick.Release\"," +
                        $"\"msg\":\"[DEBUG] release pose order\",\"data\":{{" +
                        $"\"item\":\"{view.Definition.Id}\"," +
                        $"\"beforePosition\":{JsonUtility.ToJson(debugBeforeRelease.position)}," +
                        $"\"afterPosition\":{JsonUtility.ToJson(debugAfterRelease.position)}," +
                        $"\"snapDistance\":{Vector3.Distance(debugBeforeRelease.position, debugAfterRelease.position)}," +
                        $"\"rotationDelta\":{Quaternion.Angle(debugBeforeRelease.rotation, debugAfterRelease.rotation)}," +
                        $"\"velocity\":{JsonUtility.ToJson(view.Body.Rigidbody.velocity)}," +
                        $"\"angularVelocity\":{JsonUtility.ToJson(view.Body.Rigidbody.angularVelocity)}," +
                        $"\"controlled\":{view.Body.IsInteractionControlled.ToString().ToLowerInvariant()}," +
                        $"\"collisions\":{view.Body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                        $"\"support\":{view.Body.HasSupport().ToString().ToLowerInvariant()}}}}}";
                    var debugReleaseRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        Application.isEditor
                            ? "http://127.0.0.1:7781/event"
                            : "http://127.0.0.1:7781/event",
                        debugReleasePayload, "application/json");
                    debugReleaseRequest.SendWebRequest().completed += _ => debugReleaseRequest.Dispose();
#endif
#endregion
                    Next(Phase.Retract);
                    break;
                case Phase.Retract:
                    if (fromHeld && pickup != null)
                    {
                        rig.PickupMotion(pickup, pickup.contactTime + elapsed);
                        return elapsed >= pickup.Duration - pickup.contactTime;
                    }
                    rig.Motion(ItemMotion.Reach, 1 - Mathf.Clamp01(elapsed / .5f),
                        1 - Mathf.Clamp01(elapsed / .5f));
                    return elapsed >= .5f;
            }
            return false;
        }

        private static Quaternion Facing(Vector3 from, Vector3 to)
            => Quaternion.LookRotation(Vector3.ProjectOnPlane(to - from, Vector3.up).normalized);

        public void Dispose()
        {
            move?.Dispose();
            SetStationaryAttention(false);
            if (active) { motor.Stop(); rig.ClearReach(); rig.ClearPickup(); active = false; }
        }
    }
}
