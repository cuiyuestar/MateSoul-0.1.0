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
        private bool started, arrived, didRelease, cancelled;
        private float elapsed;
        private Vector3 ballAtStart;
        public string PhaseId => !arrived ? "approaching" : didRelease ? "watching_ball" : "bowling";
        public string PostureId => !arrived || didRelease ? "standing" : "crouching";

        public BowlingTurnAction(AvatarMotor motor, INavigationService navigation, AvatarBowlingRig rig,
            BowlingBall ball, Vector3 origin, Vector3 target, Action released, Func<bool> finished)
        {
            this.motor = motor; this.rig = rig; this.ball = ball;
            this.origin = origin; this.target = target; this.released = released; this.finished = finished;
            direction = Vector3.ProjectOnPlane(target - origin, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, direction);
            var stand = origin - direction * .4f - right * .28f;
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
                ballAtStart = ball.Body.Pose.position;
                ball.Body.BeginFollow(ball.Body.Pose);
            }
            motor.Face(target, dt);
            elapsed += dt;
            if (!didRelease)
            {
                Vector3 center;
                if (elapsed < 1)
                    center = Vector3.Lerp(ballAtStart, origin + Vector3.up * .55f, Mathf.SmoothStep(0, 1, elapsed));
                else
                    center = Vector3.Lerp(origin - direction * .12f + Vector3.up * .55f,
                        origin + direction * .3f + Vector3.up * .22f,
                        Mathf.SmoothStep(0, 1, (elapsed - 1) / .8f));
                ball.Body.SetFollowTarget(new Pose(center, Quaternion.identity));
                rig.Reach(ball.Body.Pose.position, Mathf.Clamp01(elapsed * 3));
                if (elapsed >= 1.8f)
                {
                    ball.Body.EndFollow();
                    var aim = Quaternion.AngleAxis(UnityEngine.Random.Range(-2.5f, 2.5f), Vector3.up) * direction;
                    var speed = Mathf.Clamp(Vector3.Distance(origin, target) * .65f + 1.5f, 2.4f, 5f);
                    ball.Body.AddForce(aim * speed - ball.Body.Rigidbody.velocity, ForceMode.VelocityChange);
                    // Rolling angular speed is derived from the actual ball radius.
                    var spin = Vector3.Cross(Vector3.up, aim) * (speed / BowlingGame.BallRadius);
                    ball.Body.AddTorque(spin - ball.Body.Rigidbody.angularVelocity, ForceMode.VelocityChange);
                    didRelease = true;
                    released();
                }
            }
            else rig.Reach(ball.Body.Pose.position, Mathf.Clamp01(1 - (elapsed - 1.8f) * 3));
            return finished() ? ActionProgress.Succeeded : ActionProgress.Running;
        }

        public void Cancel(ActionStopReason reason) => cancelled = true;
        public void Dispose()
        {
            if (!started) return;
            movement.Dispose();
            rig.Release();
            if (ball == null) return;
            ball.Body.EndFollow();
            foreach (var shape in motor.Body.Shapes)
                foreach (var ballShape in ball.Body.Shapes) Physics.IgnoreCollision(shape, ballShape, false);
        }
    }
}
