using System;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class LieOnBedAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly RealBedScanner scanner;
        private readonly ItemRegistry items;
        private readonly string targetId;
        private readonly AvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly AvatarLyingRig rig;
        private BedTarget bed;
        private BedMountPlan mount;
        private ItemReservation reservation;
        private MoveAction movement;
        private float phaseTime;
        private bool started, exiting, contact;
        private string exitError;
        private int phase;

        public string Phase => new[]
        {
            "walking", "docking", "mounting", "lying", "resting", "rising"
        }[phase];
        public string PhaseId => Phase;
        public string PostureId => phase >= 3 && phase < 5 ? "lying" : "standing";
        public BedMountEdge? MountEdge => mount?.Edge;
        public Vector3 ApproachPosition => mount?.Dock ?? Vector3.zero;

        public LieOnBedAction(RealBedScanner scanner, ItemRegistry items, string id,
            AvatarMotor motor, INavigationService navigation, AvatarLyingRig rig)
        {
            this.scanner = scanner;
            this.items = items;
            targetId = id;
            this.motor = motor;
            this.navigation = navigation;
            this.rig = rig;
        }

        public string Check()
        {
            bed = scanner.Resolve(targetId)?.Copy();
            if (bed == null || !items.TryGet(targetId, out var item) || item.IsOccupied)
                return "床铺尚未稳定识别或正在被使用。";
            if (!scanner.IsSafe(bed)) return "床面或躺卧路径空间不足。";
            var fit = rig.Check(bed);
            if (fit != null) return fit;
            if (!BedMountPlanner.TryCreate(bed, motor.Position, navigation,
                    scanner.Geometry, out mount))
                return "床边没有可达且净空充足的上床位置。";
            movement = new MoveAction(motor, navigation, () => mount.Dock);
            return movement.Check();
        }

        public void Start()
        {
            reservation = items.Reserve(targetId, RealityInteractionIds.Lie, this);
            if (reservation == null) throw new InvalidOperationException("床铺已被占用。");
            started = true;
            movement.Start();
            Debug.Log($"MATESOUL_BED phase=walking id={targetId} center={bed.Center:F3} " +
                      $"width={bed.Width:F3} length={bed.Length:F3} height=" +
                      $"{bed.Center.y - bed.FloorY:F3} edge={mount.Edge} " +
                      $"dock={mount.Dock:F3} planeError={bed.PlaneError:F4}");
        }

        public ActionProgress Tick(float dt)
        {
            phaseTime += dt;
            if (!exiting && (!reservation.IsValid || !scanner.IsSafe(bed) ||
                             phase <= 2 && !scanner.IsMountSafe(bed, mount)))
            {
                Debug.LogWarning("MATESOUL_BED target_unavailable; exit to approach point");
                Cancel(ActionStopReason.TargetLost);
            }
            if (exiting && phase < 2)
            {
                if (exitError != null) throw new InvalidOperationException(exitError);
                return ActionProgress.Succeeded;
            }
            if (exiting)
            {
                if (phase != 5)
                {
                    Advance(5);
                    return ActionProgress.Running;
                }
                if (exitError != null) throw new InvalidOperationException(exitError);
                return ActionProgress.Succeeded;
            }

            if (phase == 0)
            {
                if (movement.Tick(dt) == ActionProgress.Succeeded) Advance(1);
                return ActionProgress.Running;
            }
            if (phase == 1)
            {
                if (phaseTime > 5f) throw new InvalidOperationException("无法精确到达床边。");
                if (motor.Dock(mount.Dock, mount.Rotation, dt)) Advance(2);
                return ActionProgress.Running;
            }
            if (phase == 2)
            {
                motor.BeginLyingContact(mount.Dock, new Pose(bed.ContactRoot, bed.LyingRotation));
                contact = true;
                rig.SetPose(bed, 1);
                Advance(3);
                return ActionProgress.Running;
            }
            if (phase == 3)
            {
                Advance(4);
                return ActionProgress.Running;
            }

            return ActionProgress.Running;
        }

        private void Advance(int next)
        {
            phase = next;
            phaseTime = 0;
            Debug.Log($"MATESOUL_BED phase={Phase}");
        }

        public void Cancel(ActionStopReason reason)
        {
            exiting = true;
            if (reason == ActionStopReason.TargetLost)
                exitError = "床面几何或可用空间已变化，已退出躺卧。";
        }

        public void Dispose()
        {
            if (!started) return;
            try
            {
                movement?.Dispose();
                if (contact) motor.EndBodyContact();
                rig.Release();
            }
            finally
            {
                reservation?.Dispose();
            }
        }
    }
}
