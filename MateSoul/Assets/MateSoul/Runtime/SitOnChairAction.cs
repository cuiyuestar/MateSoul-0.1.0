using System;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class SitOnChairAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly RealChairScanner scanner;
        private readonly ItemRegistry items;
        private readonly string targetId;
        private readonly AvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly AvatarSeatingRig rig;
        private SeatTarget seat;
        private ItemReservation reservation;
        private MoveAction movement;
        private Vector3 dock;
        private float phaseTime, blend;
        private bool started, exiting;
        private string exitError;
        private int phase;
        public string Phase => new[] { "walking", "docking", "sitting", "seated", "standing" }[phase];
        public string PhaseId => Phase;
        public string PostureId => phase >= 2 && phase < 4 ? "seated" : "standing";

        public SitOnChairAction(RealChairScanner scanner, ItemRegistry items, string id,
            AvatarMotor motor, INavigationService navigation, AvatarSeatingRig rig)
        {
            this.scanner = scanner; this.items = items; targetId = id;
            this.motor = motor; this.navigation = navigation; this.rig = rig;
        }

        public string Check()
        {
            seat = scanner.Resolve(targetId)?.Copy();
            if (seat == null || !items.TryGet(targetId, out var item) || item.IsOccupied)
                return "椅子尚未稳定识别或正在被使用。";
            if (seat.Backless &&
                !SeatGeometry.TryOrientBackless(seat, scanner.Geometry, motor.transform.position))
                return "凳子周围没有可用的落座方向。";
            if (!scanner.IsSafe(seat)) return "椅子前方空间不足。";
            var fit = rig.Check(seat);
            if (fit != null) return fit;
            if (!navigation.TrySample(seat.Dock, .15f, out dock) ||
                Vector3.ProjectOnPlane(dock - seat.Dock, Vector3.up).magnitude > .10f ||
                Mathf.Abs(dock.y - seat.FloorY) > .08f)
                return "椅子正前方没有可用的站立位置。";
            movement = new MoveAction(motor, navigation, () => dock);
            return movement.Check();
        }

        public void Start()
        {
            reservation = items.Reserve(targetId, RealityInteractionIds.Sit, this);
            if (reservation == null) throw new InvalidOperationException("椅子已被占用。");
            started = true;
            movement.Start();
            Debug.Log($"MATESOUL_SEAT phase=walking id={targetId} center={seat.Center:F3} " +
                      $"width={seat.Width:F3} depth={seat.Depth:F3} planeError={seat.PlaneError:F4}");
        }

        public ActionProgress Tick(float dt)
        {
            phaseTime += dt;
            if (!exiting && (!reservation.IsValid || !scanner.IsSafe(seat)))
            {
                Debug.LogWarning("MATESOUL_SEAT target_unavailable; exit to approach point");
                Cancel(ActionStopReason.TargetLost);
            }
            if (exiting && phase < 2)
            {
                if (exitError != null) throw new InvalidOperationException(exitError);
                return ActionProgress.Succeeded;
            }
            if (phase == 0 && movement.Tick(dt) == ActionProgress.Succeeded) Advance(1);
            if (phase == 1)
            {
                if (phaseTime > 5f) throw new InvalidOperationException("无法精确到达椅子前方。");
                if (motor.Dock(dock, seat.Rotation, dt))
                {
                    motor.BeginBodyContact(dock, seat.Center.y, seat.SourceId);
                    Advance(2);
                }
            }
            if (phase >= 2)
            {
                if (exiting) { if (phase != 4) Advance(4); blend = Mathf.Max(0, blend - dt / 1.6f); }
                else blend = Mathf.Min(1, blend + dt / 1.8f);
                var eased = Mathf.SmoothStep(0, 1, blend);
                var root = new Vector3(seat.Center.x, dock.y, seat.Center.z) - seat.Forward * .079f;
                motor.SetContactRoot(Vector3.Lerp(dock, root, eased), seat.Rotation);
                rig.SetPose(seat, eased);
                if (!exiting && blend >= 1 && phase != 3) Advance(3);
                if (exiting && blend <= 0)
                {
                    if (exitError != null) throw new InvalidOperationException(exitError);
                    return ActionProgress.Succeeded;
                }
            }
            return ActionProgress.Running;
        }

        private void Advance(int next)
        {
            phase = next; phaseTime = 0;
            Debug.Log($"MATESOUL_SEAT phase={Phase}");
        }

        public void Cancel(ActionStopReason reason)
        {
            exiting = true;
            if (reason == ActionStopReason.TargetLost) exitError = "椅子几何或可用空间已变化，已退出落座。";
        }
        public void Dispose()
        {
            if (!started) return;
            try
            {
                movement?.Dispose();
                motor.EndBodyContact();
                rig.Release();
            }
            finally { reservation?.Dispose(); }
        }
    }
}
