using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace MateSoul.RoomNavigation.Editor
{
    public static class RoomNavigationSceneBuilder
    {
        private const string ScenePath =
            "Assets/MateSoul/RoomNavigation/RoomNavigation_v001.unity";

        private const string CharacterPrefabPath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/" +
            "Lengyue_GameCharacter.prefab";

        private const string MockSceneDataPath =
            "Packages/com.bytedance.pico.xr/Assets/Resources/MockSceneData.json";

        [MenuItem("MateSoul/Room Navigation/Build Validation Scene")]
        public static void BuildValidationScene()
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            var mockSceneData = AssetDatabase.LoadAssetAtPath<TextAsset>(MockSceneDataPath);
            if (characterPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Production character prefab was not found at {CharacterPrefabPath}.");
            }

            if (mockSceneData == null)
            {
                throw new InvalidOperationException(
                    $"PICO mock scene data was not found at {MockSceneDataPath}.");
            }

            ValidateMockSnapshot(mockSceneData);

            var previousScene = SceneManager.GetActiveScene();
            var canRestorePreviousScene =
                previousScene.IsValid() &&
                previousScene.isLoaded &&
                !string.IsNullOrEmpty(previousScene.path);
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                canRestorePreviousScene ? NewSceneMode.Additive : NewSceneMode.Single);

            try
            {
                SceneManager.SetActiveScene(scene);
                CreateLighting();
                CreateCameraRig();

                var navigationRoot = new GameObject("RoomNavigation");
                navigationRoot.AddComponent<RuntimeRoomNavigation>();
                var bootstrap = navigationRoot.AddComponent<RoomNavigationBootstrap>();

                var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
                character.name = "Lengyue_RoomWalker";
                character.SetActive(false);

                bootstrap.Configure(mockSceneData, character, true, 1701);
                var validationProbe = navigationRoot.AddComponent<RoomNavigationValidationProbe>();
                validationProbe.Configure(bootstrap, 10f);

                EditorSceneManager.SaveScene(scene, ScenePath);
                EnsureFirstBuildScene(ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"ROOM_NAVIGATION_SCENE_BUILT path={ScenePath} " +
                    $"mock={MockSceneDataPath} character={CharacterPrefabPath}");
            }
            finally
            {
                if (canRestorePreviousScene)
                {
                    SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        [MenuItem("MateSoul/Room Navigation/Open Validation Scene")]
        public static void OpenValidationScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("MateSoul/Room Navigation/Run Validation Scene")]
        public static void RunValidationScene()
        {
            BuildValidationScene();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("MateSoul/Room Navigation/Build Android Validation APK")]
        public static void BuildAndroidValidationApk()
        {
            BuildValidationScene();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android,
                    BuildTarget.Android))
            {
                throw new InvalidOperationException("Unable to switch the active build target to Android.");
            }

            var outputPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../Builds/RoomNavigation_v001.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android build failed: {report.summary.result}, errors={report.summary.totalErrors}.");
            }

            Debug.Log(
                $"ROOM_NAVIGATION_ANDROID_BUILD_PASS path={outputPath} " +
                $"bytes={report.summary.totalSize} duration={report.summary.totalTime}");
        }

        private static void ValidateMockSnapshot(TextAsset mockSceneData)
        {
            var snapshot = new PicoMockRoomMapProvider(mockSceneData)
                .LoadAsync(default)
                .GetAwaiter()
                .GetResult();
            var validation = snapshot.Validate();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"PICO mock room data is not navigation-ready: {validation.Error}");
            }

            var floorMesh = RoomGeometry.CreateHorizontalPolygonMesh(
                snapshot.MainFloor.Vertices,
                "MockFloorValidation");
            try
            {
                if (floorMesh.vertexCount < 3 || floorMesh.triangles.Length < 3)
                {
                    throw new InvalidOperationException(
                        "PICO mock room floor triangulation produced no usable geometry.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(floorMesh);
            }
        }

        public static XROrigin CreateCameraRig()
        {
            var xrOriginObject = new GameObject("XR Origin (XR Rig)");
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginObject.transform, false);

            var xrCameraObject = new GameObject("XR Camera");
            xrCameraObject.transform.SetParent(cameraOffset.transform, false);
            var xrCamera = xrCameraObject.AddComponent<Camera>();
            xrCamera.nearClipPlane = 0.05f;
            xrCamera.allowHDR = false;
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            xrCameraObject.AddComponent<AudioListener>();

            var positionAction = new InputAction(
                "HMD Position",
                InputActionType.Value,
                "<XRHMD>/centerEyePosition");
            var rotationAction = new InputAction(
                "HMD Rotation",
                InputActionType.Value,
                "<XRHMD>/centerEyeRotation");
            var poseDriver = xrCameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(positionAction);
            poseDriver.rotationInput = new InputActionProperty(rotationAction);
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            var xrOrigin = xrOriginObject.AddComponent<XROrigin>();
            xrOrigin.Camera = xrCamera;
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var manager = xrOriginObject.AddComponent<PXR_Manager>();
            manager.openMRC = false;
            manager.eyeTracking = true;
            manager.sharpeningMode = SharpeningMode.Quality;
            manager.sharpeningEnhance = SharpeningEnhance.SelfAdaptive;
            manager.useRecommendedAntiAliasingLevel = true;

            var editorCameraObject = new GameObject("Editor Overview Camera");
            editorCameraObject.transform.position = new Vector3(-0.6f, 6.5f, -5.5f);
            editorCameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(-0.6f, 0.5f, 2.4f) - editorCameraObject.transform.position,
                Vector3.up);
            var editorCamera = editorCameraObject.AddComponent<Camera>();
            editorCamera.fieldOfView = 52f;
            editorCamera.nearClipPlane = 0.05f;
            editorCamera.clearFlags = CameraClearFlags.SolidColor;
            editorCamera.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 1f);
            editorCameraObject.AddComponent<AudioListener>();

            var selector = xrOriginObject.AddComponent<RoomNavigationCameraSelector>();
            selector.Configure(xrCameraObject, editorCameraObject);
            return xrOrigin;
        }

        public static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.17f, 0.2f);
        }

        private static void EnsureFirstBuildScene(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            scenes.AddRange(
                EditorBuildSettings.scenes.Where(existing =>
                    !string.Equals(existing.path, scenePath, StringComparison.Ordinal)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
