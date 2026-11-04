using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class CutFruitAction : ItemActionRun
    {
        public const string Id = "cut-fruit";
        private ItemActionBinding[] pieces;
        private int workspaceSide = -1;
        public CutFruitAction(GameSession session) : base(session,
            new ItemActionRequirement("fruit", "cuttable-fruit", "苹果"),
            new ItemActionRequirement("knife", "cutting-tool", "刀"),
            new ItemActionRequirement("plate", "food-receiver", "盘子"),
            new ItemActionRequirement("table", "food-workstation", "餐桌")) { }

        protected override string CheckBindings()
        {
            var table = Bindings["table"].View;
            if (!table.IsFixedAfterPlacement || Vector3.Dot(table.transform.up, Vector3.up) < .99f)
                return "餐桌需要稳定、水平地放置。";
            if (Bindings["fruit"].Profile.cutResult == null)
                return "苹果缺少切块模型。";
            return TrySelectWorkspaceSide()
                ? null : "餐桌两条长边都无法使用，请清理周围通道。";
        }

        private Vector3 Oriented(Vector3 point)
            => workspaceSide < 0
                ? point
                : new Vector3(-point.x, point.y, -point.z);

        private Vector3 World(Vector3 point)
            => Bindings["table"].View.transform.TransformPoint(
                Oriented(point));

        private Quaternion WorkspaceRotation
            => Bindings["table"].View.Body.Pose.rotation *
               (workspaceSide < 0
                   ? Quaternion.identity
                   : Quaternion.Euler(0, 180, 0));

        private Pose At(Vector3 point)
            => new Pose(World(point), WorkspaceRotation);

        private Vector3 DockForSide(Vector3 point, int side)
        {
            var local = Bindings["table"].Profile.approachPosition;
            local.x = point.x - .15f;
            if (side > 0)
                local = new Vector3(-local.x, local.y, -local.z);
            return Bindings["table"].View.transform.TransformPoint(local);
        }

        private Vector3 Dock(Vector3 point)
            => DockForSide(point, workspaceSide);

        private bool TrySelectWorkspaceSide()
        {
            var profile = Bindings["table"].Profile;
            var points = new[]
            {
                profile.platePosition,
                profile.workPosition,
                profile.toolPosition
            };
            var bestSide = 0;
            var bestScore = float.PositiveInfinity;
            foreach (var side in new[] { -1, 1 })
            {
                var score = 0f;
                var valid = true;
                foreach (var point in points)
                {
                    var requested = DockForSide(point, side);
                    requested.y = Session.Navigation.FloorHeight;
#region debug-point A-B-C-E:workspace-side-candidate
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    var debugSampledOk = Session.Navigation.TrySample(
                        requested, .08f, out var debugSampled);
                    Vector3[] debugCorners = null;
                    var debugPathOk = debugSampledOk &&
                                      Session.Navigation.TryPath(
                                          Motor.Position, debugSampled,
                                          out debugCorners);
                    var debugTargetLocal = side < 0
                        ? point
                        : new Vector3(-point.x, point.y, -point.z);
                    var debugTarget = Bindings["table"].View.transform
                        .TransformPoint(debugTargetLocal);
                    var debugDirection = Vector3.ProjectOnPlane(
                        debugTarget - debugSampled, Vector3.up);
                    var debugCanOccupy = debugSampledOk &&
                                         debugDirection.sqrMagnitude >= .001f &&
                                         Motor.Body.CanOccupy(new Pose(
                                             debugSampled + Vector3.up * .025f,
                                             Quaternion.LookRotation(debugDirection)));
                    var debugCornerJson = debugCorners == null
                        ? ""
                        : string.Join(",", Array.ConvertAll(
                            debugCorners, value => JsonUtility.ToJson(value)));
                    var debugPayload =
                        $"{{\"sessionId\":\"item-seat-navigation-fail\",\"runId\":\"post-fix\"," +
                        $"\"hypothesisId\":\"A-B-C-E\",\"location\":\"CutFruitAction.TrySelectWorkspaceSide\"," +
                        $"\"msg\":\"[DEBUG] workspace side candidate\",\"data\":{{" +
                        $"\"frame\":{Time.frameCount},\"side\":{side}," +
                        $"\"point\":{JsonUtility.ToJson(point)}," +
                        $"\"tablePosition\":{JsonUtility.ToJson(Bindings["table"].View.transform.position)}," +
                        $"\"tableRotation\":{JsonUtility.ToJson(Bindings["table"].View.transform.rotation)}," +
                        $"\"origin\":{JsonUtility.ToJson(Motor.Position)}," +
                        $"\"requested\":{JsonUtility.ToJson(requested)}," +
                        $"\"sampledOk\":{debugSampledOk.ToString().ToLowerInvariant()}," +
                        $"\"sampled\":{JsonUtility.ToJson(debugSampled)}," +
                        $"\"sampleError\":{(debugSampledOk ? Vector3.Distance(debugSampled, requested) : -1)}," +
                        $"\"pathOk\":{debugPathOk.ToString().ToLowerInvariant()}," +
                        $"\"corners\":[{debugCornerJson}]," +
                        $"\"directionSqr\":{debugDirection.sqrMagnitude}," +
                        $"\"canOccupy\":{debugCanOccupy.ToString().ToLowerInvariant()}}}}}";
                    var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                        Application.isEditor
                            ? "http://127.0.0.1:7780/event"
                            : "http://127.0.0.1:7780/event",
                        debugPayload, "application/json");
                    debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
                    if (!Session.Navigation.TrySample(
                            requested, .08f, out var sampled) ||
                        Vector3.Distance(sampled, requested) > .08f ||
                        !Session.Navigation.TryPath(
                            Motor.Position, sampled, out var corners))
                    {
                        valid = false;
                        break;
                    }
                    var targetLocal = side < 0
                        ? point
                        : new Vector3(-point.x, point.y, -point.z);
                    var target = Bindings["table"].View.transform
                        .TransformPoint(targetLocal);
                    var direction = Vector3.ProjectOnPlane(
                        target - sampled, Vector3.up);
                    if (direction.sqrMagnitude < .001f ||
                        !Motor.Body.CanOccupy(new Pose(
                            sampled + Vector3.up * .025f,
                            Quaternion.LookRotation(direction))))
                    {
                        valid = false;
                        break;
                    }
                    score += PathLength(Motor.Position, corners);
                }
                if (!valid || score >= bestScore) continue;
                bestScore = score;
                bestSide = side;
            }
            if (bestSide == 0) return false;
            workspaceSide = bestSide;
            return true;
        }

        private static float PathLength(
            Vector3 origin, IReadOnlyList<Vector3> corners)
        {
            var length = 0f;
            var previous = origin;
            for (var index = 0; index < corners.Count; index++)
            {
                length += Vector3.Distance(previous, corners[index]);
                previous = corners[index];
            }
            return length;
        }

        internal static bool IsAtWorkspace(
            ItemActionBinding binding, Pose target)
        {
            if (binding?.View?.Body == null ||
                !binding.View.Body.HasSupport())
                return false;
            var current = binding.View.Body.Pose;
            return Mathf.Abs(current.position.y - target.position.y) <= .08f &&
                   Vector3.ProjectOnPlane(
                       current.position - target.position,
                       Vector3.up).magnitude <= .12f &&
                   Vector3.Dot(
                       current.rotation * Vector3.up,
                       target.rotation * Vector3.up) >= .85f;
        }

        protected override IEnumerable<IItemActionStep> BuildSteps()
        {
            var table = Bindings["table"];
            var workspace = table.Profile;
            var plateTarget = At(workspace.platePosition);
            var fruitTarget = At(workspace.workPosition);
            var knifeTarget = At(workspace.toolPosition);
#region debug-point D:workspace-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugWorkspacePayload =
                $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"D\",\"location\":\"CutFruitAction.BuildSteps\"," +
                $"\"msg\":\"[DEBUG] workspace placement state\",\"data\":{{" +
                $"\"workspaceSide\":{workspaceSide}," +
                $"\"plateDistance\":{Vector3.Distance(Bindings["plate"].View.Body.Pose.position, plateTarget.position)}," +
                $"\"plateAngle\":{Quaternion.Angle(Bindings["plate"].View.Body.Pose.rotation, plateTarget.rotation)}," +
                $"\"fruitDistance\":{Vector3.Distance(Bindings["fruit"].View.Body.Pose.position, fruitTarget.position)}," +
                $"\"fruitAngle\":{Quaternion.Angle(Bindings["fruit"].View.Body.Pose.rotation, fruitTarget.rotation)}," +
                $"\"knifeDistance\":{Vector3.Distance(Bindings["knife"].View.Body.Pose.position, knifeTarget.position)}," +
                $"\"knifeAngle\":{Quaternion.Angle(Bindings["knife"].View.Body.Pose.rotation, knifeTarget.rotation)}}}}}";
            var debugWorkspaceRequest = UnityEngine.Networking.UnityWebRequest.Post(
                Application.isEditor
                    ? "http://127.0.0.1:7781/event"
                    : "http://127.0.0.1:7781/event",
                debugWorkspacePayload, "application/json");
            debugWorkspaceRequest.SendWebRequest().completed += _ => debugWorkspaceRequest.Dispose();
#endif
#endregion
            if (!IsAtWorkspace(Bindings["plate"], plateTarget))
                yield return new ItemTransferStep("把盘子放到餐桌", Session, Rig,
                    Bindings["plate"], RunId, () => At(workspace.platePosition),
                    Dock(workspace.platePosition));
            if (!IsAtWorkspace(Bindings["fruit"], fruitTarget))
                yield return new ItemTransferStep("把苹果放到餐桌", Session, Rig,
                    Bindings["fruit"], RunId, () => At(workspace.workPosition),
                    Dock(workspace.workPosition));
            if (!IsAtWorkspace(Bindings["knife"], knifeTarget))
                yield return new ItemTransferStep("把刀拿到餐桌", Session, Rig,
                    Bindings["knife"], RunId,
                    () => At(workspace.toolPosition),
                    Dock(workspace.toolPosition), true);
            var cutDock = Dock(workspace.toolPosition) +
                          table.View.transform.forward *
                          (.04f * workspaceSide);
            yield return new KnifeCutStep(Session, Rig, Bindings["fruit"], Bindings["knife"],
                RunId, this, cutDock, result =>
                {
                    pieces = result;
                    for (var i = 0; i < pieces.Length; i++) Bindings.Add("piece-" + i, pieces[i]);
                });
            for (var i = 0; i < pieces.Length; i++)
            {
                var index = i;
                yield return new ItemTransferStep("将苹果块装入盘子", Session, Rig, pieces[i],
                    RunId, () =>
                    {
                        var plate = Bindings["plate"].View.Body.Pose;
                        var offset = new Vector3(index % 2 == 0 ? -.027f : .027f,
                            Bindings["plate"].Profile.receivingHeight + .003f,
                            index < 2 ? -.027f : .027f);
                        return new Pose(plate.position + plate.rotation * offset,
                            plate.rotation * Quaternion.Euler(0, index * 90, 0));
                    }, Dock(workspace.platePosition));
            }
        }
    }

    internal sealed class KnifeCutStep : IItemActionStep
    {
        private readonly GameSession session;
        private readonly AvatarMotor motor;
        private readonly AvatarItemRig rig;
        private readonly ItemActionBinding fruit, knife;
        private readonly Guid run;
        private readonly object owner;
        private readonly Vector3 dock;
        private readonly Action<ItemActionBinding[]> committed;
        private readonly List<(Collider, Collider, bool)> ignored = new List<(Collider, Collider, bool)>();
        private MoveAction movement;
        private Pose rest, drivenPose, gestureBase, gestureRaised;
        private Quaternion workFacing;
        private int phase;
        private float time;
        private bool gestureInitialized;
        private bool attentionActive;
        private Pose postCutRaised;
#if UNITY_EDITOR
        private float debugNextTopAlignment;
        private float debugNextPhaseState;
        private float debugNextStrokeState;
        private int debugLastPhase = -1;
#endif
        public string Label => "切开苹果";

        public KnifeCutStep(GameSession session, AvatarItemRig rig, ItemActionBinding fruit,
            ItemActionBinding knife, Guid run, object owner, Vector3 dock,
            Action<ItemActionBinding[]> committed)
        {
            this.session = session; this.rig = rig; this.fruit = fruit; this.knife = knife;
            this.run = run; this.owner = owner; this.dock = dock; this.committed = committed;
            motor = (AvatarMotor)session.Motor;
        }

        public void Start()
        {
            rest = knife.View.Body.Pose;
            movement = new MoveAction(motor, session.Navigation, () => dock);
            if (movement.Check() != null) throw new InvalidOperationException("餐桌工作位置不可达。");
            movement.Start();
        }

        private void SetAttention(Transform target, bool enabled)
        {
            if (attentionActive == enabled && !enabled) return;
            attentionActive = enabled;
            session.SetItemActionAttention(enabled ? target : null, enabled);
        }

        private bool MoveKnife(Pose target, float dt, float gripTolerance = .02f)
        {
            var body = knife.View.Body;
            drivenPose = new Pose(
                Vector3.MoveTowards(drivenPose.position, target.position, dt * .3f),
                Quaternion.RotateTowards(drivenPose.rotation, target.rotation, dt * 100));
            var drivenGrip = rig.GripForHeldPosition(
                drivenPose.position);
            rig.Reach(drivenGrip);
            var positionDistance = Vector3.Distance(body.Pose.position, target.position);
            var gripDistance = Vector3.Distance(
                knife.Profile.WorldGrip(body.Pose).position,
                rig.GripForHeldPosition(target.position).position);
            var rotationDistance = Quaternion.Angle(
                body.Pose.rotation, target.rotation);
#region debug-point A-B:knife-move-gate
#if UNITY_EDITOR
            if (Time.unscaledTime >= debugNextPhaseState)
            {
                debugNextPhaseState = Time.unscaledTime + .25f;
                var payload =
                    $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"grip-redesign-phase\"," +
                    $"\"hypothesisId\":\"A-B\",\"location\":\"KnifeCutStep.MoveKnife\"," +
                    $"\"msg\":\"[DEBUG] knife move gate\",\"data\":{{\"phase\":{phase}," +
                    $"\"phaseTime\":{time},\"positionDistance\":{positionDistance}," +
                    $"\"gripDistance\":{gripDistance},\"gripTolerance\":{gripTolerance}," +
                    $"\"rotationDistance\":{rotationDistance}," +
                    $"\"bodyPosition\":{JsonUtility.ToJson(body.Pose.position)}," +
                    $"\"targetPosition\":{JsonUtility.ToJson(target.position)}," +
                    $"\"drivenPosition\":{JsonUtility.ToJson(drivenPose.position)}," +
                    $"\"bodyRotation\":{JsonUtility.ToJson(body.Pose.rotation)}," +
                    $"\"targetRotation\":{JsonUtility.ToJson(target.rotation)}," +
                    $"\"contactError\":{rig.ContactError}}}}}";
                var request = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7786/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
            return positionDistance < gripTolerance;
        }

        private Pose NaturalKnifeReleasePose(Pose support)
        {
            var body = knife.View.Body;
            var bottom = body.Pose.position.y;
            foreach (var shape in body.Shapes)
                if (shape != null && shape.enabled)
                    bottom = Mathf.Min(bottom, shape.bounds.min.y);
            var pose = body.Pose;
            pose.position = support.position;
            pose.position.y += body.Pose.position.y - bottom + .005f;
            return pose;
        }

        private void Next(int value) { phase = value; time = 0; }

        public bool Tick(float dt)
        {
            time += dt;
#region debug-point C:knife-phase-state
#if UNITY_EDITOR
            if (debugLastPhase != phase)
            {
                debugLastPhase = phase;
                var phasePayload =
                    $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"grip-redesign-phase\"," +
                    $"\"hypothesisId\":\"C\",\"location\":\"KnifeCutStep.Tick\"," +
                    $"\"msg\":\"[DEBUG] knife phase entered\",\"data\":{{\"phase\":{phase}," +
                    $"\"phaseTime\":{time},\"bodyPosition\":" +
                    $"{JsonUtility.ToJson(knife.View.Body.Pose.position)},\"restPosition\":" +
                    $"{JsonUtility.ToJson(rest.position)}}}}}";
                var phaseRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7786/event", phasePayload, "application/json");
                phaseRequest.SendWebRequest().completed += _ => phaseRequest.Dispose();
            }
#endif
#endregion
            if (time > 20) throw new InvalidOperationException("切割步骤超时。");
            switch (phase)
            {
                case 0:
                    if (movement.Tick(dt) != MateSoul.Core.ActionProgress.Succeeded) return false;
                    SetAttention(knife.View.transform, true);
                    Next(1);
                    break;
                case 1:
                    var direction = fruit.View.Body.Pose.position - dock;
                    direction.y = 0;
                    workFacing = Quaternion.LookRotation(direction);
                    if (!motor.Dock(dock, workFacing, dt)) return false;
                    Next(2);
                    break;
                case 2:
                    rig.Motion(ItemMotion.Reach, Mathf.Min(1, time));
                    rig.Reach(knife.Profile.WorldGrip(knife.View.Body.Pose), Mathf.Min(1, time));
                    if (time < 1.2f) return false;
                    if (!ItemTransferStep.CanSnapPickup(rig.ContactError))
                        throw new InvalidOperationException("刀柄仍超出冷月手部可吸附范围。");
                    if (!knife.View.Body.IsInteractionControlled)
                        knife.View.Stabilize(knife.Lease);
                    knife.Lease.Transition(
                        knife.Lease.State,
                        ItemControlState.HeldByCharacter);
                    if (!knife.View.Body.SetInteractionCollisions(run, false))
                        throw new InvalidOperationException("刀具无法进入无碰撞持有状态。");
                    if (!knife.View.Body.TryMoveInteraction(
                            run, knife.Profile.ItemFromGrip(rig.HandGrip)))
                        throw new InvalidOperationException("刀柄无法吸附到手心。");
                    rig.AttachHeld(run, knife.View.Body, knife.Profile);
                    drivenPose = knife.View.Body.Pose;
                    foreach (var a in knife.View.Body.Shapes)
                        foreach (var b in fruit.View.Body.Shapes)
                        {
                            ignored.Add((a, b, Physics.GetIgnoreCollision(a, b)));
                            Physics.IgnoreCollision(a, b, true);
                        }
                    Next(3);
                    break;
                case 3:
                    SetAttention(fruit.View.transform, true);
                    var prepareBody = Mathf.Clamp01(time / .8f);
                    rig.Motion(ItemMotion.Cut, prepareBody);
                    if (!gestureInitialized)
                    {
                        gestureBase = knife.View.Body.Pose;
                        gestureRaised = gestureBase;
                        gestureRaised.position += Vector3.up * .12f;
                        drivenPose = gestureBase;
                        gestureInitialized = true;
                    }
                    var raisedArrived = MoveKnife(
                        gestureRaised, dt, .04f);
#region debug-point A-C:knife-prepare-gate
#if UNITY_EDITOR
                    if (raisedArrived || time >= 19.5f)
                    {
                        var preparePayload =
                            $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"grip-redesign-phase\"," +
                            $"\"hypothesisId\":\"A-C\",\"location\":\"KnifeCutStep.PrepareGate\"," +
                            $"\"msg\":\"[DEBUG] knife prepare gate\",\"data\":{{\"phase\":{phase}," +
                            $"\"phaseTime\":{time},\"prepareBody\":{prepareBody}," +
                            $"\"raisedArrived\":{raisedArrived.ToString().ToLowerInvariant()}," +
                            $"\"distance\":{Vector3.Distance(knife.View.Body.Pose.position, gestureRaised.position)}," +
                            $"\"contactError\":{rig.ContactError}}}}}";
                        var prepareRequest = UnityEngine.Networking.UnityWebRequest.Post(
                            "http://127.0.0.1:7786/event", preparePayload, "application/json");
                        prepareRequest.SendWebRequest().completed += _ => prepareRequest.Dispose();
                    }
#endif
#endregion
                    if (prepareBody < 1) return false;
                    Next(4);
                    break;
                case 4:
                    rig.Motion(ItemMotion.Cut, 1);
                    MoveKnife(gestureRaised, dt, .04f);
                    var topBlade = knife.View.Body.Pose;
                    var topMidpoint = topBlade.position + topBlade.rotation *
                        ((knife.Profile.bladeStart + knife.Profile.bladeEnd) * .5f);
                    var topOffset = Vector3.ProjectOnPlane(
                        topMidpoint - fruit.View.Body.Pose.position, Vector3.up);
#region debug-point A-B-D:knife-top-alignment
#if UNITY_EDITOR
                    if (Time.unscaledTime >= debugNextTopAlignment)
                    {
                        debugNextTopAlignment = Time.unscaledTime + .25f;
                        var topPayload =
                            $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"grip-redesign\"," +
                            $"\"hypothesisId\":\"A-B-D\",\"location\":\"KnifeCutStep.TopAlignment\"," +
                            $"\"msg\":\"[DEBUG] single gesture apex\",\"data\":{{" +
                            $"\"horizontal\":{topOffset.magnitude},\"contactError\":{rig.ContactError}," +
                            $"\"actualPosition\":{JsonUtility.ToJson(topBlade.position)}," +
                            $"\"targetPosition\":{JsonUtility.ToJson(gestureRaised.position)}," +
                            $"\"actualRotation\":{JsonUtility.ToJson(topBlade.rotation)}," +
                            $"\"targetRotation\":{JsonUtility.ToJson(gestureRaised.rotation)}}}}}";
                        var topRequest = UnityEngine.Networking.UnityWebRequest.Post(
                            "http://127.0.0.1:7786/event",
                            topPayload, "application/json");
                        topRequest.SendWebRequest().completed += _ => topRequest.Dispose();
                    }
#endif
#endregion
                    if (time < .2f) return false;
                    Next(5);
                    break;
                case 5:
                    var cutProgress = Mathf.Clamp01(time / .7f);
                    rig.Motion(ItemMotion.Cut, cutProgress);
                    var commandHeight = Mathf.Lerp(
                        .12f, .015f, Mathf.SmoothStep(0, 1, cutProgress));
                    var strokePose = gestureBase;
                    strokePose.position += Vector3.up * commandHeight;
                    MoveKnife(strokePose, dt, .04f);
                    var blade = knife.View.Body.Pose;
                    var midpoint = blade.position + blade.rotation *
                        ((knife.Profile.bladeStart + knife.Profile.bladeEnd) * .5f);
                    var fruitPosition = fruit.View.Body.Pose.position;
                    var height = midpoint.y - fruitPosition.y;
                    var horizontalOffset = Vector3.ProjectOnPlane(
                        midpoint - fruitPosition, Vector3.up);
#region debug-point E-F:knife-stroke-trajectory
#if UNITY_EDITOR
                    if (Time.unscaledTime >= debugNextStrokeState)
                    {
                        debugNextStrokeState = Time.unscaledTime + .1f;
                        var strokeHorizontal = Vector3.ProjectOnPlane(
                            midpoint - fruitPosition, Vector3.up).magnitude;
                        var strokeGripDistance = Vector3.Distance(
                            knife.Profile.WorldGrip(knife.View.Body.Pose).position,
                            rig.GripForHeldPosition(strokePose.position).position);
                        var strokeRotationDistance = Quaternion.Angle(
                            knife.View.Body.Pose.rotation, strokePose.rotation);
                        var strokePayload = FormattableString.Invariant(
                            $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"single-cut-post-fix\",\"hypothesisId\":\"E-F\",\"location\":\"KnifeCutStep.StrokeTrajectory\",\"msg\":\"[DEBUG] single cut gesture\",\"data\":{{\"phaseTime\":{time},\"progress\":{cutProgress},\"commandHeight\":{commandHeight},\"bladeHeight\":{height},\"horizontal\":{strokeHorizontal},\"gripDistance\":{strokeGripDistance},\"rotationDistance\":{strokeRotationDistance}}}}}");
                        var strokeRequest = UnityEngine.Networking.UnityWebRequest.Post(
                            "http://127.0.0.1:7786/event", strokePayload, "application/json");
                        strokeRequest.SendWebRequest().completed += _ => strokeRequest.Dispose();
                    }
#endif
#endregion
                    if (cutProgress < 1) return false;
                    var origin = fruit.View.Body.Pose;
                    var poses = new Pose[4];
                    for (var i = 0; i < 4; i++)
                    {
                        var rotation = origin.rotation *
                                       Quaternion.Euler(0, i * 90, 0);
                        poses[i] = new Pose(origin.position + rotation *
                            fruit.Profile.cutResult.Interaction.assemblyOffset,
                            rotation);
                    }
                    postCutRaised = knife.View.Body.Pose;
                    postCutRaised.position += Vector3.up * .12f;
#region debug-point G:cut-gate
#if UNITY_EDITOR
                    var payload = FormattableString.Invariant($"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"single-cut-post-fix\",\"hypothesisId\":\"A-B-D\",\"location\":\"KnifeCutStep.Tick\",\"msg\":\"[DEBUG] timed cut commit\",\"data\":{{\"gestureCompleted\":true,\"bladeHeight\":{height},\"horizontal\":{horizontalOffset.magnitude},\"contactError\":{rig.ContactError},\"wristCorrection\":{rig.WristCorrection},\"elbowAngle\":{rig.ElbowAngle},\"heldAlignmentError\":{rig.HeldAlignmentError},\"bladeMidpoint\":{JsonUtility.ToJson(midpoint)},\"fruit\":{JsonUtility.ToJson(fruitPosition)}}}}}");
                    var request = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7786/event", payload, "application/json");
                    request.SendWebRequest().completed += _ => request.Dispose();
#endif
#endregion
                    committed(session.CommitCut(fruit, poses, owner, run));
                    Next(6);
                    break;
                case 6:
                    rig.Motion(ItemMotion.Cut, 1);
                    MoveKnife(postCutRaised, dt, .04f);
                    if (time < .6f) return false;
                    Next(7);
                    break;
                case 7:
                    SetAttention(knife.View.transform, true);
                    rig.Motion(ItemMotion.Reach, 1);
                    var releaseRest = NaturalKnifeReleasePose(rest);
                    var aboveRest = releaseRest;
                    aboveRest.position += Vector3.up * .2f;
                    var restArrived = MoveKnife(
                        time < .6f ? aboveRest : releaseRest, dt, .005f);
                    if (time < .6f || !restArrived ||
                        Vector3.Distance(
                            knife.View.Body.Pose.position,
                            releaseRest.position) > .005f)
                        return false;
                    rig.DetachHeld(run);
                    rig.ClearReach();
                    knife.Lease.Transition(
                        ItemControlState.HeldByCharacter,
                        ItemControlState.StagedForAction);
                    Next(8);
                    break;
                case 8:
                    rig.Motion(ItemMotion.Reach,
                        1 - Mathf.Clamp01(time / .5f),
                        1 - Mathf.Clamp01(time / .5f));
                    knife.View.ReleaseInteractionToPhysics(knife.Lease);
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            movement?.Dispose();
            SetAttention(null, false);
            foreach (var pair in ignored)
                if (pair.Item1 != null && pair.Item2 != null)
                    Physics.IgnoreCollision(pair.Item1, pair.Item2, pair.Item3);
            ignored.Clear();
            rig.ClearReach();
        }
    }
}
