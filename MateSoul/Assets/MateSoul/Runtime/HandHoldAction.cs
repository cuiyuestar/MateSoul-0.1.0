using System;
using MateSoul.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public sealed class HandHoldAction : IAvatarAction, IActionRuntimeStatus
    {
        public const string LeftId = "hold-left-hand";
        public const string RightId = "hold-right-hand";

        private const float PathGraceSeconds = .25f;
        private const float DiagnosticInterval = .25f;
        private readonly AvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly HandIKRig rig;
        private readonly HandHoldRequest request;
        private readonly Transform playerHead;
        private readonly HandHoldTractionController traction = new HandHoldTractionController();
        private readonly HandHoldLeadController lead = new HandHoldLeadController();
        private bool started, exiting, blocked;
        private float previousExtension;
        private Vector3 previousHandPosition;
        private Vector3 previousPlayerPosition;
        private Vector3 handVelocity;
        private Vector3 playerVelocity;
        private Vector3 pathDirection;
        private Vector3 lastPathDirection;
        private float repathRemaining, blockedTime, hapticRemaining;
        private float pathGrace, diagnosticRemaining;

        public string PhaseId => exiting ? "releasing" :
            blocked ? "blocked" : traction.Engaged || lead.Engaged ? "following" : "holding";
        public string PostureId => "standing";

        public HandHoldAction(AvatarMotor motor, INavigationService navigation,
            HandIKRig rig, HandHoldRequest request, Transform playerHead)
        {
            this.motor = motor;
            this.navigation = navigation;
            this.rig = rig;
            this.request = request;
            this.playerHead = playerHead;
        }

        public string Check()
            => motor == null || navigation == null || rig == null
                ? "角色牵手运行时不可用。"
                : rig.Check(request);

        public void Start()
        {
            if (!request.TryGetInteractorPose(out var pose))
                throw new InvalidOperationException("玩家手部跟踪不可用。");
            started = true;
            rig.BeginHold(request);
            motor.BeginHandHold();
            previousHandPosition = pose.position;
            previousPlayerPosition = playerHead != null ? playerHead.position : pose.position;
            previousExtension = 0;
            pathGrace = PathGraceSeconds;
            diagnosticRemaining = 0;
            Debug.Log($"MATESOUL_HAND_HOLD phase=holding side={request.Side.ToString().ToLowerInvariant()}");
#region debug-point D:handhold-action-start
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"D\",\"location\":\"HandHoldAction.Start\"," +
                $"\"msg\":\"[DEBUG] handhold action started\",\"data\":{{" +
                $"\"side\":\"{request.Side}\",\"pose\":{JsonUtility.ToJson(pose.position)}," +
                $"\"motorPosition\":{JsonUtility.ToJson(motor.Position)}}}}}";
            var debugRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7781/event", payload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
        }

        public ActionProgress Tick(float deltaSeconds)
        {
            if (exiting)
            {
                motor.SetHandHoldVelocity(Vector3.zero);
                return rig.BlendTowards(0, deltaSeconds)
                    ? ActionProgress.Succeeded : ActionProgress.Running;
            }
            if (!request.TryGetInteractorPose(out var interactor))
            {
                BeginExit(true, "tracking_lost");
                return ActionProgress.Running;
            }

            var sample = rig.UpdateTarget(interactor, deltaSeconds);
            if (sample.TrackingJump)
            {
                BeginExit(true, "tracking_jump");
                return ActionProgress.Running;
            }
            rig.BlendTowards(1, deltaSeconds);

            var velocityAlpha = HandHoldMath.ExpSmoothing(12, deltaSeconds);
            var nextHandVelocity = deltaSeconds > 0
                ? (interactor.position - previousHandPosition) / deltaSeconds : Vector3.zero;
            handVelocity = Vector3.Lerp(handVelocity,
                Vector3.ClampMagnitude(nextHandVelocity, 4), velocityAlpha);
            previousHandPosition = interactor.position;
            var playerPosition = playerHead != null ? playerHead.position : interactor.position;
            var nextPlayerVelocity = deltaSeconds > 0
                ? (playerPosition - previousPlayerPosition) / deltaSeconds : Vector3.zero;
            playerVelocity = Vector3.Lerp(playerVelocity,
                Vector3.ClampMagnitude(nextPlayerVelocity, 3), velocityAlpha);
            previousPlayerPosition = playerPosition;

            var outwardSpeed = deltaSeconds > 0
                ? Mathf.Max(0, sample.Extension - previousExtension) / deltaSeconds : 0;
            previousExtension = sample.Extension;
            var force = HandHoldMath.ProxyForce(sample.Extension,
                sample.TensionDirection, outwardSpeed, rig.Tuning);
            var requestedVelocity = traction.Tick(force,
                playerVelocity, handVelocity, deltaSeconds, rig.Tuning);
            var leadVelocity = lead.Tick(playerVelocity, handVelocity, deltaSeconds, rig.Tuning);
            if (leadVelocity.sqrMagnitude > requestedVelocity.sqrMagnitude)
                requestedVelocity = leadVelocity;
            var safeVelocity = ResolveNavigationVelocity(requestedVelocity, deltaSeconds);
            motor.SetHandHoldVelocity(safeVelocity);
            LogFollowDiagnostics(sample, requestedVelocity, safeVelocity, deltaSeconds);

            var constrainedTooFar = sample.TargetSeparation >
                                    rig.Tuning.maximumTargetSeparation;
            blockedTime = constrainedTooFar ? blockedTime + deltaSeconds : 0;
            if (blockedTime >= rig.Tuning.blockedReleaseDelay)
            {
                BeginExit(true, blocked ? "path_blocked" : "reach_exceeded");
                return ActionProgress.Running;
            }
            UpdateHaptics(sample, deltaSeconds);
            return ActionProgress.Running;
        }

        private Vector3 ResolveNavigationVelocity(Vector3 requested, float deltaSeconds)
        {
            requested.y = 0;
            if (requested.sqrMagnitude < .0004f)
            {
                blocked = false;
                pathDirection = Vector3.zero;
                pathGrace = PathGraceSeconds;
                return Vector3.zero;
            }
            repathRemaining -= deltaSeconds;
            var requestedDirection = requested.normalized;
            if (repathRemaining <= 0 || pathDirection.sqrMagnitude < .5f ||
                Vector3.Dot(pathDirection, requestedDirection) < .8f)
            {
                repathRemaining = .15f;
                var probe = motor.Position + requestedDirection * .8f;
                if (!navigation.TryPath(motor.Position, probe, out var corners))
                {
                    // A probe can fail on a navmesh seam while the character can still
                    // walk. Hold the last validated direction for a short grace instead
                    // of freezing the follow until the arm stretches out of range.
                    if (pathGrace > 0 && lastPathDirection.sqrMagnitude > .5f)
                    {
                        pathGrace -= deltaSeconds;
                        blocked = false;
                        pathDirection = lastPathDirection;
                        return pathDirection * requested.magnitude;
                    }
                    blocked = true;
                    pathDirection = Vector3.zero;
                    return Vector3.zero;
                }
                pathGrace = PathGraceSeconds;
                pathDirection = requestedDirection;
                for (var index = 0; index < corners.Length; index++)
                {
                    var offset = Vector3.ProjectOnPlane(corners[index] - motor.Position, Vector3.up);
                    if (offset.sqrMagnitude <= .0025f) continue;
                    pathDirection = offset.normalized;
                    break;
                }
                lastPathDirection = pathDirection;
            }
            blocked = false;
            return pathDirection * requested.magnitude;
        }

        private void LogFollowDiagnostics(HandHoldRigSample sample, Vector3 requested,
            Vector3 safe, float deltaSeconds)
        {
            diagnosticRemaining -= deltaSeconds;
            if (diagnosticRemaining > 0) return;
            diagnosticRemaining = DiagnosticInterval;
            var playerSpeed = new Vector2(playerVelocity.x, playerVelocity.z).magnitude;
            var handSpeed = new Vector2(handVelocity.x, handVelocity.z).magnitude;
            var coherence = playerSpeed > .001f && handSpeed > .001f
                ? Vector3.Dot(playerVelocity / playerSpeed, handVelocity / handSpeed) : 0;
            Debug.Log($"MATESOUL_HAND_HOLD phase={PhaseId} " +
                      $"side={request.Side.ToString().ToLowerInvariant()} " +
                      $"player={playerSpeed:F2} hand={handSpeed:F2} dot={coherence:F2} " +
                      $"lead={(lead.Engaged ? 1 : 0)} " +
                      $"traction={(traction.Engaged ? 1 : 0)} " +
                      $"blocked={(blocked ? 1 : 0)} requested={requested.magnitude:F2} " +
                      $"safe={safe.magnitude:F2} separation={sample.TargetSeparation:F2} " +
                      $"motor=({motor.Position.x:F2},{motor.Position.z:F2})");
        }

        private void UpdateHaptics(HandHoldRigSample sample, float deltaSeconds)
        {
            hapticRemaining -= deltaSeconds;
            if (hapticRemaining > 0) return;
            var utilization = Mathf.Max(sample.ReachUtilization, sample.RotationUtilization);
            if (!blocked && utilization < rig.Tuning.wrist.softLimitRatio) return;
            request.SendHaptic(Mathf.Lerp(.08f, .38f,
                Mathf.InverseLerp(rig.Tuning.wrist.softLimitRatio, 1, utilization)), .035f);
            hapticRemaining = .14f;
        }

        private void BeginExit(bool endSelection, string reason)
        {
            if (exiting) return;
            exiting = true;
            motor.EndHandHold();
            traction.Reset();
            lead.Reset();
            if (endSelection) request.EndSelection();
            Debug.Log($"MATESOUL_HAND_HOLD phase=releasing reason={reason}");
#region debug-point D:handhold-action-exit
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"D\",\"location\":\"HandHoldAction.BeginExit\"," +
                $"\"msg\":\"[DEBUG] handhold action exiting\",\"data\":{{" +
                $"\"side\":\"{request.Side}\",\"reason\":\"{reason}\"," +
                $"\"blocked\":{blocked.ToString().ToLowerInvariant()}," +
                $"\"endSelection\":{endSelection.ToString().ToLowerInvariant()}}}}}";
            var debugRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7781/event", payload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
        }

        public void Cancel(ActionStopReason reason)
            => BeginExit(false, reason.ToString().ToLowerInvariant());

        public void Dispose()
        {
            if (started)
            {
                motor.EndHandHold();
                rig.EndHold();
            }
            request?.EndSelection();
        }
    }
}
