using System;
using System.Linq;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public enum BowlingPhase
    {
        PlacingPins, PlacingSpawn, Approaching, PlayerReady, PlayerRolling, PlayerResult,
        CompanionPlaying, CompanionRolling, CompanionResult, Winner
    }

    public sealed class BowlingGame : MiniGame
    {
        public const float BallRadius = .10915f;
        public const float PinSpacing = .3048f;
        public const float ScoreDelay = 5f;
        public override string Id => "bowling";
        public override string DisplayName => "保龄球";
        public override string Description => "与冷月轮流滚球，每人一球，击倒一个球瓶得一分。";
        [SerializeField] private BowlingPin pinPrefab;
        [SerializeField] private BowlingBall ballPrefab;
        [SerializeField] private AudioClip cheer, invitation;
        private readonly BowlingPin[] pins = new BowlingPin[10];
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private BowlingBall ball;
        private BowlingPinContacts contacts;
        private Collider[] roomShapes;
        private GameObject heldPreview;
        private Transform[] markers;
        private Material markerMaterial;
        private AudioSource speaker;
        private ActionHandle characterAction;
        private Pose rackPose, spawnPose, candidate;
        private float elapsed, firstHit = -1, quiet;
        private bool autonomyBefore, initialized, releasedInput, pressedInput, returning, resetBlocked;
#region debug-point A-D:approach-observation
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private Vector3 debugApproachStart, debugApproachTarget;
        private float debugNextApproachReport;
#endif
#endregion
        public BowlingPhase Phase { get; private set; }
        public BowlingBall Ball => ball;
        public System.Collections.Generic.IReadOnlyList<BowlingPin> Pins => pins;
        public int PlayerScore { get; private set; }
        public int CompanionScore { get; private set; }
        public override string DialogueLine => Phase == BowlingPhase.PlayerReady && elapsed < 5
            ? "我们一起玩吧，你先来。" : null;
        public override RuntimeActivitySnapshot CaptureRuntimeContext() =>
            new RuntimeActivitySnapshot
            {
                id = Id,
                name = DisplayName,
                state = Phase == BowlingPhase.Winner ? "finished" : "active",
                phase = Phase.ToString().ToLowerInvariant(),
                player_score = PlayerScore,
                companion_score = CompanionScore
            };

        public void Configure(BowlingPin pin, BowlingBall bowlingBall, AudioClip celebration, AudioClip greeting)
        { pinPrefab = pin; ballPrefab = bowlingBall; cheer = celebration; invitation = greeting; }

        public override void RegisterActions(GameSession session)
        {
            if (!session.Catalog.TryGet("bowling-turn", out _))
                session.Catalog.Register(new ActionDefinition("bowling-turn", "Play one bowling turn",
                    ControlChannels.All, intent => (BowlingTurnAction)intent.Payload,
                    timeoutSeconds: 75, exposeToAI: false));
        }

        protected override void Begin()
        {
            if (pinPrefab == null || ballPrefab == null || cheer == null)
                throw new InvalidOperationException("保龄球内容或音效尚未构建。");
            if (Host.Input == null && !Application.isEditor)
                throw new InvalidOperationException("玩家输入尚未就绪。");
            roomShapes = Host.GetComponentsInChildren<Collider>()
                .Where(c => !c.isTrigger && c.attachedRigidbody == null &&
                            c.GetComponentInParent<GameItem>() == null).ToArray();
            autonomyBefore = Session.Autonomy.Enabled;
            Session.Autonomy.Enabled = false;
            initialized = true;
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 0;
            speaker.volume = .65f;
            var markerSource = pinPrefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .FirstOrDefault(material => material != null && material.renderQueue < 3000);
            if (markerSource == null)
                throw new InvalidOperationException("球瓶缺少可用于放置标记的材质。");
            markerMaterial = new Material(markerSource) { name = "Bowling Placement Marker" };
            var markerColor = new Color(.68f, .47f, .85f);
            if (markerMaterial.HasProperty("_BaseColor"))
                markerMaterial.SetColor("_BaseColor", markerColor);
            if (markerMaterial.HasProperty("_Color"))
                markerMaterial.SetColor("_Color", markerColor);
            if (Host.Input != null) Host.Input.ModeChanged += OnModeChanged;
            StartPlacement(BowlingPhase.PlacingPins);
        }

        public static Vector3 PinOffset(int index)
        {
            var row = 0;
            while (index > row) { index -= row + 1; row++; }
            return new Vector3((index - row * .5f) * PinSpacing, 0,
                row * PinSpacing * Mathf.Sqrt(3) * .5f);
        }

        public static Vector3[] CompanionApproachCandidates(Pose spawn, Vector3 rackPosition)
        {
            var direction = Vector3.ProjectOnPlane(rackPosition - spawn.position, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, direction);
            var behind = spawn.position - direction * .4f;
            return new[] { behind - right * .7f, behind + right * .7f };
        }

        private void StartPlacement(BowlingPhase phase)
        {
            Phase = phase;
            releasedInput = pressedInput = false;
            if (Host.Input != null) Host.Input.PlacementActive = true;
            if (heldPreview != null) Destroy(heldPreview);
            var model = (phase == BowlingPhase.PlacingPins ? pinPrefab.transform : ballPrefab.transform).Find("Model");
            heldPreview = Instantiate(model.gameObject, transform);
            heldPreview.name = "手中放置预览";
            if (markers != null) foreach (var marker in markers) Destroy(marker.gameObject);
            markers = new Transform[phase == BowlingPhase.PlacingPins ? 10 : 1];
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "地面放置标记";
                marker.transform.SetParent(transform, false);
                marker.GetComponent<Collider>().enabled = false;
                Destroy(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                marker.transform.localScale = new Vector3(.16f, .003f, .16f);
                markers[i] = marker.transform;
            }
            ShowPlacementHelp();
        }

        private void OnModeChanged(PlayerInteractionMode mode)
        {
            releasedInput = pressedInput = false;
            if (Phase == BowlingPhase.PlacingPins || Phase == BowlingPhase.PlacingSpawn) ShowPlacementHelp();
        }

        private void ShowPlacementHelp() => Notifications.ShowHint(
            (Phase == BowlingPhase.PlacingPins ? "请放置保龄球瓶在地面。\n" : "请设置保龄球出发点。\n") +
            (Host.Input?.PlacementHelp ?? "鼠标指向地面，单击确认。"), 0);

        private void Update()
        {
            if (!initialized || Session.Paused) return;
            var dt = Time.deltaTime;
            elapsed += dt;
            if (Phase == BowlingPhase.PlacingPins || Phase == BowlingPhase.PlacingSpawn)
            { UpdatePlacement(); return; }
#region debug-point A-D:approach-observation
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (Phase == BowlingPhase.Approaching && characterAction != null &&
                Time.unscaledTime >= debugNextApproachReport)
            {
                debugNextApproachReport = Time.unscaledTime + 1f;
                var current = Session.Motor.Position;
                var routeValid = Session.Navigation.TryPath(current, debugApproachTarget, out var route);
                var routeLength = 0f;
                if (route != null)
                    for (var index = 1; index < route.Length; index++)
                        routeLength += Vector3.Distance(route[index - 1], route[index]);
                var avatar = Session.Motor as AvatarMotor;
                var rigidbody = avatar?.Body?.Rigidbody;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var payload = $"{{\"sessionId\":\"bowling-companion-timeout\",\"runId\":\"post-fix\"," +
                              $"\"hypothesisId\":\"A-B-C-D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                              $"\"location\":\"BowlingGame.Update\",\"msg\":\"[DEBUG] approach progress\",\"data\":{{" +
                              $"\"phase\":\"{Phase}\",\"state\":\"{characterAction.State}\"," +
                              $"\"stopReason\":\"{characterAction.StopReason}\"," +
                              $"\"actionElapsed\":{characterAction.ElapsedSeconds.ToString("R", culture)}," +
                              $"\"avatarCount\":{Session.Avatar.Count}," +
                              $"\"routeValid\":{routeValid.ToString().ToLowerInvariant()}," +
                              $"\"routeCorners\":{(route?.Length ?? 0)},\"routeLength\":{routeLength.ToString("R", culture)}," +
                              $"\"positionX\":{current.x.ToString("R", culture)}," +
                              $"\"positionY\":{current.y.ToString("R", culture)}," +
                              $"\"positionZ\":{current.z.ToString("R", culture)}," +
                              $"\"distanceMoved\":{Vector3.Distance(debugApproachStart, current).ToString("R", culture)}," +
                              $"\"distanceRemaining\":{Vector3.Distance(current, debugApproachTarget).ToString("R", culture)}," +
                              $"\"motorMoving\":{(avatar?.IsMoving ?? false).ToString().ToLowerInvariant()}," +
                              $"\"bodyPaused\":{(avatar?.Body?.IsPaused ?? false).ToString().ToLowerInvariant()}," +
                              $"\"bodyContact\":{(avatar?.Body?.IsContact ?? false).ToString().ToLowerInvariant()}," +
                              $"\"bodyKinematic\":{(rigidbody?.isKinematic ?? false).ToString().ToLowerInvariant()}," +
                              $"\"velocity\":{(rigidbody?.velocity.magnitude ?? -1f).ToString("R", culture)}" + "}}";
                UnityWebRequest.Post("http://127.0.0.1:7783/event", payload, "application/json")
                    .SendWebRequest();
            }
#endif
#endregion
            if (characterAction != null && characterAction.IsFinished &&
                characterAction.State != ActionState.Succeeded)
            {
#region debug-point C-D:approach-failure
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var payload = $"{{\"sessionId\":\"bowling-companion-timeout\",\"runId\":\"post-fix\"," +
                              $"\"hypothesisId\":\"C-D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                              $"\"location\":\"BowlingGame.Update\",\"msg\":\"[DEBUG] approach failed\",\"data\":{{" +
                              $"\"state\":\"{characterAction.State}\",\"stopReason\":\"{characterAction.StopReason}\"," +
                              $"\"elapsed\":{characterAction.ElapsedSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                              $"\"error\":\"{(characterAction.Error ?? "none").Replace("\"", "'")}\"" + "}}";
                UnityWebRequest.Post("http://127.0.0.1:7783/event", payload, "application/json")
                    .SendWebRequest();
#endif
#endregion
                Fail("冷月暂时无法完成动作：" + characterAction.Error);
                return;
            }
            if (Phase == BowlingPhase.Approaching && characterAction.IsFinished)
            {
                SetPhase(BowlingPhase.PlayerReady);
                ball.AllowPlayer(true);
                Notifications.ShowHint("你先滚动保龄球，下一次轮到冷月。\n击倒的球瓶越多，分数越高。", 0);
                if (invitation != null) speaker.PlayOneShot(invitation);
            }
            else if (Phase == BowlingPhase.PlayerRolling || Phase == BowlingPhase.CompanionRolling)
            {
                quiet = ball.Body.IsSettled ? quiet + dt : 0;
                if (firstHit >= 0 ? elapsed - firstHit >= ScoreDelay :
                    elapsed >= 12 || elapsed >= 1.5f && quiet >= .8f) Score();
            }
            else if (Phase == BowlingPhase.PlayerResult)
            {
                if (!TryReturnBall() || !TryResetPins()) return;
                if (elapsed >= 2) StartCompanion();
            }
            else if (Phase == BowlingPhase.CompanionResult)
            {
                if (!TryReturnBall()) return;
                if (elapsed >= 1)
                {
                    SetPhase(BowlingPhase.Winner);
                    var winner = PlayerScore == CompanionScore ? "平局！" :
                        PlayerScore > CompanionScore ? "你获胜了！" : "冷月获胜了！";
                    Notifications.ShowResult($"{winner}\n你 {PlayerScore} : {CompanionScore} 冷月", 0);
                    Notifications.ShowHint("本局结束。打开「互动 → 小游戏」可再来一局或结束游戏。", 0);
                    speaker.PlayOneShot(cheer);
                }
            }
        }

        private void FixedUpdate() => contacts?.Sync();

        private void UpdatePlacement()
        {
            Pose hand = default;
            Ray aim = default;
            bool pressed = false;
            var tracked = Host.Input != null && Host.Input.TryPlacementInput(out hand, out aim, out pressed);
            if (!tracked)
            {
                if (Application.isEditor && Camera.main != null && Mouse.current != null)
                {
                    var camera = Camera.main;
                    aim = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    hand = new Pose(camera.transform.position + camera.transform.forward * .5f -
                        camera.transform.up * .2f + camera.transform.right * .2f, camera.transform.rotation);
                    pressed = Mouse.current.leftButton.isPressed;
                    tracked = true;
                }
            }
            heldPreview.SetActive(tracked);
            if (!tracked)
            {
                releasedInput = pressedInput = false;
                foreach (var marker in markers) marker.gameObject.SetActive(false);
                return;
            }
            heldPreview.transform.SetPositionAndRotation(hand.position + hand.rotation * Vector3.forward * .16f,
                hand.rotation);
            var valid = ResolveFloor(aim, out var floor);
            if (valid)
            {
                var forward = Vector3.ProjectOnPlane(
                    Host.Input != null && Host.Input.DirectHandsEnabled
                        ? Camera.main.transform.forward : floor - hand.position, Vector3.up).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                candidate = new Pose(floor, Quaternion.LookRotation(forward));
                valid = CanPlace(candidate, out _);
            }
            foreach (var marker in markers) marker.gameObject.SetActive(valid);
            if (valid)
                for (var i = 0; i < markers.Length; i++)
                    markers[i].position = floor + candidate.rotation * PinOffset(i) + Vector3.up * .005f;
            // Opening the menu can leave the hand closed. Require a fresh gesture.
            if (!pressed) releasedInput = true;
            var controller = Host.Input == null || Host.Input.CurrentMode == PlayerInteractionMode.Controller;
            var confirm = releasedInput && (controller ? pressed && !pressedInput : !pressed && pressedInput);
            pressedInput = pressed;
            if (!confirm) return;
            if (!valid) { Notifications.ShowHint("该位置无法放置，请选择平整、空旷且有足够距离的地面。"); return; }
            TryConfirmPlacement(candidate);
        }

        private bool ResolveFloor(Ray ray, out Vector3 floor)
        {
            floor = default;
            var distance = Host.Input != null && Host.Input.DirectHandsEnabled ? 2f : 8f;
            var count = Physics.RaycastNonAlloc(ray, hits, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            var nearest = float.PositiveInfinity;
            var found = false;
            for (var i = 0; i < count; i++)
                if (hits[i].distance < nearest)
                {
                    nearest = hits[i].distance;
                    floor = hits[i].point;
                    found = hits[i].normal.y >= .97f &&
                            Mathf.Abs(floor.y - Session.Navigation.FloorHeight) < .06f &&
                            Array.IndexOf(roomShapes, hits[i].collider) >= 0;
                }
            return found;
        }

        private bool Free(Vector3 point, float radius, float height)
        {
            var count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * (radius + .015f),
                point + Vector3.up * Mathf.Max(radius + .015f, height - radius), radius,
                overlaps, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            return count == 0;
        }

        private bool CanPlace(Pose pose, out string error)
        {
            error = "请选择已扫描、平整且空旷的地面。";
            if (Mathf.Abs(pose.position.y - Session.Navigation.FloorHeight) > .06f) return false;
            var count = Phase == BowlingPhase.PlacingPins ? 10 : 1;
            for (var i = 0; i < count; i++)
            {
                var point = pose.position + pose.rotation * PinOffset(i);
                if (!Session.Navigation.TrySample(point, .08f, out var sample) ||
                    Vector3.Distance(point, sample) > .08f ||
                    !Free(point, count == 10 ? .066f : BallRadius, count == 10 ? .38f : .22f)) return false;
            }
            if (count == 10) { error = null; return true; }
            var direction = Vector3.ProjectOnPlane(rackPose.position - pose.position, Vector3.up);
            var length = direction.magnitude;
            error = "出发点需在球瓶前方 1.5～6 米，球道内请留出空位。";
            if (length < 1.5f || length > 6 || Vector3.Dot(direction.normalized, rackPose.rotation * Vector3.forward) < .8f)
                return false;
            var number = Physics.SphereCastNonAlloc(pose.position + Vector3.up * (BallRadius + .03f),
                BallRadius, direction.normalized, hits, length - .22f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            if (number != 0) return false;
            for (var d = 0f; d < length; d += .25f)
                if (!Session.Navigation.TrySample(pose.position + direction.normalized * d, .1f, out _)) return false;
            error = "冷月无法走到出发点旁，请为她留出站立和通行空间。";
            if (!TryResolveCompanionApproach(pose, out _)) return false;
            error = null;
            return true;
        }

        private bool TryResolveCompanionApproach(Pose pose, out Vector3 join)
        {
            join = default;
            var bestLength = float.PositiveInfinity;
            foreach (var candidate in CompanionApproachCandidates(pose, rackPose.position))
            {
                if (!Session.Navigation.TrySample(candidate, .15f, out var sampled) ||
                    Vector3.Distance(candidate, sampled) > .15f ||
                    !Free(sampled, .22f, 1.5f) ||
                    !Session.Navigation.TryPath(Session.Motor.Position, sampled, out var path) ||
                    !ApproachPathClear(path, pose.position)) continue;
                var length = 0f;
                for (var index = 1; index < path.Length; index++)
                    length += Vector3.Distance(path[index - 1], path[index]);
                if (length >= bestLength) continue;
                bestLength = length;
                join = sampled;
            }
            return !float.IsPositiveInfinity(bestLength);
        }

        private bool ApproachPathClear(Vector3[] path, Vector3 reservedBallPosition)
        {
            const float radius = .22f;
            const float height = 1.5f;
            var avatarBody = (Session.Motor as AvatarMotor)?.Body?.Rigidbody;
            for (var index = 1; index < path.Length; index++)
            {
                var from = path[index - 1];
                var difference = Vector3.ProjectOnPlane(path[index] - from, Vector3.up);
                var distance = difference.magnitude;
                if (distance < .001f) continue;
                if (DistanceToSegmentXZ(reservedBallPosition, from, path[index]) <
                    radius + BallRadius + .03f) return false;
                var count = Physics.CapsuleCastNonAlloc(
                    from + Vector3.up * (radius + .015f),
                    from + Vector3.up * (height - radius),
                    radius, difference / distance, hits, distance,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return false;
                for (var hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    var hit = hits[hitIndex];
                    if (avatarBody != null && hit.rigidbody == avatarBody || hit.normal.y > .7f) continue;
                    if (hit.distance < distance - .02f) return false;
                }
            }
            return true;
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
        {
            point.y = start.y = end.y = 0;
            var segment = end - start;
            if (segment.sqrMagnitude < .000001f) return Vector3.Distance(point, start);
            var t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude);
            return Vector3.Distance(point, start + segment * t);
        }

        // Same validation boundary serves XR, mouse and the integration harness.
        public bool TryConfirmPlacement(Pose pose)
        {
            if (Phase != BowlingPhase.PlacingPins && Phase != BowlingPhase.PlacingSpawn) return false;
            PhysicsBody.ValidatePose(pose);
            Physics.SyncTransforms();
            if (!CanPlace(pose, out var error)) { Notifications.ShowHint(error); return false; }
            var join = default(Vector3);
            if (Phase == BowlingPhase.PlacingSpawn && !TryResolveCompanionApproach(pose, out join))
            {
                Notifications.ShowHint("冷月无法走到出发点旁，请为她留出站立和通行空间。");
                return false;
            }
            if (Phase == BowlingPhase.PlacingPins)
            {
                rackPose = pose;
                for (var i = 0; i < pins.Length; i++)
                {
                    pins[i] = Instantiate(pinPrefab, transform);
                    pins[i].Rack(new Pose(pose.position + pose.rotation * PinOffset(i), pose.rotation));
                }
                contacts = new BowlingPinContacts(pins, null, roomShapes);
                StartPlacement(BowlingPhase.PlacingSpawn);
                return true;
            }
            spawnPose = new Pose(pose.position + Vector3.up * (BallRadius + .008f), Quaternion.identity);
            ball = Instantiate(ballPrefab, transform);
            ball.Body.Place(spawnPose);
            ball.AllowPlayer(false);
            ball.Released += OnPlayerReleased;
            ball.PinHit += OnPinHit;
            contacts.Dispose();
            contacts = new BowlingPinContacts(pins, ball, roomShapes);
            Destroy(heldPreview);
            foreach (var marker in markers) Destroy(marker.gameObject);
            markers = null;
            if (Host.Input != null) Host.Input.PlacementActive = false;
            var direction = Vector3.ProjectOnPlane(rackPose.position - pose.position, Vector3.up).normalized;
#region debug-point A-B:approach-submit
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            debugApproachStart = Session.Motor.Position;
            debugApproachTarget = join;
            debugNextApproachReport = 0;
            var validatedStand = join;
            var routeValid = Session.Navigation.TryPath(debugApproachStart, join, out var route);
            var routeLength = 0f;
            if (route != null)
                for (var index = 1; index < route.Length; index++)
                    routeLength += Vector3.Distance(route[index - 1], route[index]);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var payload = $"{{\"sessionId\":\"bowling-companion-timeout\",\"runId\":\"post-fix\"," +
                          $"\"hypothesisId\":\"A-B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                          $"\"location\":\"BowlingGame.TryConfirmPlacement\",\"msg\":\"[DEBUG] approach submitted\",\"data\":{{" +
                          $"\"startX\":{debugApproachStart.x.ToString("R", culture)}," +
                          $"\"startY\":{debugApproachStart.y.ToString("R", culture)}," +
                          $"\"startZ\":{debugApproachStart.z.ToString("R", culture)}," +
                          $"\"joinX\":{join.x.ToString("R", culture)},\"joinY\":{join.y.ToString("R", culture)}," +
                          $"\"joinZ\":{join.z.ToString("R", culture)}," +
                          $"\"validatedStandX\":{validatedStand.x.ToString("R", culture)}," +
                          $"\"validatedStandY\":{validatedStand.y.ToString("R", culture)}," +
                          $"\"validatedStandZ\":{validatedStand.z.ToString("R", culture)}," +
                          $"\"targetDelta\":{Vector3.Distance(validatedStand, join).ToString("R", culture)}," +
                          $"\"routeValid\":{routeValid.ToString().ToLowerInvariant()}," +
                          $"\"routeCorners\":{(route?.Length ?? 0)},\"routeLength\":{routeLength.ToString("R", culture)}" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7783/event", payload, "application/json")
                .SendWebRequest();
#endif
#endregion
            characterAction = Session.Submit(new AvatarIntent("move", IntentSource.System, payload: join));
            SetPhase(BowlingPhase.Approaching);
            Notifications.ShowHint("冷月正在走过来，准备一起玩保龄球。", 0);
            return true;
        }

        private void OnPlayerReleased()
        {
            if (Phase != BowlingPhase.PlayerReady) return;
            ball.AllowPlayer(false);
            Launch(BowlingPhase.PlayerRolling);
        }

        private void Launch(BowlingPhase phase)
        {
            firstHit = -1; quiet = 0;
            contacts.BallReleased = true;
            SetPhase(phase);
        }

        private void OnPinHit()
        {
            if ((Phase == BowlingPhase.PlayerRolling || Phase == BowlingPhase.CompanionRolling) && firstHit < 0)
            {
                firstHit = elapsed;
                Debug.Log($"BOWLING_FIRST_HIT turn={Phase} t={elapsed:F3}");
            }
        }

        private void Score()
        {
            var score = pins.Count(pin => pin.IsDown);
            var player = Phase == BowlingPhase.PlayerRolling;
            if (player) PlayerScore = score; else CompanionScore = score;
            Debug.Log($"BOWLING_SCORE player={player} score={score} hit={firstHit:F3} elapsed={elapsed:F3}");
            contacts.BallReleased = false;
            ball.Body.SetContact(true);
            foreach (var pin in pins) pin.Body.SetContact(true);
            returning = true;
            resetBlocked = false;
            SetPhase(player ? BowlingPhase.PlayerResult : BowlingPhase.CompanionResult);
            Notifications.ShowResult(player ? $"你获得了{score}分" : $"冷月获得了{score}分", player ? 2 : 1);
        }

        private bool TryReturnBall()
        {
            if (!returning) return true;
            if (!ball.Body.CanOccupy(spawnPose, ignoredRoot: transform))
            {
                if (!resetBlocked) Notifications.ShowHint("请移开保龄球刷新点上的物品，回合将继续。", 0);
                resetBlocked = true;
                elapsed = 0;
                return false;
            }
            ball.Body.Place(spawnPose);
            ball.Body.SetContact(false);
            ball.Body.Place(spawnPose);
            returning = false;
            return true;
        }

        private bool TryResetPins()
        {
            if (pins.All(pin => !pin.Body.IsContact || pin.IsPlacementLocked)) return true;
            foreach (var pin in pins)
                if (!pin.Body.CanOccupy(pin.StandingPose, ignoredRoot: transform))
                {
                    Notifications.ShowHint("请移开球瓶原位置上的物品，以便重置球瓶。", 0);
                    elapsed = 0;
                    return false;
                }
            foreach (var pin in pins) pin.Rack(pin.StandingPose);
            return true;
        }

        private void StartCompanion()
        {
            Session.Conversation.Cancel();
            Notifications.ShowHint("现在轮到冷月击球，请等待。", 0);
            SetPhase(BowlingPhase.CompanionPlaying);
            var motor = (AvatarMotor)Session.Motor;
            var action = new BowlingTurnAction(motor, Session.Navigation,
                motor.GetComponent<AvatarBowlingRig>(), ball,
                spawnPose.position - Vector3.up * (BallRadius + .008f), rackPose.position,
                () => Launch(BowlingPhase.CompanionRolling), () => Phase == BowlingPhase.Winner);
            characterAction = Session.Submit(new AvatarIntent("bowling-turn", IntentSource.System, payload: action));
        }

        private void SetPhase(BowlingPhase phase) { Phase = phase; elapsed = 0; }
        private void Fail(string error)
        {
            var messages = Notifications;
            Host.Stop();
            messages.ShowHint(error);
        }

        public override void End()
        {
            if (!initialized) return;
            initialized = false;
            contacts?.Dispose();
            Session.Avatar.Cancel(characterAction);
            Session.Autonomy.Enabled = autonomyBefore;
            if (Host.Input != null)
            {
                Host.Input.PlacementActive = false;
                Host.Input.ModeChanged -= OnModeChanged;
            }
            if (ball != null) { ball.AllowPlayer(false); ball.Released -= OnPlayerReleased; ball.PinHit -= OnPinHit; }
            if (speaker != null) speaker.Stop();
            if (markerMaterial != null) Destroy(markerMaterial);
        }
    }
}
