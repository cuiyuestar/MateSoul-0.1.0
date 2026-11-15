#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MateSoul.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MateSoul.Runtime
{
    // Development-only harness. It prepares the real bowling flow, but never invokes Select or Grab.
    public sealed class BowlingGrabDeviceProbe : MonoBehaviour
    {
        private const string SessionId = "grip-grab-regression";
        private const string RunId = "post-fix-device";
        private const string DebugUrl = "http://127.0.0.1:7790/event";
        private readonly List<GameObject> hiddenItems = new List<GameObject>();
        private GameBootstrap bootstrap;
        private BowlingGame game;
        private BowlingBall ball;
        private XRRayInteractor rightRay;
        private ActionBasedController rightController;
        private Coroutine setup;
        private float nextPoll;
        private string commandError;
        private string setupError;
        private string lastSignature;
        private int releases;
        private bool setupComplete;
        private bool snapObserved;
        private bool followingObserved;
        private bool movedWhileSelected;
        private bool movedFollowObserved;
        private float commandedHandDistance;
        private bool lockRayPose;
        private Vector3 lockedRayPosition;
        private Quaternion lockedRayRotation;

        private string DirectoryPath =>
            Path.Combine(Application.persistentDataPath, "bowling-grab-validation");

        [Serializable]
        private sealed class State
        {
            public string runId;
            public string phase;
            public string inputPhase;
            public string controls;
            public string hit;
            public string setupError;
            public string commandError;
            public bool setupRunning;
            public bool setupComplete;
            public bool hovered;
            public bool selected;
            public bool following;
            public bool selectActive;
            public bool keyboardO;
            public bool snapObserved;
            public bool followingObserved;
            public bool movedWhileSelected;
            public bool movedFollowObserved;
            public int releases;
            public float selectValue;
            public float distance;
            public float commandedHandDistance;
            public float time;
            public Vector3 ballPosition;
            public Vector3 controllerPosition;
        }

        [Serializable]
        private sealed class DebugEvent
        {
            public string sessionId;
            public string runId;
            public string hypothesisId;
            public string location;
            public string msg;
            public State data;
            public long ts;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            var gameBootstrap = FindObjectOfType<GameBootstrap>();
            if (gameBootstrap != null &&
                gameBootstrap.GetComponent<BowlingGrabDeviceProbe>() == null)
                gameBootstrap.gameObject.AddComponent<BowlingGrabDeviceProbe>();
        }

        private void Awake() => bootstrap = GetComponent<GameBootstrap>();

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .1f;
            Directory.CreateDirectory(DirectoryPath);
            ProcessCommand();
            CaptureState();
        }

        private void ProcessCommand()
        {
            var path = Path.Combine(DirectoryPath, "command");
            if (!File.Exists(path)) return;
            try
            {
                var command = File.ReadAllText(path).Trim();
                File.Delete(path);
                commandError = null;
                if (command == "setup")
                {
                    if (setup != null) StopCoroutine(setup);
                    setup = StartCoroutine(Setup());
                }
                else if (command.StartsWith("move-hand ", StringComparison.Ordinal))
                {
                    if (rightRay == null) throw new InvalidOperationException("Right controller ray is not ready.");
                    var values = command.Split(' ');
                    if (values.Length != 4) throw new FormatException("Expected: move-hand <x> <y> <z>");
                    var offset = new Vector3(
                        float.Parse(values[1], CultureInfo.InvariantCulture),
                        float.Parse(values[2], CultureInfo.InvariantCulture),
                        float.Parse(values[3], CultureInfo.InvariantCulture));
                    movedWhileSelected |= ball != null && ball.Grab.isSelected;
                    commandedHandDistance += offset.magnitude;
                    lockedRayPosition += offset;
                    rightRay.transform.position = lockedRayPosition;
                    Physics.SyncTransforms();
                }
                else
                    throw new InvalidOperationException("Unknown command: " + command);
            }
            catch (Exception error)
            {
                commandError = error.Message;
                Debug.LogException(error, this);
            }
        }

        private IEnumerator Setup()
        {
            setupComplete = snapObserved = followingObserved = movedWhileSelected =
                movedFollowObserved = false;
            setupError = commandError = null;
            releases = 0;
            commandedHandDistance = 0;
            BindBall(null);
            game = null;
            rightRay = null;
            rightController = null;
            lockRayPose = false;

            var readyDeadline = Time.realtimeSinceStartup + 60;
            while (bootstrap.Session == null && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            if (bootstrap.Session == null)
            {
                setupError = "GameBootstrap did not become ready: " + bootstrap.Status;
                setup = null;
                yield break;
            }

            bootstrap.Session.Autonomy.Enabled = false;
            bootstrap.Session.Submit(new AvatarIntent("stay", IntentSource.Player));
            HideItems();
            bootstrap.MiniGames.Stop();
            yield return null;
            yield return null;

            var input = bootstrap.MiniGames.Input;
            if (input == null)
            {
                setupError = "PlayerInteractionModeController is missing.";
                setup = null;
                yield break;
            }
            input.SetMode(PlayerInteractionMode.Controller);

            var floor = bootstrap.Room.Snapshot.MainFloor;
            var center = floor.Centroid;
            center.y = bootstrap.Session.Navigation.FloorHeight;
            var cameraForward = Camera.main == null
                ? Vector3.forward
                : Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            if (cameraForward.sqrMagnitude < .1f) cameraForward = Vector3.forward;
            var cameraRight = Vector3.Cross(Vector3.up, cameraForward);
            var directions = Enumerable.Range(0, 8)
                .Select(index => Quaternion.AngleAxis(index * 45, Vector3.up) * cameraForward)
                .ToArray();
            var offsets = new[]
            {
                Vector3.zero,
                cameraRight * .65f, -cameraRight * .65f,
                cameraForward * .65f, -cameraForward * .65f,
                (cameraForward + cameraRight).normalized * 1.1f,
                (cameraForward - cameraRight).normalized * 1.1f,
                (-cameraForward + cameraRight).normalized * 1.1f,
                (-cameraForward - cameraRight).normalized * 1.1f
            };

            var placed = false;
            foreach (var offset in offsets)
            {
                foreach (var direction in directions)
                {
                    if (bootstrap.MiniGames.Active == null)
                    {
                        if (!bootstrap.MiniGames.TryStart("bowling"))
                        {
                            yield return null;
                            continue;
                        }
                        yield return null;
                    }
                    game = bootstrap.MiniGames.Active as BowlingGame;
                    if (game == null)
                    {
                        setupError = "The bowling catalog did not create BowlingGame.";
                        setup = null;
                        yield break;
                    }

                    var rackPosition = center + offset + direction * .8f;
                    var rackPose = new Pose(rackPosition, Quaternion.LookRotation(direction, Vector3.up));
                    if (!game.TryConfirmPlacement(rackPose)) continue;

                    var right = Vector3.Cross(Vector3.up, direction);
                    foreach (var distance in new[] { 2f, 2.4f, 1.7f, 2.8f })
                    {
                        foreach (var lateral in new[] { 0f, .35f, -.35f })
                        {
                            var spawn = rackPosition - direction * distance + right * lateral;
                            if (!game.TryConfirmPlacement(new Pose(spawn, Quaternion.identity))) continue;
                            placed = true;
                            break;
                        }
                        if (placed) break;
                    }
                    if (placed) break;
                    bootstrap.MiniGames.Stop();
                    game = null;
                    yield return null;
                    yield return null;
                }
                if (placed) break;
            }

            if (!placed || game == null)
            {
                setupError = "No validated rack/spawn pair was found on the captured room floor.";
                setup = null;
                yield break;
            }

            var approachDeadline = Time.realtimeSinceStartup + 100;
            while (bootstrap.MiniGames.Active == game &&
                   game.Phase != BowlingPhase.PlayerReady &&
                   Time.realtimeSinceStartup < approachDeadline)
                yield return null;
            if (bootstrap.MiniGames.Active != game || game.Phase != BowlingPhase.PlayerReady)
            {
                setupError = bootstrap.MiniGames.Active == null
                    ? "Bowling stopped before PlayerReady."
                    : "Timed out waiting for PlayerReady; phase=" + game.Phase;
                setup = null;
                yield break;
            }

            BindBall(game.Ball);
            if (ball == null)
            {
                setupError = "PlayerReady has no bowling ball.";
                setup = null;
                yield break;
            }
            var rayObject = GameObject.Find("RightHand UI Ray");
            rightRay = rayObject == null ? null : rayObject.GetComponent<XRRayInteractor>();
            rightController = rayObject == null ? null : rayObject.GetComponent<ActionBasedController>();
            if (rightRay == null || rightController == null)
            {
                setupError = "RightHand UI Ray is missing.";
                setup = null;
                yield break;
            }

            rightController.enableInputTracking = false;
            rightController.currentControllerState.inputTrackingState = InputTrackingState.None;
            rightRay.transform.SetParent(null, true);
            yield return null;
            var lane = Vector3.ProjectOnPlane(game.Pins[0].transform.position - ball.transform.position,
                Vector3.up).normalized;
            if (lane.sqrMagnitude < .1f) lane = cameraForward;
            var controllerPosition = ball.Body.Pose.position - lane * 1f + Vector3.up * .18f;
            lockedRayPosition = controllerPosition;
            lockedRayRotation = Quaternion.LookRotation(
                ball.Body.Pose.position - controllerPosition, Vector3.up);
            lockRayPose = true;
            ApplyLockedRayPose();
            Physics.SyncTransforms();

            var hoverDeadline = Time.realtimeSinceStartup + 5;
            while (!rightRay.interactablesHovered.Contains(ball.Grab) &&
                   Time.realtimeSinceStartup < hoverDeadline)
                yield return null;
            if (!rightRay.interactablesHovered.Contains(ball.Grab))
                setupError = "Production right ray did not hover the bowling ball.";
            else
                setupComplete = true;
            setup = null;
        }

        private void LateUpdate()
        {
            if (lockRayPose) ApplyLockedRayPose();
        }

        private void ApplyLockedRayPose()
        {
            if (rightController != null) rightController.enableInputTracking = false;
            if (rightRay != null)
                rightRay.transform.SetPositionAndRotation(lockedRayPosition, lockedRayRotation);
        }

        private void HideItems()
        {
            foreach (var item in FindObjectsOfType<GameItem>(true))
            {
                if (!item.gameObject.activeSelf) continue;
                hiddenItems.Add(item.gameObject);
                item.gameObject.SetActive(false);
            }
        }

        private void OnReleased() => releases++;

        private void BindBall(BowlingBall value)
        {
            if (ball == value) return;
            if (ball != null) ball.Released -= OnReleased;
            ball = value;
            if (ball != null) ball.Released += OnReleased;
        }

        private void CaptureState()
        {
            if (game == null && bootstrap?.MiniGames?.Active is BowlingGame active) game = active;
            if (ball == null && game?.Ball != null) BindBall(game.Ball);
            var selected = ball != null && ball.Grab.isSelected;
            var following = ball != null && ball.Body.IsFollowing;
            var hovered = ball != null && rightRay != null &&
                          rightRay.interactablesHovered.Contains(ball.Grab);
            var distance = ball == null || rightRay == null
                ? -1
                : Vector3.Distance(ball.Body.Pose.position, rightRay.transform.position);
            snapObserved |= selected && distance >= 0 && distance < .05f;
            followingObserved |= following;
            movedFollowObserved |= movedWhileSelected && selected && following &&
                                   commandedHandDistance >= .1f && distance >= 0 && distance < .05f;
            var action = rightController?.selectAction.action;
            var state = new State
            {
                runId = RunId,
                phase = game == null ? "none" : game.Phase.ToString(),
                inputPhase = action == null ? "none" : action.phase.ToString(),
                controls = action == null ? "" : string.Join(";", action.controls.Select(control => control.path)),
                setupRunning = setup != null,
                setupComplete = setupComplete,
                hovered = hovered,
                selected = selected,
                following = following,
                selectActive = rightController != null && rightController.selectInteractionState.active,
                keyboardO = Keyboard.current != null && Keyboard.current.oKey.isPressed,
                snapObserved = snapObserved,
                followingObserved = followingObserved,
                movedWhileSelected = movedWhileSelected,
                movedFollowObserved = movedFollowObserved,
                releases = releases,
                selectValue = action == null ? 0 : action.ReadValue<float>(),
                distance = distance,
                commandedHandDistance = commandedHandDistance,
                setupError = setupError,
                commandError = commandError,
                time = Time.unscaledTime,
                ballPosition = ball == null ? Vector3.zero : ball.Body.Pose.position,
                controllerPosition = rightRay == null ? Vector3.zero : rightRay.transform.position
            };
            if (rightRay != null && rightRay.TryGetCurrent3DRaycastHit(out var hit))
                state.hit = hit.collider == null ? "none" : hit.collider.name;
            else
                state.hit = "none";
            WriteState(state);

            var signature = $"{state.phase}|{state.setupComplete}|{state.hovered}|{state.selected}|" +
                            $"{state.following}|{state.selectActive}|{state.keyboardO}|{state.releases}|" +
                            $"{state.movedFollowObserved}|{state.setupError}|{state.commandError}";
            if (signature == lastSignature) return;
            lastSignature = signature;
#region debug-point A-D:device-grab-state
            var request = UnityWebRequest.Post(DebugUrl, JsonUtility.ToJson(new DebugEvent
            {
                sessionId = SessionId,
                runId = RunId,
                hypothesisId = "A-B-C-D",
                location = "BowlingGrabDeviceProbe.CaptureState",
                msg = "[DEBUG] device bowling grab state changed",
                data = state,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }), "application/json");
            request.SendWebRequest().completed += _ => request.Dispose();
#endregion
        }

        private void WriteState(State state)
        {
            var path = Path.Combine(DirectoryPath, "state.json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private void OnDestroy()
        {
            BindBall(null);
            foreach (var item in hiddenItems)
                if (item != null) item.SetActive(true);
            hiddenItems.Clear();
        }
    }
}
#endif
