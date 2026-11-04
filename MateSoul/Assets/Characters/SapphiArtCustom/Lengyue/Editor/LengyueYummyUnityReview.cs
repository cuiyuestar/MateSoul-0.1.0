using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Animation;
using MateSoul.Cloth;
using MateSoul.Hair;
using MateSoul.Validation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class LengyueYummyUnityReview
    {
        const string Root = "Assets/Characters/SapphiArtCustom/Lengyue";
        const string ActionFbx =
            Root + "/Runtime/Animations/Yummy/Yummy_Action_InPlace.fbx";
        const string BodyClip =
            Root + "/Runtime/Animations/Yummy/Lengyue_Yummy_BodyOnly.anim";
        const string ScenePath =
            Root + "/Validation/Lengyue_Yummy_Capture.unity";
        const float EnhancedHairMaxBendDegrees = 20f;
        const float EnhancedHairMaxChainBendDegrees = 15.6f;
        const float EnhancedSkirtResponseMultiplier = 1.45f;
        const float EnhancedSkirtMaxResponseDegrees = 42f;
        const float EnhancedSkirtGravityResponse = .5f;
        const float EnhancedSkirtInertiaResponse = .45f;
        const float EnhancedSkirtLateralResponse = 1.5f;
        const float EnhancedSkirtSpringFrequency = 2.6f;
        const float EnhancedSkirtDampingRatio = .58f;
        const float EnhancedSkirtMaxSecondaryDegrees = 7f;
        const float EnhancedSkirtMaxAcceleration = 6f;
        const int CaptureWidth = 1920;
        const int CaptureHeight = 1080;
        const int CaptureMsaa = 4;
        const int CaptureSupersample = 2;
        const float ReferenceOrthographicSize = .5f;
        static readonly Vector3 ReferenceCameraPosition =
            new Vector3(.18f, 1.41f, 3.2f);
        static readonly Vector3 ReferenceCameraTarget =
            new Vector3(.18f, 1.41f, 0);
        static string Repository =>
            Path.GetFullPath(Application.dataPath + "/../..");
        static string Output => Repository +
            "/ArtSource/SapphiArt/Animation/Yummy";
        static double nextPoll;

        [Serializable]
        sealed class ClipBuildReport
        {
            public string unity;
            public string source;
            public string sourceClip;
            public string outputClip;
            public string mappingMode;
            public float sourceLength;
            public float sourceFrameRate;
            public int sourceBindings;
            public int mappedBindings;
            public int facialBindings;
            public int excludedBindings;
            public int mappedBones;
            public string facialSource;
            public string[] missingBones;
            public string[] mappedBoneNames;
            public string[] excludedBoneNames;
            public string[] facialShapeNames;
            public bool skirtCurvesRemoved;
            public bool hairCurvesRemoved;
            public bool passed;
        }

        [Serializable]
        sealed class UnityRenderReport
        {
            public string unity;
            public string renderer;
            public string clip;
            public bool deterministicRuntimeStep;
            public bool toonVerified;
            public bool hairInitialized;
            public bool skirtInitialized;
            public bool headPoseGuardDisabled;
            public bool forcedSkinningUpdate;
            public int frameRate;
            public int frames;
            public int captureWidth;
            public int captureHeight;
            public int captureMsaa;
            public float cameraOrthographicSize;
            public Vector3 cameraPosition;
            public int rendererCount;
            public int renderersUsingHandBones;
            public int hairConfiguredBones;
            public int hairSimulatedJoints;
            public int hairSubsteps;
            public int meshRenderingErrors;
            public float hairMaxBoneAngleDeg;
            public float hairTipRelativeMovementM;
            public float skirtMaxBoneAngleDeg;
            public float skirtMaxLeftFlexDeg;
            public float skirtMaxRightFlexDeg;
            public float leftHandMovementM;
            public float rightHandMovementM;
            public float headNeckDistanceRatioDeviation;
            public bool passed;
        }

        sealed class RetargetBone
        {
            public string sourceName;
            public string targetName;
            public string targetPath;
            public Transform source;
            public Vector3 sourceRestPosition;
            public Quaternion sourceRestRotation;
            public Vector3 sourceRestScale;
            public Vector3 targetRestPosition;
            public Quaternion targetRestRotation;
            public Vector3 targetRestScale;
            public Quaternion previousRotation;
            public bool hasPreviousRotation;
            public readonly AnimationCurve[] sourcePosition =
                new AnimationCurve[3];
            public readonly AnimationCurve[] sourceRotation =
                new AnimationCurve[4];
            public readonly AnimationCurve[] sourceScale =
                new AnimationCurve[3];
            public readonly AnimationCurve[] position = {
                new AnimationCurve(), new AnimationCurve(), new AnimationCurve()
            };
            public readonly AnimationCurve[] rotation = {
                new AnimationCurve(), new AnimationCurve(),
                new AnimationCurve(), new AnimationCurve()
            };
            public readonly AnimationCurve[] scale = {
                new AnimationCurve(), new AnimationCurve(), new AnimationCurve()
            };
        }

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayModeState;
            EditorApplication.playModeStateChanged += OnPlayModeState;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string playerRequest =
                Repository + "/.dbg/yummy-unity-build-player";
            if (File.Exists(playerRequest))
            {
                File.Delete(playerRequest);
                try
                {
                    BuildCapturePlayerInteractive();
                }
                catch (Exception exception)
                {
                    Directory.CreateDirectory(Output + "/reports");
                    File.WriteAllText(
                        Output +
                        "/reports/unity_player_build_error.txt",
                        exception.ToString());
                    Debug.LogException(exception);
                }
                return;
            }
            string request = Repository + "/.dbg/yummy-unity-capture";
            if (!File.Exists(request))
                return;
            File.Delete(request);
            try
            {
                BuildAndCapture();
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Output + "/reports");
                File.WriteAllText(
                    Output + "/reports/unity_capture_error.txt",
                    exception.ToString());
                Debug.LogException(exception);
            }
        }

        static void OnPlayModeState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("MateSoul/Lengyue/Render Yummy In Unity")]
        public static void BuildAndCapture()
        {
            Directory.CreateDirectory(Output + "/evidence/unity_runtime");
            Directory.CreateDirectory(Output + "/reports");
            File.Delete(Output + "/reports/unity_capture_error.txt");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter();

            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(ActionFbx)
                .OfType<AnimationClip>()
                .Where(item => !item.name.StartsWith("__preview__"))
                .OrderByDescending(item => item.length)
                .FirstOrDefault();
            if (!sourceClip)
                throw new InvalidOperationException(
                    "Yummy FBX contains no imported AnimationClip");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LengyueLayeredDressBuilder.Prefab);
            if (!prefab)
                throw new InvalidOperationException(
                    "Canonical Lengyue prefab is missing");
            AnimationClip bodyClip = BuildBodyClip(sourceClip, prefab);
            Scene captureScene = BuildCaptureScene(prefab, bodyClip);
            SceneManager.SetActiveScene(captureScene);
            EditorApplication.isPlaying = true;
        }

        public static void BuildAndCaptureBatch()
        {
            try
            {
                Directory.CreateDirectory(Output + "/evidence/unity_runtime");
                Directory.CreateDirectory(Output + "/reports");
                File.Delete(Output + "/reports/unity_capture_error.txt");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter();
                var sourceClip = AssetDatabase.LoadAllAssetsAtPath(ActionFbx)
                    .OfType<AnimationClip>()
                    .Where(item => !item.name.StartsWith("__preview__"))
                    .OrderByDescending(item => item.length)
                    .FirstOrDefault();
                if (!sourceClip)
                    throw new InvalidOperationException(
                        "Yummy FBX contains no imported AnimationClip");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    LengyueLayeredDressBuilder.Prefab);
                if (!prefab)
                    throw new InvalidOperationException(
                        "Canonical Lengyue prefab is missing");
                AnimationClip bodyClip = BuildBodyClip(sourceClip, prefab);
                Scene scene = BuildCaptureScene(prefab, bodyClip);
                var camera = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true))
                    .Single(item => item.name == "Yummy Unity Camera");
                Object.DestroyImmediate(
                    camera.GetComponent<LengyueYummyCaptureProbe>());
                var character = scene.GetRootGameObjects().Single(
                    item => item.name == "Lengyue_Yummy_Runtime");
                bool passed = CaptureSynchronous(
                    character, camera, bodyClip);
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Output + "/reports");
                File.WriteAllText(
                    Output + "/reports/unity_capture_error.txt",
                    exception.ToString());
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void BuildCapturePlayerBatch()
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget !=
                    BuildTarget.StandaloneOSX)
                    throw new InvalidOperationException(
                        "Launch Unity with -buildTarget StandaloneOSX");
                Directory.CreateDirectory(Output + "/build");
                Directory.CreateDirectory(Output + "/reports");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter();
                var sourceClip = AssetDatabase.LoadAllAssetsAtPath(ActionFbx)
                    .OfType<AnimationClip>()
                    .Where(item => !item.name.StartsWith("__preview__"))
                    .OrderByDescending(item => item.length)
                    .FirstOrDefault();
                if (!sourceClip)
                    throw new InvalidOperationException(
                        "Yummy FBX contains no imported AnimationClip");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    LengyueLayeredDressBuilder.Prefab);
                if (!prefab)
                    throw new InvalidOperationException(
                        "Canonical Lengyue prefab is missing");
                AnimationClip bodyClip = BuildBodyClip(sourceClip, prefab);
                BuildCaptureScene(prefab, bodyClip);

                string player = Output + "/build/YummyCapture.app";
                if (Directory.Exists(player))
                    Directory.Delete(player, true);
                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions {
                        scenes = new[] { ScenePath },
                        locationPathName = player,
                        target = BuildTarget.StandaloneOSX,
                        options = BuildOptions.None
                    });
                File.WriteAllText(
                    Output + "/reports/unity_player_build.json",
                    "{\"result\":\"" + report.summary.result +
                    "\",\"errors\":" + report.summary.totalErrors +
                    ",\"warnings\":" + report.summary.totalWarnings +
                    ",\"size\":" + report.summary.totalSize + "}");
                if (Application.isBatchMode)
                    EditorApplication.Exit(
                        report.summary.result ==
                        BuildResult.Succeeded ? 0 : 1);
                else if (report.summary.result !=
                    BuildResult.Succeeded)
                    throw new InvalidOperationException(
                        "Yummy capture Player build failed");
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Output + "/reports");
                File.WriteAllText(
                    Output + "/reports/unity_player_build_error.txt",
                    exception.ToString());
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        [MenuItem("MateSoul/Lengyue/Build Yummy Capture Player")]
        public static void BuildCapturePlayerInteractive()
        {
            BuildTarget previousTarget =
                EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup previousGroup =
                BuildPipeline.GetBuildTargetGroup(previousTarget);
            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneOSX))
                    throw new InvalidOperationException(
                        "Failed to switch to StandaloneOSX");
                BuildCapturePlayerBatch();
            }
            finally
            {
                if (previousTarget != BuildTarget.StandaloneOSX)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(
                        previousGroup, previousTarget);
            }
        }

        static bool CaptureSynchronous(
            GameObject character, Camera camera, AnimationClip clip)
        {
            var animator = character.GetComponent<Animator>();
            var headGuard = character.GetComponent<HeadPoseGuard>();
            var skirt = character.GetComponent<SkirtPoseGuard>();
            var hair = character.GetComponentInChildren<HairSpringPhysics>(true);
            if (!animator || !headGuard || !skirt || !hair)
                throw new InvalidOperationException(
                    "Canonical runtime components are missing");
            animator.enabled = false;
            headGuard.enabled = false;
            skirt.enabled = false;
            skirt.BindByName();
            skirt.Initialize();
            hair.enabled = false;
            ConfigureEnhancedHair(hair);
            ConfigureEnhancedSkirt(skirt);
            hair.Initialize();
            var surfaceContact = hair.GetComponent<LengyueHairSurfaceContact>();
            surfaceContact.Initialize();

            Transform[] skirtBones = character
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith(
                    "Skirt_", StringComparison.Ordinal))
                .ToArray();
            var initialSkirtRotations = skirtBones.ToDictionary(
                item => item, item => item.localRotation);
            Transform hairTip = Find(character, "H_Back_10_08")
                ?? Find(character, "H_SideL_01_08")
                ?? hair.bones.Last(item => item.transform).transform;
            Transform head = Find(character, "Head 1");
            Transform neck = Find(character, "Neck");
            Transform leftHand = Find(character, "Hand_L");
            Transform rightHand = Find(character, "Hand_R");
            if (!hairTip || !head || !neck || !leftHand || !rightHand)
                throw new InvalidOperationException(
                    "Motion validation landmarks are missing");
            var renderers = character
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int renderersUsingHandBones = renderers.Count(renderer =>
                renderer.bones.Contains(leftHand) ||
                renderer.bones.Contains(rightHand));
            bool toonVerified = renderers.All(renderer =>
                renderer.sharedMaterials.All(material =>
                    material && material.shader &&
                    material.shader.name == "MateSoul/Character Toon"));

            clip.SampleAnimation(character, 0);
            skirt.ResetDynamics();
            skirt.Evaluate(1f / 30f);
            hair.ResetSimulation();
            for (int i = 0; i < 6; i++)
                hair.Advance(1f / 30f);

            string frames = Output + "/evidence/unity_runtime/frames";
            if (Directory.Exists(frames))
                Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            var target = new RenderTexture(
                CaptureWidth, CaptureHeight, 24,
                RenderTextureFormat.ARGB32) {
                antiAliasing = CaptureMsaa
            };
            target.Create();
            var resolved = new RenderTexture(
                CaptureWidth, CaptureHeight, 0,
                RenderTextureFormat.ARGB32);
            resolved.Create();
            var pixels = new Texture2D(
                CaptureWidth, CaptureHeight,
                TextureFormat.RGB24, false);
            camera.targetTexture = target;
            Shader.WarmupAllShaders();
            camera.Render();
            camera.Render();
            var bakedMeshes = new Mesh[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer sourceRenderer = renderers[i];
                var mesh = new Mesh {
                    name = sourceRenderer.name + "_YummyBaked"
                };
                sourceRenderer.BakeMesh(mesh);
                bakedMeshes[i] = mesh;
            }

            int simulatedJoints = 0;
            int substeps = 0;
            int meshErrors = 0;
            float hairMaxAngle = 0;
            float skirtMaxAngle = 0;
            float skirtMaxLeft = 0;
            float skirtMaxRight = 0;
            Vector3 tipMinimum = head.InverseTransformPoint(hairTip.position);
            Vector3 tipMaximum = tipMinimum;
            Vector3 leftHandMinimum = leftHand.position;
            Vector3 leftHandMaximum = leftHand.position;
            Vector3 rightHandMinimum = rightHand.position;
            Vector3 rightHandMaximum = rightHand.position;
            float restHeadNeckDistance =
                Vector3.Distance(head.position, neck.position);
            float maxHeadNeckDistanceDeviation = 0;
            Application.LogCallback logCallback = (
                message, stack, type) => {
                if (message.Contains(
                    "SkinnedMeshRenderer: Rendering stopped"))
                    meshErrors++;
            };
            Application.logMessageReceived += logCallback;
            try
            {
                for (int frame = 0; frame < 173; frame++)
                {
                    clip.SampleAnimation(
                        character, Mathf.Min(clip.length, frame / 30f));
                    skirt.Evaluate(1f / 30f);
                    hair.Advance(1f / 30f);
                    surfaceContact.Solve();

                    simulatedJoints = Mathf.Max(
                        simulatedJoints, hair.LastSimulatedJoints);
                    substeps += hair.LastSubsteps;
                    hairMaxAngle = Mathf.Max(
                        hairMaxAngle, hair.MaxBoneAngle);
                    skirtMaxLeft = Mathf.Max(
                        skirtMaxLeft, Mathf.Abs(skirt.LastLeftFlex));
                    skirtMaxRight = Mathf.Max(
                        skirtMaxRight, Mathf.Abs(skirt.LastRightFlex));
                    foreach (Transform bone in skirtBones)
                        skirtMaxAngle = Mathf.Max(
                            skirtMaxAngle,
                            Quaternion.Angle(
                                initialSkirtRotations[bone],
                                bone.localRotation));
                    Vector3 tip =
                        head.InverseTransformPoint(hairTip.position);
                    tipMinimum = Vector3.Min(tipMinimum, tip);
                    tipMaximum = Vector3.Max(tipMaximum, tip);
                    leftHandMinimum = Vector3.Min(
                        leftHandMinimum, leftHand.position);
                    leftHandMaximum = Vector3.Max(
                        leftHandMaximum, leftHand.position);
                    rightHandMinimum = Vector3.Min(
                        rightHandMinimum, rightHand.position);
                    rightHandMaximum = Vector3.Max(
                        rightHandMaximum, rightHand.position);
                    maxHeadNeckDistanceDeviation = Mathf.Max(
                        maxHeadNeckDistanceDeviation,
                        Mathf.Abs(Vector3.Distance(
                            head.position, neck.position) /
                            restHeadNeckDistance - 1f));

                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].BakeMesh(bakedMeshes[i]);
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = resolved;
                    pixels.ReadPixels(
                        new Rect(
                            0, 0, CaptureWidth, CaptureHeight),
                        0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(
                        Path.Combine(
                            frames, $"frame_{frame:D4}.png"),
                        pixels.EncodeToPNG());
                }
            }
            finally
            {
                Application.logMessageReceived -= logCallback;
                for (int i = 0; i < renderers.Length; i++)
                    Object.DestroyImmediate(bakedMeshes[i]);
                camera.targetTexture = null;
                target.Release();
                resolved.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(pixels);
            }

            var report = new UnityRenderReport {
                unity = Application.unityVersion,
                renderer = "Unity URP Camera.Render",
                clip = clip.name,
                deterministicRuntimeStep = true,
                toonVerified = toonVerified,
                hairInitialized = hair.IsInitialized,
                skirtInitialized = skirt.IsInitialized,
                headPoseGuardDisabled = !headGuard.enabled,
                forcedSkinningUpdate = true,
                frameRate = 30,
                frames = 173,
                captureWidth = CaptureWidth,
                captureHeight = CaptureHeight,
                captureMsaa = CaptureMsaa,
                cameraOrthographicSize =
                    camera.orthographicSize,
                cameraPosition =
                    camera.transform.position,
                rendererCount = renderers.Length,
                renderersUsingHandBones =
                    renderersUsingHandBones,
                hairConfiguredBones = hair.bones.Length,
                hairSimulatedJoints = simulatedJoints,
                hairSubsteps = substeps,
                hairMaxBoneAngleDeg = hairMaxAngle,
                hairTipRelativeMovementM =
                    Vector3.Distance(tipMinimum, tipMaximum),
                skirtMaxBoneAngleDeg = skirtMaxAngle,
                skirtMaxLeftFlexDeg = skirtMaxLeft,
                skirtMaxRightFlexDeg = skirtMaxRight,
                leftHandMovementM = Vector3.Distance(
                    leftHandMinimum, leftHandMaximum),
                rightHandMovementM = Vector3.Distance(
                    rightHandMinimum, rightHandMaximum),
                headNeckDistanceRatioDeviation =
                    maxHeadNeckDistanceDeviation,
                meshRenderingErrors = meshErrors
            };
            report.passed = report.deterministicRuntimeStep &&
                report.toonVerified && report.hairInitialized &&
                report.skirtInitialized &&
                report.headPoseGuardDisabled &&
                report.forcedSkinningUpdate &&
                report.renderersUsingHandBones > 0 &&
                report.frames == 173 &&
                report.hairSimulatedJoints > 0 &&
                report.hairSubsteps > 0 &&
                report.hairMaxBoneAngleDeg > .1f &&
                report.hairTipRelativeMovementM > .0002f &&
                report.skirtMaxBoneAngleDeg > .05f &&
                report.leftHandMovementM > .1f &&
                report.rightHandMovementM > .1f &&
                report.headNeckDistanceRatioDeviation < .05f &&
                report.meshRenderingErrors == 0;
            File.WriteAllText(
                Output + "/reports/unity_render.json",
                JsonUtility.ToJson(report, true));
            Debug.Log("LENGYUE_YUMMY_UNITY_" +
                (report.passed ? "PASS" : "FAIL") + "\n" +
                JsonUtility.ToJson(report, true));
            return report.passed;
        }

        static bool AssignSourceCurve(
            RetargetBone bone, string property, AnimationCurve curve)
        {
            int separator = property.LastIndexOf('.');
            if (separator < 0 || separator == property.Length - 1)
                return false;
            int channel = "xyzw".IndexOf(property[separator + 1]);
            if (channel < 0)
                return false;
            string group = property.Substring(0, separator);
            if (group == "m_LocalPosition" && channel < 3)
                bone.sourcePosition[channel] = curve;
            else if (group == "m_LocalRotation" && channel < 4)
                bone.sourceRotation[channel] = curve;
            else if (group == "m_LocalScale" && channel < 3)
                bone.sourceScale[channel] = curve;
            else
                return false;
            return true;
        }

        static Vector3 EvaluateVector(
            AnimationCurve[] curves, float time)
        {
            return new Vector3(
                curves[0].Evaluate(time),
                curves[1].Evaluate(time),
                curves[2].Evaluate(time));
        }

        static Quaternion EvaluateQuaternion(
            AnimationCurve[] curves, float time)
        {
            var value = new Quaternion(
                curves[0].Evaluate(time),
                curves[1].Evaluate(time),
                curves[2].Evaluate(time),
                curves[3].Evaluate(time));
            return Quaternion.Normalize(value);
        }

        static float SafeRatio(float value, float basis)
        {
            return Mathf.Abs(basis) > 1e-6f ? value / basis : 1f;
        }

        static void AddKeys(
            AnimationCurve[] curves, float time, Vector3 value)
        {
            curves[0].AddKey(time, value.x);
            curves[1].AddKey(time, value.y);
            curves[2].AddKey(time, value.z);
        }

        static void AddKeys(
            AnimationCurve[] curves, float time, Quaternion value)
        {
            curves[0].AddKey(time, value.x);
            curves[1].AddKey(time, value.y);
            curves[2].AddKey(time, value.z);
            curves[3].AddKey(time, value.w);
        }

        static void SetCurves(
            AnimationClip clip, string path, string property,
            AnimationCurve[] curves, string[] channels)
        {
            for (int channel = 0; channel < curves.Length; channel++)
            {
                AnimationCurve curve = curves[channel];
                for (int key = 0; key < curve.length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(
                        path, typeof(Transform),
                        property + "." + channels[channel]),
                    curve);
            }
        }

        static void ConfigureImporter()
        {
            AssetDatabase.ImportAsset(
                ActionFbx, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ActionFbx) as ModelImporter;
            if (!importer)
                throw new InvalidOperationException(
                    "Could not create the Yummy FBX importer");
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression =
                ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
        }

        static AnimationClip BuildBodyClip(
            AnimationClip source, GameObject prefab)
        {
            var targets = prefab.GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var mappedBones = new HashSet<string>();
            var excludedBones = new HashSet<string>();
            var missingBones = new HashSet<string>();
            int mappedBindings = 0;
            int excludedBindings = 0;
            var generated = new AnimationClip {
                name = "Lengyue_Yummy_BodyOnly",
                frameRate = 30,
                wrapMode = WrapMode.Once
            };
            EditorCurveBinding[] bindings =
                AnimationUtility.GetCurveBindings(source);
            var includedSourceBones = new HashSet<string>();
            foreach (var sourceBinding in bindings)
            {
                string sourceName = Leaf(sourceBinding.path);
                if (IsExcluded(sourceName))
                {
                    excludedBindings++;
                    excludedBones.Add(sourceName);
                    continue;
                }
                includedSourceBones.Add(sourceName);
                string targetName =
                    sourceName == "Head" ? "Head 1" : sourceName;
                if (!targets.TryGetValue(targetName, out Transform[] matches) ||
                    matches.Length != 1)
                {
                    missingBones.Add(sourceName);
                    continue;
                }
                mappedBones.Add(targetName);
            }
            if (missingBones.Count > 0)
                throw new InvalidOperationException(
                    "Missing Yummy target bones: " +
                    string.Join(", ", missingBones));

            GameObject sourcePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ActionFbx);
            if (!sourcePrefab)
                throw new InvalidOperationException(
                    "Yummy FBX model hierarchy is missing");
            GameObject sourceInstance = Object.Instantiate(sourcePrefab);
            sourceInstance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sourceTransforms = sourceInstance
                    .GetComponentsInChildren<Transform>(true)
                    .GroupBy(item => item.name)
                    .ToDictionary(
                        group => group.Key, group => group.ToArray());
                var retargetBones = new List<RetargetBone>();
                foreach (string sourceName in includedSourceBones.OrderBy(
                    name => name))
                {
                    string targetName =
                        sourceName == "Head" ? "Head 1" : sourceName;
                    if (!sourceTransforms.TryGetValue(
                            sourceName, out Transform[] sourceMatches) ||
                        sourceMatches.Length != 1)
                        throw new InvalidOperationException(
                            "Missing unique source transform: " +
                            sourceName);
                    Transform sourceTransform = sourceMatches[0];
                    Transform targetTransform = targets[targetName][0];
                    retargetBones.Add(new RetargetBone {
                        sourceName = sourceName,
                        targetName = targetName,
                        targetPath =
                            AnimationUtility.CalculateTransformPath(
                                targetTransform, prefab.transform),
                        source = sourceTransform,
                        sourceRestPosition =
                            sourceTransform.localPosition,
                        sourceRestRotation =
                            sourceTransform.localRotation,
                        sourceRestScale =
                            sourceTransform.localScale,
                        targetRestPosition =
                            targetTransform.localPosition,
                        targetRestRotation =
                            targetTransform.localRotation,
                        targetRestScale =
                            targetTransform.localScale
                    });
                }
                var bySourceName = retargetBones.ToDictionary(
                    bone => bone.sourceName);
                foreach (EditorCurveBinding binding in bindings)
                {
                    string sourceName = Leaf(binding.path);
                    if (!bySourceName.TryGetValue(
                            sourceName, out RetargetBone bone))
                        continue;
                    AnimationCurve curve =
                        AnimationUtility.GetEditorCurve(source, binding);
                    if (AssignSourceCurve(
                            bone, binding.propertyName, curve))
                        mappedBindings++;
                }
                foreach (RetargetBone bone in retargetBones)
                    if (bone.sourcePosition.Any(curve => curve == null) ||
                        bone.sourceRotation.Any(curve => curve == null) ||
                        bone.sourceScale.Any(curve => curve == null))
                        throw new InvalidOperationException(
                            "Incomplete source transform curves: " +
                            bone.sourceName);

                for (int frame = 0; frame < 173; frame++)
                {
                    float time = frame / 30f;
                    foreach (RetargetBone bone in retargetBones)
                    {
                        float sourceTime = Mathf.Min(
                            source.length, time);
                        Vector3 sourcePosition = EvaluateVector(
                            bone.sourcePosition, sourceTime);
                        Quaternion sourceRotation =
                            EvaluateQuaternion(
                                bone.sourceRotation, sourceTime);
                        Vector3 sourceScale = EvaluateVector(
                            bone.sourceScale, sourceTime);
                        Vector3 position = bone.targetRestPosition +
                            (sourcePosition -
                             bone.sourceRestPosition);
                        Quaternion rotation = sourceRotation;
                        if (bone.hasPreviousRotation &&
                            Quaternion.Dot(
                                bone.previousRotation, rotation) < 0)
                            rotation = new Quaternion(
                                -rotation.x, -rotation.y,
                                -rotation.z, -rotation.w);
                        bone.previousRotation = rotation;
                        bone.hasPreviousRotation = true;
                        Vector3 scale = new Vector3(
                            bone.targetRestScale.x * SafeRatio(
                                sourceScale.x,
                                bone.sourceRestScale.x),
                            bone.targetRestScale.y * SafeRatio(
                                sourceScale.y,
                                bone.sourceRestScale.y),
                            bone.targetRestScale.z * SafeRatio(
                                sourceScale.z,
                                bone.sourceRestScale.z));
                        AddKeys(bone.position, time, position);
                        AddKeys(bone.rotation, time, rotation);
                        AddKeys(bone.scale, time, scale);
                    }
                }
                foreach (RetargetBone bone in retargetBones)
                {
                    SetCurves(
                        generated, bone.targetPath,
                        "m_LocalPosition", bone.position,
                        new[] { "x", "y", "z" });
                    SetCurves(
                        generated, bone.targetPath,
                        "m_LocalRotation", bone.rotation,
                        new[] { "x", "y", "z", "w" });
                    SetCurves(
                        generated, bone.targetPath,
                        "m_LocalScale", bone.scale,
                        new[] { "x", "y", "z" });
                }
            }
            finally
            {
                Object.DestroyImmediate(sourceInstance);
            }
            string[] facialShapeNames =
                AddFacialCurves(generated, prefab);
            generated.EnsureQuaternionContinuity();

            AnimationClip existing =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyClip);
            if (existing)
            {
                EditorUtility.CopySerialized(generated, existing);
                existing.name = generated.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(generated);
            }
            else
            {
                AssetDatabase.CreateAsset(generated, BodyClip);
                existing = generated;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                BodyClip, ImportAssetOptions.ForceSynchronousImport);
            existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyClip);

            EditorCurveBinding[] resultBindings =
                AnimationUtility.GetCurveBindings(existing);
            string[] resultBones = resultBindings.Select(
                binding => Leaf(binding.path)).Distinct().ToArray();
            bool skirtRemoved = resultBones.All(name =>
                !name.StartsWith("Skirt_", StringComparison.Ordinal) &&
                !name.StartsWith("OuterSkirt_", StringComparison.Ordinal));
            bool hairRemoved = resultBones.All(name => !IsHair(name));
            var report = new ClipBuildReport {
                unity = Application.unityVersion,
                source = ActionFbx,
                sourceClip = source.name,
                outputClip = BodyClip,
                mappingMode =
                    "UnityAbsoluteRotation_TargetRestTranslation",
                sourceLength = source.length,
                sourceFrameRate = source.frameRate,
                sourceBindings = bindings.Length,
                mappedBindings = mappedBindings,
                facialBindings = facialShapeNames.Length,
                excludedBindings = excludedBindings,
                mappedBones = mappedBones.Count,
                facialSource =
                    "AnimatorReference_ReconstructedBecauseSourceFbxHasNoBlendShapeChannels",
                missingBones = missingBones.OrderBy(name => name).ToArray(),
                mappedBoneNames = mappedBones.OrderBy(name => name).ToArray(),
                excludedBoneNames = excludedBones.OrderBy(name => name).ToArray(),
                facialShapeNames = facialShapeNames,
                skirtCurvesRemoved = skirtRemoved,
                hairCurvesRemoved = hairRemoved
            };
            report.passed = report.sourceLength > 5.7f &&
                report.sourceFrameRate == 30 && report.mappedBones >= 60 &&
                report.facialBindings == 12 &&
                report.missingBones.Length == 0 &&
                report.skirtCurvesRemoved && report.hairCurvesRemoved;
            File.WriteAllText(
                Output + "/reports/unity_clip_build.json",
                JsonUtility.ToJson(report, true));
            if (!report.passed)
                throw new InvalidOperationException(
                    "Yummy body clip mapping failed:\n" +
                    JsonUtility.ToJson(report, true));
            return existing;
        }

        static Scene BuildCaptureScene(
            GameObject prefab, AnimationClip bodyClip)
        {
            if (!prefab.GetComponent<LengyueSleeveCloth>())
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    contents.AddComponent<LengyueSleeveCloth>();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene existing = SceneManager.GetSceneAt(i);
                if (existing.path == ScenePath)
                    EditorSceneManager.CloseScene(existing, true);
            }
            Scene activeScene = SceneManager.GetActiveScene();
            bool replaceEmptyUntitled =
                !Application.isBatchMode &&
                SceneManager.sceneCount == 1 &&
                string.IsNullOrEmpty(activeScene.path);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                Application.isBatchMode || replaceEmptyUntitled
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab, scene);
            character.name = "Lengyue_Yummy_Runtime";
            var hair = character.GetComponentInChildren<HairSpringPhysics>(true);
            var skirt = character.GetComponent<SkirtPoseGuard>();
            if (!hair || !skirt)
                throw new InvalidOperationException(
                    "Secondary-motion components are missing");
            ConfigureEnhancedHair(hair);
            ConfigureEnhancedSkirt(skirt);

            var cameraObject = new GameObject(
                "Yummy Unity Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.34f, .35f, .38f);
            camera.orthographic = true;
            camera.orthographicSize = ReferenceOrthographicSize;
            camera.aspect =
                (float)CaptureWidth / CaptureHeight;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.transform.position = ReferenceCameraPosition;
            camera.transform.LookAt(ReferenceCameraTarget);

            var lightObject = new GameObject(
                "Yummy Unity Key", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1, .94f, .91f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(38, 148, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.27f, .28f, .34f);
            RenderSettings.skybox = null;

            var probe = cameraObject.AddComponent<LengyueYummyCaptureProbe>();
            probe.character = character;
            probe.clip = bodyClip;
            probe.outputDirectory = Output + "/evidence/unity_runtime";
            probe.reportDirectory = Output + "/reports";
            probe.frameCount = 173;
            probe.frameRate = 30;
            probe.captureWidth = CaptureWidth;
            probe.captureHeight = CaptureHeight;
            probe.captureMsaa = CaptureMsaa;
            probe.captureSupersample = CaptureSupersample;
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        static void ConfigureEnhancedHair(HairSpringPhysics hair)
        {
            hair.quality = HairSpringSolver.Quality.Full;
            hair.maxBendDegrees = EnhancedHairMaxBendDegrees;
            hair.maxChainBendDegrees =
                EnhancedHairMaxChainBendDegrees;
            hair.directionReference =
                Find(hair.transform.root.gameObject, "Head 1");
            for (int i = 0; i < hair.bones.Length; i++)
            {
                HairSpringPhysics.Bone bone = hair.bones[i];
                string name = bone.transform
                    ? bone.transform.name
                    : string.Empty;
                if (name.StartsWith(
                        "H_Fringe_", StringComparison.Ordinal))
                {
                    bone.full =
                        name.EndsWith("_04", StringComparison.Ordinal) ||
                        name.EndsWith("_05", StringComparison.Ordinal) ||
                        name.EndsWith("_06", StringComparison.Ordinal);
                    bone.stiffness = Mathf.Max(bone.stiffness, 1.45f);
                    bone.drag = Mathf.Max(bone.drag, .66f);
                    bone.gravity = Mathf.Max(bone.gravity, .17f);
                    bone.lateralInertiaResponse = 8f;
                    bone.maxLateralDegrees = 14f;
                    bone.maxDepthDegrees = 5f;
                    hair.bones[i] = bone;
                }
                else if (name.StartsWith(
                        "H_SideL_", StringComparison.Ordinal) ||
                    name.StartsWith(
                        "H_SideR_", StringComparison.Ordinal))
                {
                    bone.full = bone.balanced ||
                        name.EndsWith("_05", StringComparison.Ordinal);
                    bone.stiffness = Mathf.Max(bone.stiffness, 1f);
                    bone.drag = Mathf.Max(bone.drag, .58f);
                    bone.gravity = Mathf.Max(bone.gravity, .12f);
                    bone.lateralInertiaResponse = 4f;
                    bone.maxLateralDegrees = 15f;
                    bone.maxDepthDegrees = 8f;
                    hair.bones[i] = bone;
                }
                else if (name.StartsWith(
                        "H_Back_", StringComparison.Ordinal))
                {
                    bone.stiffness = Mathf.Max(bone.stiffness, .9f);
                    bone.drag = Mathf.Max(bone.drag, .58f);
                    bone.gravity = Mathf.Max(bone.gravity, .22f);
                    hair.bones[i] = bone;
                }
                else if (name.StartsWith(
                        "H_FringeFine_", StringComparison.Ordinal) ||
                    name.StartsWith(
                        "H_Detail_", StringComparison.Ordinal))
                {
                    bone.full = false;
                    hair.bones[i] = bone;
                }
            }
            var contact = hair.GetComponent<LengyueHairSurfaceContact>();
            if (!contact) contact = hair.gameObject.AddComponent<LengyueHairSurfaceContact>();
            contact.target = hair.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
        }

        static void ConfigureEnhancedSkirt(SkirtPoseGuard skirt)
        {
            skirt.responseMultiplier =
                EnhancedSkirtResponseMultiplier;
            skirt.maxResponseDegrees =
                EnhancedSkirtMaxResponseDegrees;
            skirt.avoidanceStartDegrees = 3f;
            skirt.avoidanceFullDegrees = 10f;
            skirt.innerSideDrapeDegrees = 14f;
            skirt.outerSideDrapeDegrees = 17f;
            skirt.frontBackDrapeDegrees = 0f;
            skirt.drapeRadialContraction = .16f;
            skirt.drapeReleaseStartDegrees = 5f;
            skirt.drapeReleaseEndDegrees = 14f;
            skirt.gravityResponse =
                EnhancedSkirtGravityResponse;
            skirt.inertiaResponse =
                EnhancedSkirtInertiaResponse;
            skirt.lateralResponse =
                EnhancedSkirtLateralResponse;
            skirt.springFrequency =
                EnhancedSkirtSpringFrequency;
            skirt.dampingRatio =
                EnhancedSkirtDampingRatio;
            skirt.maxSecondaryDegrees =
                EnhancedSkirtMaxSecondaryDegrees;
            skirt.secondaryTipResponse = .48f;
            skirt.maxAcceleration =
                EnhancedSkirtMaxAcceleration;
        }

        static string[] AddFacialCurves(
            AnimationClip clip, GameObject prefab)
        {
            var shapes = new List<string>();
            AddFacialCurve(clip, prefab, "face",
                "face_eye_close_L_BS", shapes,
                (0, 0), (36, 0), (44, 100), (105, 100),
                (110, 0), (172, 0));
            AddFacialCurve(clip, prefab, "face",
                "face_eye_close_R_BS", shapes,
                (0, 0), (36, 0), (44, 100), (105, 100),
                (110, 0), (172, 0));
            AddFacialCurve(clip, prefab, "face",
                "face_eye_wide_L_BS", shapes,
                (0, 0), (18, 12), (29, 55), (40, 0),
                (100, 0), (106, 40), (110, 92), (132, 92),
                (140, 72), (150, 10),
                (160, 0), (172, 0));
            AddFacialCurve(clip, prefab, "face",
                "face_eye_wide_R_BS", shapes,
                (0, 0), (18, 12), (29, 55), (40, 0),
                (100, 0), (106, 40), (110, 92), (132, 92),
                (140, 72), (150, 10),
                (160, 0), (172, 0));
            AddFacialCurve(clip, prefab, "brow",
                "brow_sad_L", shapes,
                (0, 45), (18, 55), (30, 72), (42, 30),
                (106, 24), (120, 58), (138, 52), (150, 34),
                (165, 20), (172, 42));
            AddFacialCurve(clip, prefab, "brow",
                "brow_sad_R", shapes,
                (0, 45), (18, 55), (30, 72), (42, 30),
                (106, 24), (120, 58), (138, 52), (150, 34),
                (165, 20), (172, 42));
            AddFacialCurve(clip, prefab, "face",
                "face_O_BS", shapes,
                (0, 0), (20, 6), (30, 14), (35, 0),
                (140, 0), (148, 18), (152, 0), (157, 14),
                (161, 0), (166, 18), (170, 0), (172, 8));
            AddFacialCurve(clip, prefab, "face",
                "face_sad_BS", shapes,
                (0, 14), (30, 22), (35, 0),
                (126, 0), (140, 4), (172, 0));
            AddFacialCurve(clip, prefab, "face",
                "face_smile_BS", shapes,
                (0, 0), (110, 0), (126, 8), (140, 12),
                (172, 8));
            AddFacialCurve(clip, prefab, "face",
                "face_Puff_BS", shapes,
                (0, 0), (27, 0), (35, 72), (100, 72),
                (105, 68), (110, 58), (118, 25),
                (126, 0), (172, 0));
            AddFacialCurve(clip, prefab, "face",
                "face_jaw_open_BS", shapes,
                (0, 0), (143, 0), (148, 34), (152, 0),
                (157, 28), (161, 0), (166, 32),
                (170, 0), (172, 14));
            AddFacialCurve(clip, prefab, "face",
                "face_E_mouth_BS", shapes,
                (0, 0), (143, 0), (148, 18), (152, 0),
                (157, 14), (161, 0), (166, 18),
                (170, 0), (172, 8));
            return shapes.ToArray();
        }

        static void AddFacialCurve(
            AnimationClip clip, GameObject prefab,
            string rendererName, string shapeSuffix,
            ICollection<string> shapes,
            params (int frame, float value)[] samples)
        {
            SkinnedMeshRenderer renderer = prefab
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(item => item.name == rendererName);
            string[] matches = Enumerable.Range(
                    0, renderer.sharedMesh.blendShapeCount)
                .Select(renderer.sharedMesh.GetBlendShapeName)
                .Where(name => name.EndsWith(
                    shapeSuffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Expected one {rendererName} BlendShape ending in " +
                    $"{shapeSuffix}, found {matches.Length}");

            var curve = new AnimationCurve(samples
                .Select(sample => new Keyframe(
                    sample.frame / 30f, sample.value))
                .ToArray());
            for (int key = 0; key < curve.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, key, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, key, AnimationUtility.TangentMode.Linear);
            }
            string path = AnimationUtility.CalculateTransformPath(
                renderer.transform, prefab.transform);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    path, typeof(SkinnedMeshRenderer),
                    "blendShape." + matches[0]),
                curve);
            shapes.Add(matches[0]);
        }

        static string Leaf(string path)
        {
            int separator = path.LastIndexOf('/');
            return separator < 0 ? path : path.Substring(separator + 1);
        }

        static Transform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        static bool IsHair(string name)
        {
            return name == "HairRoot" ||
                name == "Lengyue_HairRig_v002" ||
                name.StartsWith("H_", StringComparison.Ordinal) ||
                name.StartsWith("Hair_", StringComparison.Ordinal) ||
                name.StartsWith("Backhair_", StringComparison.Ordinal);
        }

        static bool IsExcluded(string name)
        {
            return IsHair(name) ||
                name.StartsWith("Skirt_", StringComparison.Ordinal) ||
                name.StartsWith("OuterSkirt_", StringComparison.Ordinal) ||
                name.StartsWith("CTRL_", StringComparison.Ordinal) ||
                name.StartsWith("DRV_", StringComparison.Ordinal) ||
                name.StartsWith("MCH_", StringComparison.Ordinal) ||
                name.EndsWith("_IK", StringComparison.Ordinal) ||
                name.Contains("_IK.") ||
                name.EndsWith("_Fulcrum", StringComparison.Ordinal);
        }
    }
}
