#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.Core;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MateSoul.Runtime
{
    // Local development build only. No network endpoint or release-build command path.
    public sealed class SeatingDeviceProbe : MonoBehaviour
    {
        private GameBootstrap game;
        private GameObject character;
        private ActionHandle handle;
        private float nextPoll;
        private static bool meshWritten;
        private GameObject fixture;
        private Material fixtureMaterial;
        private List<RealityTriangle> fixtureGeometry;
        private string commandError;
        private readonly List<GameObject> hiddenItems = new List<GameObject>();
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        private readonly List<XRRayInteractor> hiddenRays = new List<XRRayInteractor>();
        private TrackedPoseDriver cameraTracking;
        private string DirectoryPath => Path.Combine(Application.persistentDataPath, "seating-validation");
        public void Configure(GameBootstrap bootstrap, GameObject avatar) { game = bootstrap; character = avatar; }

        [Serializable] private sealed class MeshBlock
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3[] vertices;
            public ushort[] indices;
            public int[] labels;
        }
        [Serializable] private sealed class MeshDump { public MeshBlock[] blocks; }
        [Serializable] private sealed class RealityState
        {
            public string id, semantic, definition;
            public string[] declaredInteractions, availableInteractions;
            public bool capturedBySdk, geometryInferred;
            public Vector3 center, size;
        }
        [Serializable] private sealed class State
        {
            public string source, scanner, bedScanner, action, actionPhase, posture;
            public string error, commandError;
            public Vector3 root, pelvis, lyingPelvis, leftFoot, rightFoot, camera;
            public Quaternion rotation, cameraRotation;
            public float weight, lyingWeight, pelvisOffset;
            public SeatTarget[] seats;
            public BedTarget[] beds;
            public RealityState[] realityItems;
            public int triangleCount;
            public bool capsuleEnabled;
            public float time;
            public bool fullBodyOwned;
        }

        // Explicit development command only; never replaces SDK input automatically.
        private void CreateFixture(float x, float z, float yaw)
        {
            ClearFixture();
            game.Session.Autonomy.Enabled = false;
            game.Session.Submit(new AvatarIntent("stay", IntentSource.Player));
            HideItems();
            var floor = game.Room.Snapshot.MainFloor.Centroid.y;
            fixture = new GameObject("SYNTHETIC STANDARD CHAIR - development validation");
            fixture.transform.SetPositionAndRotation(new Vector3(x, floor, z), Quaternion.Euler(0, yaw, 0));
            fixtureMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fixtureMaterial.SetColor("_BaseColor", new Color(.22f, .42f, .46f));
            fixtureMaterial.SetFloat("_Smoothness", .25f);
            fixtureGeometry = new List<RealityTriangle>();
            void Box(string name, Vector3 center, Vector3 size)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = name;
                part.transform.SetParent(fixture.transform, false);
                part.transform.localPosition = center;
                part.transform.localScale = size;
                part.GetComponent<Renderer>().sharedMaterial = fixtureMaterial;
                var mesh = part.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices.Select(part.transform.TransformPoint).ToArray();
                var indices = mesh.triangles;
                for (var i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                    // Simulate observed top/side surfaces, not an X-ray scan of the underside.
                    fixtureGeometry.Add(new RealityTriangle(a, b, c, Vector3.Cross(b - a, c - a).normalized.y > -.985f));
                }
            }
            Box("seat 480 x 440 mm; height 450 mm", new Vector3(0, .42f, 0), new Vector3(.48f, .06f, .44f));
            Box("backrest", new Vector3(0, .69f, -.24f), new Vector3(.48f, .48f, .04f));
            foreach (var side in new[] { -1, 1 })
                foreach (var end in new[] { -1, 1 })
                    Box("leg", new Vector3(side * .205f, .195f, end * .185f), new Vector3(.045f, .39f, .045f));
            game.Session.Chairs.UseDevelopmentGeometry = true;
            game.Session.Chairs.Invalidate();
            Debug.Log("MATESOUL_SEAT fixture=synthetic_standard_chair; SDK recognition NOT under test");
        }

        private void ClearFixture()
        {
            if (fixture != null) Destroy(fixture);
            if (fixtureMaterial != null) Destroy(fixtureMaterial);
            fixtureGeometry = null;
            ShowItems();
            if (game?.Session?.Chairs != null)
            {
                game.Session.Chairs.UseDevelopmentGeometry = false;
                game.Session.Chairs.Invalidate();
            }
        }

        private void HideItems()
        {
            foreach (var item in game.Session.ItemViews)
                if (item.gameObject.activeSelf)
                { hiddenItems.Add(item.gameObject); item.gameObject.SetActive(false); }
        }

        private void ShowItems()
        {
            foreach (var item in hiddenItems) if (item != null) item.SetActive(true);
            hiddenItems.Clear();
        }

        public static void WriteMeshOnce(List<PxrSpatialMeshInfo> blocks)
        {
            if (meshWritten || blocks.Count == 0) return;
            meshWritten = true;
            var directory = Path.Combine(Application.persistentDataPath, "seating-validation");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "mesh.json"),
                JsonUtility.ToJson(new MeshDump
                {
                    blocks = blocks.Select(b => new MeshBlock
                    {
                        position = b.position, rotation = b.rotation, vertices = b.vertices,
                        indices = b.indices, labels = b.labels?.Select(l => (int)l).ToArray()
                    }).ToArray()
                }));
        }

        private void Update()
        {
            if (game?.Session == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .5f;
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, "command");
            if (File.Exists(path))
            {
                var command = File.ReadAllText(path).Trim();
                File.Delete(path);
                try
                {
                    commandError = null;
                    if (command == "sit")
                    {
                        game.Session.Autonomy.Enabled = false;
                        var seat = game.Session.Chairs.Nearest(character.transform.position);
                        if (seat == null) throw new InvalidOperationException(game.Session.Chairs.Status);
                        handle = game.Session.Submit(new AvatarIntent(
                            RealityInteractionIds.Sit, IntentSource.UI, seat.Id));
                    }
                    else if (command == "lie")
                    {
                        game.Session.Autonomy.Enabled = false;
                        var bed = game.Session.Beds.Nearest(character.transform.position);
                        if (bed == null) throw new InvalidOperationException(game.Session.Beds.Status);
                        handle = game.Session.Submit(new AvatarIntent(
                            RealityInteractionIds.Lie, IntentSource.UI, bed.Id));
                    }
#region debug-point F:cut-fruit-command
                    else if (command == "cut-fruit")
                    {
                        game.Session.Autonomy.Enabled = false;
                        handle = game.Session.Submit(new AvatarIntent(
                            CutFruitAction.Id, IntentSource.UI));
                    }
#endregion
                    else if (command == "stand")
                        handle = game.Session.Submit(new AvatarIntent("stay", IntentSource.Player));
                    else if (command == "stop")
                        game.Session.Autonomy.Enabled = false;
                    else if (command.StartsWith("move "))
                    {
                        var values = command.Split(' ');
                        var floor = game.Room.Snapshot.MainFloor.Centroid.y;
                        handle = game.Session.Submit(new AvatarIntent("move", IntentSource.Player,
                            payload: new Vector3(float.Parse(values[1], CultureInfo.InvariantCulture),
                                floor, float.Parse(values[2], CultureInfo.InvariantCulture))));
                    }
                    else if (command.StartsWith("camera "))
                    {
                        var values = command.Split(' ');
                        var camera = Camera.main;
                        cameraTracking = camera.GetComponent<TrackedPoseDriver>();
                        if (cameraTracking != null) cameraTracking.enabled = false;
                        camera.transform.SetPositionAndRotation(
                            new Vector3(float.Parse(values[1], CultureInfo.InvariantCulture),
                                float.Parse(values[2], CultureInfo.InvariantCulture),
                                float.Parse(values[3], CultureInfo.InvariantCulture)),
                            Quaternion.Euler(float.Parse(values[4], CultureInfo.InvariantCulture),
                                float.Parse(values[5], CultureInfo.InvariantCulture), 0));
                    }
                    else if (command == "camera-live")
                    {
                        if (cameraTracking != null) cameraTracking.enabled = true;
                        cameraTracking = null;
                    }
                    else if (command.StartsWith("fixture "))
                    {
                        var values = command.Split(' ');
                        CreateFixture(float.Parse(values[1], CultureInfo.InvariantCulture),
                            float.Parse(values[2], CultureInfo.InvariantCulture),
                            float.Parse(values[3], CultureInfo.InvariantCulture));
                    }
                    else if (command == "lose-target")
                    {
                        fixtureGeometry = null;
                        game.Session.Chairs.Invalidate();
                    }
                    else if (command == "lose-bed") game.Session.Beds.Invalidate();
                    else if (command == "fixture-off") ClearFixture();
                    else if (command == "hide-items") HideItems();
                    else if (command == "show-items") ShowItems();
                    else if (command == "hide-hud")
                    {
                        foreach (var canvas in FindObjectsOfType<Canvas>())
                            if (canvas.enabled) { hiddenCanvases.Add(canvas); canvas.enabled = false; }
                        foreach (var ray in FindObjectsOfType<XRRayInteractor>())
                            if (ray.enabled) { hiddenRays.Add(ray); ray.enabled = false; }
                    }
                    else if (command == "show-hud")
                    {
                        foreach (var canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
                        hiddenCanvases.Clear();
                        foreach (var ray in hiddenRays) if (ray != null) ray.enabled = true;
                        hiddenRays.Clear();
                    }
                }
                catch (Exception error) { commandError = error.Message; Debug.LogException(error); }
            }
            if (fixtureGeometry != null) game.Session.Chairs.Observe(fixtureGeometry, Time.unscaledTime);
            var rig = character.GetComponent<AvatarSeatingRig>();
            var lyingRig = character.GetComponent<AvatarLyingRig>();
            var activeAction = game.Session.Avatar.Capture()
                .LastOrDefault(snapshot => snapshot.action_id == RealityInteractionIds.Lie);
            var state = new State
            {
                source = fixture != null ? "SYNTHETIC STANDARD CHAIR / PICO Emulator" : game.Room.Snapshot.Source,
                scanner = game.Session.Chairs.Status,
                bedScanner = game.Session.Beds.Status,
                action = handle?.State.ToString(), error = handle?.Error,
                actionPhase = activeAction?.phase, posture = activeAction?.posture,
                commandError = commandError, time = Time.unscaledTime,
                fullBodyOwned = game.Session.Avatar.Leases.IsOccupied(ControlChannels.FullBody),
                root = character.transform.position, rotation = character.transform.rotation,
                pelvis = rig.PelvisPosition, leftFoot = rig.LeftFootPosition, rightFoot = rig.RightFootPosition,
                lyingPelvis = lyingRig.PelvisPosition,
                weight = rig.Weight, lyingWeight = lyingRig.Weight, pelvisOffset = rig.ContactOffset,
                camera = Camera.main.transform.position, cameraRotation = Camera.main.transform.rotation,
                seats = game.Session.Chairs.Targets.ToArray(),
                beds = game.Session.Beds.Targets.ToArray(),
                realityItems = game.Session.RealityItems.Items.OrderBy(item => item.Id).Select(item => new RealityState
                {
                    id = item.Id,
                    semantic = item.Semantic.ToString(),
                    definition = item.Definition.Id,
                    declaredInteractions = item.DeclaredInteractions.Select(value => value.Id).ToArray(),
                    availableInteractions = item.AvailableInteractions.OrderBy(value => value).ToArray(),
                    capturedBySdk = item.CapturedBySdk,
                    geometryInferred = item.GeometryInferred,
                    center = item.Center,
                    size = item.Size
                }).ToArray(),
                triangleCount = game.Session.RealityMesh.Geometry.Count,
                capsuleEnabled = character.GetComponent<CapsuleCollider>().enabled
            };
            File.WriteAllText(Path.Combine(DirectoryPath, "state.json"), JsonUtility.ToJson(state, true));
        }

        private void OnDestroy() { ClearFixture(); }
    }
}
#endif
