using System;
using MateSoul.Core;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class BowlingTurnAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly AvatarMotor motor;
        private readonly AvatarBowlingRig rig;
        private readonly BowlingBall ball;
        private readonly Vector3 origin, target, direction;
        private readonly Action released;
        private readonly Func<bool> finished;
        private readonly MoveAction movement;
        private readonly Guid run = Guid.NewGuid();
        private bool started, arrived, docked, animationStarted, animationFinished, cancelled;
        private float elapsed;
        private readonly Vector3 stand;
        private readonly Quaternion facing;
        public string PhaseId => !docked ? "approaching" :
            animationFinished || rig.DidRelease ? "watching_ball" : "bowling";
        public string PostureId => !docked || animationFinished || rig.DidRelease ? "standing" : "crouching";

        public BowlingTurnAction(AvatarMotor motor, INavigationService navigation, AvatarBowlingRig rig,
            BowlingBall ball, Vector3 origin, Vector3 target, Action released, Func<bool> finished)
        {
            this.motor = motor; this.rig = rig; this.ball = ball;
            this.origin = origin; this.target = target; this.released = released; this.finished = finished;
            direction = Vector3.ProjectOnPlane(target - origin, Vector3.up).normalized;
            facing = Quaternion.LookRotation(direction);
            stand = rig != null && rig.Motion != null
                ? origin - facing * Vector3.ProjectOnPlane(rig.Motion.pickupCenter, Vector3.up)
                : origin;
            movement = new MoveAction(motor, navigation, () => stand);
        }

        public string Check()
            => rig == null || !rig.IsConfigured || ball == null ? "保龄球动作资源不完整。" : movement.Check();

        public void Start()
        {
            started = true;
            ball.AllowPlayer(false);
            foreach (var shape in motor.Body.Shapes)
                foreach (var ballShape in ball.Body.Shapes) Physics.IgnoreCollision(shape, ballShape, true);
            movement.Start();
        }

        public ActionProgress Tick(float dt)
        {
            if (cancelled) return ActionProgress.Succeeded;
            if (!arrived)
            {
                if (movement.Tick(dt) != ActionProgress.Succeeded) return ActionProgress.Running;
                arrived = true;
            }
            if (!docked)
            {
                if (!motor.Dock(stand, facing, dt)) return ActionProgress.Running;
                docked = true;
                motor.Stop();
                var aim = Quaternion.AngleAxis(UnityEngine.Random.Range(-2.5f, 2.5f), Vector3.up) * direction;
                var speed = Mathf.Clamp(Vector3.Distance(origin, target) * .65f + 1.5f, 2.4f, 5f);
                rig.Begin(run, ball.Body, aim, speed, released);
                animationStarted = true;
            }
            elapsed += dt;
            if (animationStarted && !animationFinished)
            {
                rig.Sample(elapsed);
                if (elapsed >= rig.Motion.clip.length && rig.DidRelease)
                {
                    rig.End(run);
                    animationFinished = true;
                }
            }
            return finished() ? ActionProgress.Succeeded : ActionProgress.Running;
        }

        public void Cancel(ActionStopReason reason) => cancelled = true;
        public void Dispose()
        {
            if (!started) return;
            movement.Dispose();
            rig.End(run);
            if (ball == null) return;
            foreach (var shape in motor.Body.Shapes)
                foreach (var ballShape in ball.Body.Shapes) Physics.IgnoreCollision(shape, ballShape, false);
        }
    }
}
