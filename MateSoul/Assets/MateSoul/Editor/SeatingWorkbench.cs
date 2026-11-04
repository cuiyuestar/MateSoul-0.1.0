using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    // Explicit file requests let local automation use the already licensed editor.
    [InitializeOnLoad]
    public static class SeatingWorkbench
    {
        public static string Output => Path.GetFullPath(Application.dataPath + "/../../.artifacts/seating");
        private static double nextPoll;

        static SeatingWorkbench() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            var path = Output + "/editor-request";
            if (!File.Exists(path)) return;
            var command = File.ReadAllText(path).Trim();
            File.Delete(path);
            try
            {
                if (command == "analyze") Analyze();
                else if (command == "preview") Preview();
                else if (command == "build") BuildAndroid();
                else if (command == "game-build") GameSceneBuilder.BuildAndroid();
                else if (command == "tests") EditorApplication.ExecuteMenuItem("MateSoul/Game/Run Framework Tests");
                else if (command == "seating-tests") EditorApplication.ExecuteMenuItem("MateSoul/Game/Run Seating Tests");
                else throw new ArgumentException("Unknown seating workbench request: " + command);
                File.WriteAllText(Output + "/last-result.txt", command + " completed");
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/last-result.txt", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("MateSoul/Seating/Build Android")]
        public static void BuildAndroid()
        {
            GameSceneBuilder.BuildScene();
            var path = Environment.GetEnvironmentVariable("MATESOUL_SEATING_APK") ??
                Path.GetFullPath(Application.dataPath + "/../../Builds/MateSoul_RealBed_Development.apk");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameSceneBuilder.ScenePath }, locationPathName = path,
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Seating Android build failed: " + report.summary.result);
        }

        [Serializable] private sealed class Bone
        {
            public string name, parent;
            public Vector3 position, localPosition, scale;
            public Quaternion rotation;
        }
        [Serializable] private sealed class Rig
        {
            public bool human;
            public Bone[] bones;
        }

        [MenuItem("MateSoul/Seating/Analyze Character")]
        public static void Analyze()
        {
            Directory.CreateDirectory(Output);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MateSoul/Content/Lengyue_Runtime.prefab");
            var root = UnityEngine.Object.Instantiate(source);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentInChildren<Animator>();
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Lengyue_Idle_BodyOnly.anim");
                idle.SampleAnimation(animator.gameObject, 0);
                var rig = new Rig
                {
                    human = animator.isHuman,
                    bones = root.GetComponentsInChildren<Transform>(true).Select(t => new Bone
                    {
                        name = t.name, parent = t.parent == null ? "" : t.parent.name,
                        position = t.position, localPosition = t.localPosition,
                        rotation = t.rotation, scale = t.lossyScale
                    }).ToArray()
                };
                File.WriteAllText(Output + "/character-rig.json", JsonUtility.ToJson(rig, true));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Preview()
        {
            GameSceneBuilder.BuildScene();
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath));
            var cameraObject = new GameObject("Seating Preview Camera");
            var seatObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var backObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var lampObject = new GameObject("Seating Preview Light");
            var texture = new RenderTexture(1200, 1200, 24);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentInChildren<Animator>();
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Lengyue_Idle_BodyOnly.anim")
                    .SampleAnimation(animator.gameObject, 0);
                var rig = root.GetComponent<MateSoul.Runtime.AvatarSeatingRig>();
                var seat = new MateSoul.Runtime.SeatTarget
                { Center = new Vector3(0, .45f, 0), Forward = Vector3.forward, Width = .48f, Depth = .44f };
                rig.SetPose(seat, 1);
                rig.SendMessage("LateUpdate");
                foreach (var guard in root.GetComponentsInChildren<MateSoul.Cloth.SkirtPoseGuard>())
                    guard.Evaluate(1f / 60);
                seatObject.transform.position = new Vector3(0, .42f, 0);
                seatObject.transform.localScale = new Vector3(.48f, .06f, .44f);
                backObject.transform.position = new Vector3(0, .68f, -.24f);
                backObject.transform.localScale = new Vector3(.48f, .52f, .04f);
                foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
                seatObject.layer = backObject.layer = 31;
                var light = lampObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f;
                light.cullingMask = 1 << 31;
                lampObject.transform.rotation = Quaternion.Euler(35, -40, 0);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .15f, .19f);
                camera.targetTexture = texture;
                camera.cullingMask = 1 << 31;
                camera.fieldOfView = 35;
                cameraObject.transform.position = new Vector3(2f, 1.3f, 2.1f);
                cameraObject.transform.LookAt(new Vector3(0, .65f, .1f));
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "/pose-preview.png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.active = null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(seatObject); UnityEngine.Object.DestroyImmediate(backObject);
                UnityEngine.Object.DestroyImmediate(lampObject); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
