using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ByteDance.PICO.XR;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    /// <summary>Deterministic integration checks against the real XRI manager and PhysicsBody.</summary>
    public static class DirectHandValidation
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<string> passed = new List<string>();

        [MenuItem("MateSoul/Validate/Direct Hands")]
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this check in a separate Unity batch process.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            typeof(GameSceneBuilder).GetMethod("ConfigureInteractionCapabilities",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            passed.Clear();
            var manager = new GameObject("Validation Manager").AddComponent<XRInteractionManager>();
            var mode = new GameObject("Validation Mode").AddComponent<PlayerInteractionModeController>();
            mode.SetMode(PlayerInteractionMode.Gesture);
            var left = MakeHand("left", manager, mode, out var leftGrab);
            var right = MakeHand("right", manager, mode, out var rightGrab);
            var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = "Near Grab Fixture";
            item.transform.localScale = Vector3.one * .1f;
            item.transform.position = new Vector3(.12f, 1, 0);
            var body = item.AddComponent<PhysicsBody>();
            var material = new PhysicsMaterial("Validation Surface");
            body.Configure(new BodySettings(.2f, material));
            var grab = item.AddComponent<PhysicsGrabInteractable>();
            grab.ConfigureSnap(true); // Direct hands must ignore authored ray snap.
            grab.interactionManager = manager;
            Invoke(grab, "Awake");
            manager.RegisterInteractable((IXRInteractable)grab);
            void Step() { Physics.SyncTransforms(); Invoke(manager, "Update"); }
            void Left(Vector3 position, bool closed, bool tracked = true)
                => SetSample(left, position, closed, tracked);
            var near = new Vector3(.03f, 1, 0);
            var far = new Vector3(-1, 1, 0);

            SetSample(right, far, false, false);
            Left(far, false); Step();
            Left(far, true); Step();
            Check(!grab.isSelected, "远处握拳不拾取");
            Left(near, true); Step();
            Check(!grab.isSelected, "握拳进入范围不吸取");
            Left(near, false); Step();
            Left(near, true); Step();
            Check(grab.isSelected && body.IsFollowing, "近距新握拳进入原有物理跟随");
            Check(Vector3.Distance(body.Pose.position, new Vector3(.12f, 1, 0)) < .0001f,
                "带Snap资产直接握取不瞬移");
            Left(near + Vector3.right * .2f, true); Step();
            Check(grab.isSelected, "持有后移出候选范围不掉落");
            var target = (Pose)typeof(PhysicsBody).GetField("followTarget", Hidden).GetValue(body);
            Check(Vector3.Distance(target.position, new Vector3(.32f, 1, 0)) < .0001f,
                "移动保持握取位置偏移");
            body.Rigidbody.velocity = Vector3.right;
            Left(near, false); Step();
            Check(!grab.isSelected && !body.IsFollowing &&
                  body.Rigidbody.velocity.x > .9f, "正常松手释放并保留物理速度");
            Left(near, true); Step();
            body.Rigidbody.velocity = Vector3.one * 3;
            Left(near, false, false); Step();
            Check(!grab.isSelected && !body.IsFollowing &&
                  body.Rigidbody.velocity.sqrMagnitude < .0001f, "丢失跟踪安全释放且清除抛出速度");
            Left(near, true); Step();
            Check(!grab.isSelected, "跟踪恢复时握拳不自动重新拾取");

            Left(near, false);
            SetSample(right, near, false, true); Step();
            Left(near, true);
            SetSample(right, near, true, true); Step();
            Check(grab.interactorsSelecting.Count == 1 &&
                  (leftGrab.hasSelection ^ rightGrab.hasSelection), "双手争抢只允许一个持有者");
            Left(near, false); SetSample(right, far, false, false); Step();
            body.SetPaused(true);
            Left(near, true); Step();
            Check(!grab.isSelected, "暂停中的物品不可拾取");
            body.SetPaused(false);
            grab.PlayerGrabbable = false;
            Left(near, false); Step(); Left(near, true); Step();
            Check(!grab.isSelected, "固定物品不可拾取");
            grab.PlayerGrabbable = true;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(.055f, 1, 0);
            wall.transform.localScale = new Vector3(.01f, .4f, .4f);
            Left(near, false); Step(); Left(near, true); Step();
            Check(!grab.isSelected, "手与物品之间的实体阻挡拾取");
            Object.DestroyImmediate(wall);
            mode.PlacementActive = true;
            Left(near, false); Step(); Left(near, true); Step();
            Check(!grab.isSelected, "放置模式禁止物品拾取");
            mode.PlacementActive = false; Step();
            Check(!grab.isSelected, "退出放置后必须重新张手握紧");
            Left(near, false); Step();
            SetProperty(left, "TipTracked", true);
            Check(leftGrab.CanPoke, "空手食指允许UI触碰");
            SetProperty(left, "Grip", .6f);
            Check(leftGrab.CanPoke, "食指触碰允许其余手指自然弯曲");
            Left(near, true); Step();
            Check(!leftGrab.CanPoke, "握取时禁止同手UI触碰");
            Left(near, false); Step();

            foreach (var side in new[] { "Left", "Right" })
            {
                var prefab = Resources.Load<GameObject>("Prefabs/Hand" + side);
                Check(prefab != null && prefab.GetComponent<PXR_Hand>() != null &&
                      prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0,
                    "官方" + side + "手骨骼模型可加载");
            }
            var shader = Resources.Load<Shader>("MateSoulHands/TrackedHand");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "虚拟手URP材质编译");
            Check(PXR_ProjectSetting.GetProjectConfig().handTrackingSupportType ==
                  HandTrackingSupport.HandsOnly, "构建配置HandsOnly");
            var previewRoot = new GameObject("Inactive Preview Owner");
            var preview = previewRoot.AddComponent<ItemPlacementPreviewController>();
            typeof(ItemPlacementPreviewController).GetField("input", Hidden).SetValue(preview, mode);
            mode.PlacementActive = true;
            preview.Cancel();
            Check(mode.PlacementActive, "未激活物品预览不清除小游戏放置状态");
            mode.PlacementActive = false;
            Object.DestroyImmediate(previewRoot);
            Directory.CreateDirectory("/tmp/matesoul-direct-hands-20261024");
            File.WriteAllLines("/tmp/matesoul-direct-hands-20261024/validation.txt", passed);
            Debug.Log($"MATESOUL_DIRECT_HAND_VALIDATION_PASS count={passed.Count}");
            Object.DestroyImmediate(item);
            Object.DestroyImmediate(left.gameObject);
            Object.DestroyImmediate(right.gameObject);
            Object.DestroyImmediate(mode.gameObject);
            Object.DestroyImmediate(manager.gameObject);
            Object.DestroyImmediate(material);
        }

        private static PicoTrackedHand MakeHand(string name, XRInteractionManager manager,
            PlayerInteractionModeController mode, out PicoDirectHandInteractor grab)
        {
            var root = new GameObject("Validation " + name);
            var source = root.AddComponent<PicoTrackedHand>();
            var palm = new GameObject("Palm");
            palm.transform.SetParent(root.transform);
            grab = palm.AddComponent<PicoDirectHandInteractor>();
            grab.Configure(source, manager, mode);
            Invoke(grab, "Awake");
            manager.RegisterInteractor((IXRInteractor)grab);
            return source;
        }

        private static void SetSample(PicoTrackedHand hand, Vector3 position, bool closed, bool tracked)
        {
            SetProperty(hand, "Palm", new Pose(position, Quaternion.identity));
            SetProperty(hand, "Tracked", tracked);
            SetProperty(hand, "Closed", closed);
            SetProperty(hand, "Grip", closed ? 1f : 0f);
        }

        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name).SetValue(target, value);
        private static void Invoke(object target, string name)
            => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("DIRECT_HAND_CHECK_FAILED " + name);
            passed.Add(name);
            Debug.Log("DIRECT_HAND_CHECK_PASS " + name);
        }

        public static void Build()
        {
            typeof(GameSceneBuilder).GetMethod("ConfigureInteractionCapabilities",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../Builds/MateSoul_DirectHands_20261024.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Same model connection validation as the normal build, without regenerating the scene.
            typeof(GameSceneBuilder).GetMethod("ValidateModelConnectionForBuild",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
            var previous = PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { GameSceneBuilder.ScenePath },
                    target = BuildTarget.Android, options = BuildOptions.Development,
                    locationPathName = path
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Direct hand APK build failed.");
                Debug.Log($"MATESOUL_DIRECT_HAND_BUILD_PASS path={path} bytes={report.summary.totalSize}");
            }
            finally { PlayerSettings.insecureHttpOption = previous; }
        }

        public static void ValidateAndBuild()
        {
            Run();
            Build();
        }
    }
}
