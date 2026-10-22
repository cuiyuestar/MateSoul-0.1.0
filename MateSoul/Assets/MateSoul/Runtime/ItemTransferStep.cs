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
        public const float FallbackPickupSnapDistance = .45f;

        public static bool CanSnapPickup(float primaryError, float secondaryError = 0)
            => Mathf.Max(primaryError, secondaryError) <= FallbackPickupSnapDistance;

        private enum Phase { Approach, Dock, Reach, Lift, Carry, DockTarget, Place, Release, Retract }
        private readonly GameSession session;
        private readonly AvatarMotor motor;
        private readonly AvatarItemRig rig;
        private readonly ItemActionBinding binding;
        private readonly Func<Pose> destination;
        private readonly Vector3 destinationDock;
        private readonly bool naturalRelease;
        private readonly Guid run;
        private Phase phase;
        private MoveAction move;
        private Pose source, drivenGrip;
        private Vector3 sourceDock;
        private Quaternion sourceFacing, targetFacing;
        private ItemMotion reachMotion;
        private float elapsed;
        private float debugNextCarryReport;
        private bool active, contactSettled, hasDrivenGrip;
        private bool releasedForAlignment;
        private bool stationaryAttention;
        public string Label { get; }

        public ItemTransferStep(string label, GameSession session, AvatarItemRig rig,
            ItemActionBinding binding, Guid run, Func<Pose> destination,
            Vector3 destinationDock, bool naturalRelease = false)
        {
            Label = label; this.session = session; motor = (AvatarMotor)session.Motor;
            this.rig = rig; this.binding = binding; this.run = run;
            this.destination = destination; this.destinationDock = destinationDock;
            this.naturalRelease = naturalRelease;
        }

        public void Start()
        {
            if (binding.View.IsFixedAfterPlacement)
                throw new InvalidOperationException(
                    $"{binding.View.Definition.DisplayName}放置后不可搬动。");
            if (binding.View.Body.Pose.position.y - session.Navigation.FloorHeight > 1.15f)
                throw new InvalidOperationException("物品过高，冷月无法取到。");
            sourceDock = FindApproach();
            BeginWalk(sourceDock);
            phase = Phase.Approach;
            active = true;
        }

        private Vector3 FindApproach()
        {
            var position = binding.View.Body.Pose.position;
            var initial = Vector3.ProjectOnPlane(motor.Position - position, Vector3.up).normalized;
            if (initial.sqrMagnitude < .01f) initial = Vector3.back;
            for (var ring = 0; ring < 3; ring++)
                for (var i = 0; i < 16; i++)
                {
                    var direction = Quaternion.Euler(0, i * 22.5f, 0) * initial;
                    var candidate = position + direction * (.44f + ring * .1f);
                    candidate.y = session.Navigation.FloorHeight;
                    if (!session.Navigation.TrySample(candidate, .08f, out var floor) ||
                        Vector3.Distance(floor, candidate) > .08f ||
                        !session.Navigation.TryPath(motor.Position, floor, out _)) continue;
                    var pose = new Pose(floor + Vector3.up * .025f,
                        Quaternion.LookRotation(-direction));
                    if (motor.Body.CanOccupy(pose)) return floor;
                }
            throw new InvalidOperationException($"无法走到{binding.View.Definition.DisplayName}旁边。");
        }

        private void BeginWalk(Vector3 point)
        {
            SetStationaryAttention(false);
            move?.Dispose();
            move = new MoveAction(motor, session.Navigation, () => point);
            var error = move.Check();
            if (error != null) throw new InvalidOperationException("物品搬运路线不可达。");
            move.Start();
        }

        private void Next(Phase value)
        {
            phase = value;
            elapsed = 0;
            contactSettled = false;
            SetStationaryAttention(
                value != Phase.Approach &&
                value != Phase.Carry);
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
            elapsed += dt;
            if (elapsed > 35) throw new InvalidOperationException("搬运物品超时。");
            var view = binding.View;
            switch (phase)
            {
                case Phase.Approach:
                    // Follow live item position before contact, including player movement before reservation.
                    if (Vector3.Distance(view.Body.Pose.position,
                            sourceDock + Vector3.up * (view.Body.Pose.position.y - sourceDock.y)) > .85f)
                    {
                        sourceDock = FindApproach();
                        BeginWalk(sourceDock);
                    }
                    if (move.Tick(dt) != ActionProgress.Succeeded) return false;
                    sourceFacing = Facing(sourceDock, view.Body.Pose.position);
                    Next(Phase.Dock);
                    break;
                case Phase.Dock:
                    if (!motor.Dock(sourceDock, sourceFacing, dt)) return false;
                    source = view.Body.Pose;
                    reachMotion = ItemMotion.Reach;
                    Next(Phase.Reach);
                    break;
                case Phase.Reach:
                    rig.Motion(reachMotion, Mathf.Min(1, elapsed / 1.1f));
                    ReachItem(view.Body.Pose,
                        Mathf.SmoothStep(0, 1, elapsed / 1.1f));
                    if (elapsed < 1.3f) return false;
                    // Animator and arm IK finish in LateUpdate after the Action tick.
                    if (!contactSettled)
                    {
                        contactSettled = true;
                        return false;
                    }
                    if (!CanSnapPickup(rig.ContactError, rig.LeftContactError))
                    {
                        throw new InvalidOperationException(
                            "物品仍超出冷月手部可吸附范围，请将物品放近一些。");
                    }
                    if (!view.Body.IsInteractionControlled) view.Stabilize(binding.Lease);
                    var snapPose = binding.Profile.RequiresTwoHands
                        ? binding.Profile.ItemFromHands(
                            rig.HandGrip, rig.LeftHandGrip)
                        : binding.Profile.ItemFromGrip(rig.HandGrip);
                    if (!view.Body.TryMoveInteraction(run, snapPose))
                        throw new InvalidOperationException("物品吸附控制权已失效。");
                    rig.AttachHeld(run, view.Body, binding.Profile);
                    drivenGrip = rig.HandGrip;
                    hasDrivenGrip = true;
                    if (!binding.Lease.Transition(binding.Lease.State, ItemControlState.HeldByCharacter))
                        throw new InvalidOperationException("物品抓取权已失效。");
                    source = view.Body.Pose;
                    Next(Phase.Lift);
                    break;
                case Phase.Lift:
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
                case Phase.Carry:
                    rig.Motion(ItemMotion.Carry, 1);
                    Drive(CarryPose(), dt);
                    if (move.Tick(dt) != ActionProgress.Succeeded) return false;
                    targetFacing = Facing(destinationDock, destination().position);
                    Next(Phase.DockTarget);
                    break;
                case Phase.DockTarget:
                    Drive(CarryPose(), dt);
                    if (!motor.Dock(destinationDock, targetFacing, dt)) return false;
                    Next(Phase.Place);
                    break;
                case Phase.Place:
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
                    if (elapsed < 2 || !NearGrip(target, .04f))
                    {
                        contactSettled = false;
                        return false;
                    }
                    // Reach is solved in LateUpdate, after this action Tick.
                    if (!contactSettled) { contactSettled = true; return false; }
                    if (rig.ContactError > .04f)
                        throw new InvalidOperationException("桌面工作位置超出手部范围。");
                    if (Vector3.Distance(
                            view.Body.Pose.position,
                            target.position) > .005f)
                    {
                        contactSettled = false;
                        return false;
                    }
                    Next(Phase.Release);
                    break;
                case Phase.Release:
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
                    Next(Phase.Retract);
                    break;
                case Phase.Retract:
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
            if (active) { motor.Stop(); rig.ClearReach(); active = false; }
        }
    }
}
