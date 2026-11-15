using System;
using System.Collections.Generic;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Wardrobe;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class LieOnBedAction : IAvatarAction, IActionRuntimeStatus
    {
        // How long a completed lie survives without a fresh scan agreeing.
        private const float LyingTargetGraceSeconds = 2.5f;
        // 走向床边的过程中，扫描抖动允许持续这么久再判定目标丢失。
        private const float PreMountGraceSeconds = 1f;
        // 换好睡衣后停留一下，让玩家看清换装结果，再进入躺卧转移。
        private const float DressingSeconds = .8f;
        private const string PajamasOutfit = "pink-pajamas";
        private readonly RealBedScanner scanner;
        private readonly ItemRegistry items;
        private readonly string targetId;
        private readonly AvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly AvatarLyingRig rig;
        private readonly LengyueWardrobeController wardrobe;
        private BedTarget bed;
        private BedMountPlan mount;
        private ItemReservation reservation;
        private MoveAction movement;
        private float phaseTime;
        private float lostTime;
        private bool started, exiting, contact, dressed;
        private string exitError;
        private int phase;

        public string Phase => new[]
        {
            "walking", "docking", "dressing", "mounting", "lying", "resting", "rising"
        }[phase];
        public string PhaseId => Phase;
        public string PostureId => phase >= 4 && phase < 6 ? "lying" : "standing";
        public BedMountEdge? MountEdge => mount?.Edge;
        public Vector3 ApproachPosition => mount?.Dock ?? Vector3.zero;

        public LieOnBedAction(RealBedScanner scanner, ItemRegistry items, string id,
            AvatarMotor motor, INavigationService navigation, AvatarLyingRig rig,
            LengyueWardrobeController wardrobe = null)
        {
            this.scanner = scanner;
            this.items = items;
            targetId = id;
            this.motor = motor;
            this.navigation = navigation;
            this.rig = rig;
            this.wardrobe = wardrobe;
        }

        public string Check()
        {
            bed = scanner.Resolve(targetId)?.Copy();
            if (bed == null || !items.TryGet(targetId, out var item) || item.IsOccupied)
                return "床或沙发尚未稳定识别或正在被使用。";
            if (!scanner.IsSafe(bed)) return "躺卧面或躺卧路径空间不足。";
            var fit = rig.Check(bed);
            if (fit != null) return fit;
            if (!BedMountPlanner.TryCreate(bed, motor.Position, navigation,
                    scanner.Geometry, out mount))
                return "家具旁没有可达且净空充足的躺卧位置。";
            movement = new MoveAction(motor, navigation, () => mount.Dock);
            return movement.Check();
        }

        public void Start()
        {
            reservation = items.Reserve(targetId, RealityInteractionIds.Lie, this);
            if (reservation == null) throw new InvalidOperationException("躺卧位置已被占用。");
            started = true;
            movement.Start();
            Debug.Log($"MATESOUL_BED semantic={bed.Semantic} phase=walking id={targetId} " +
                      $"center={bed.Center:F3} width={bed.Width:F3} length={bed.Length:F3} height=" +
                      $"{bed.Center.y - bed.FloorY:F3} edge={mount.Edge} " +
                      $"dock={mount.Dock:F3} planeError={bed.PlaneError:F4}");
            Debug.Log($"MATESOUL_BED headCue={bed.HeadCue} forward={bed.Forward:F3}");
        }

        public ActionProgress Tick(float dt)
        {
            phaseTime += dt;
            if (!exiting && !reservation.IsValid)
            {
                Debug.LogWarning("MATESOUL_BED reservation_lost; exit to approach point");
                Cancel(ActionStopReason.TargetLost);
            }
            if (!exiting && phase <= 3)
            {
                // 扫描每 0.5 秒重算一次净空/朝向，单次抖动不该让已经走了大半的
                // 流程原地作废。要求失效持续一段时间才退出。
                if (!scanner.IsSafe(bed) || !scanner.IsMountSafe(bed, mount))
                {
                    lostTime += dt;
                    if (lostTime >= PreMountGraceSeconds)
                    {
                        Debug.LogWarning(
                            $"MATESOUL_BED target_unavailable seconds={lostTime:F2}; " +
                            "exit to approach point");
                        Cancel(ActionStopReason.TargetLost);
                    }
                }
                else lostTime = 0;
            }
            if (!exiting && phase > 3)
            {
                // The mesh keeps refining under the body, so a scan can disagree
                // once. Standing up on that single scan is what interrupted a
                // completed lie; require the loss to persist instead.
                if (scanner.IsSafe(bed)) lostTime = 0;
                else if ((lostTime += dt) >= LyingTargetGraceSeconds)
                {
                    Debug.LogWarning($"MATESOUL_BED target_lost_late seconds={lostTime:F2}");
                    Cancel(ActionStopReason.TargetLost);
                }
            }
            if (exiting && phase < 3)
            {
                if (exitError != null) throw new InvalidOperationException(exitError);
                return ActionProgress.Succeeded;
            }
            if (exiting)
            {
                if (phase != 6)
                {
                    Advance(6);
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
                if (phaseTime > 7f) throw new InvalidOperationException("无法精确到达躺卧位置。");
                // The dock sits right against the seat, so the body collider can
                // stop a few centimetres short. Accept that shortfall instead of
                // abandoning the lie.
                var tolerance = phaseTime > 2f ? .12f : .015f;
                if (motor.Dock(mount.Dock, mount.Rotation, dt, tolerance))
                {
                    // Take the newest measured surface right before contact so a
                    // slightly drifting cushion does not leave the avatar hovering.
                    var latest = scanner.Resolve(targetId);
                    if (latest != null && BedGeometry.SamePose(latest, bed))
                        bed = latest.Copy();
                    Advance(2);
                }
                return ActionProgress.Running;
            }
            if (phase == 2)
            {
                if (!dressed)
                {
                    dressed = true;
                    DressForBed();
                }
                if (phaseTime >= DressingSeconds) Advance(3);
                return ActionProgress.Running;
            }
            if (phase == 3)
            {
                // Planning already excludes the target's own shell, so the transfer
                // has to use the same exclusion or a sofa back or armrest vetoes the
                // landing the planner just approved.
                var ignored = new HashSet<int>();
                BedGeometry.CollectOwnSurfaceSources(bed, scanner.Geometry, ignored);
                motor.BeginLyingContact(mount.Dock, new Pose(bed.ContactRoot, bed.LyingRotation),
                    ignored, bed.IsSofa
                        ? AvatarMotor.SofaLandingTolerance
                        : AvatarMotor.MountClipTolerance);
                contact = true;
                rig.SetPose(bed, 1);
                // The body is on the seat now, so the authored sleep loop takes
                // over from the frozen stand pose until the character gets up.
                rig.BeginSleep();
                Advance(4);
                return ActionProgress.Running;
            }
            if (phase == 4)
            {
                Advance(5);
                return ActionProgress.Running;
            }

            return ActionProgress.Running;
        }

        // 换睡衣是躺卧流程的一部分：走到床边后先换装，再瞬移上床。
        // 没有衣橱（例如纯逻辑测试）时跳过，避免因此阻塞躺床。
        private void DressForBed()
        {
            if (wardrobe == null)
            {
                Debug.LogWarning("MATESOUL_BED dressing_skipped wardrobe_missing");
                return;
            }
            var previous = wardrobe.CurrentOutfitId;
            if (!wardrobe.TrySwitchOutfit(PajamasOutfit, out var error))
                throw new InvalidOperationException("无法换上睡衣：" + error);
            Debug.Log($"MATESOUL_BED dressing outfit={PajamasOutfit} previous={previous}");
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
                exitError = "躺卧面几何或可用空间已变化，已退出躺卧。";
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
