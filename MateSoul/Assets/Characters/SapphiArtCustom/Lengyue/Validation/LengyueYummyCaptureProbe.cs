using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Animation;
using MateSoul.Cloth;
using MateSoul.Hair;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MateSoul.Validation
{
    [DefaultExecutionOrder(-20000)]
    public sealed class LengyueYummyCaptureProbe : MonoBehaviour
    {
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

        public GameObject character;
        public AnimationClip clip;
        public string outputDirectory;
        public string reportDirectory;
        public int frameCount = 173;
        public int frameRate = 30;
        public int captureWidth = 1920;
        public int captureHeight = 1080;
        public int captureMsaa = 4;
        public int captureSupersample = 2;

        int meshRenderingErrors, runtimeErrors;

        #region debug-point A-C:hair-contact-observations
        [Serializable] sealed class HairDebugEvent
        {
            public string sessionId = "hair-scalp-contact";
            public string runId = "post-fix";
            public string hypothesisId = "A,B,C";
            public string location = "LengyueYummyCaptureProbe";
            public string msg = "[DEBUG] hair mesh and simulation";
            public int frame;
            public Vector3[] vertices;
            public int[] triangles, boneIndices;
            public float[] weights, depthOffsets;
            public Vector3[] correctedVertices;
            public int boundFineStrands, boundFineVertices;
            public double surfaceContactMilliseconds;
            public string[] boneNames;
            public Matrix4x4[] skinMatrices;
            public Vector3[] joints, tails, colliderCenters;
            public float[] colliderRadii;
            public bool[] active;
            public float maxResidual, lateralDrive;
        }
        #endregion

        #region debug-point sleeve-A-D:event
        [Serializable] sealed class SleeveDebugEvent
        {
            public string sessionId = "sleeve-soft-dynamics";
            public string runId = "post-fix";
            public string hypothesisId = "A,B,C,D";
            public string location = "LengyueYummyCaptureProbe";
            public string msg = "[DEBUG] sleeve geometry and motion";
            public int frame;
            public string[] components, boneNames;
            public Vector3[] vertices, bakedVertices;
            public Vector3[] particles, animatedParticles;
            public float maximumOffset, minimumClearance;
            public int contacts;
            public int qualitySkinInfluences = (int)QualitySettings.skinWeights;
            public double simulationMs;
            public int[] vertexIds, parts, triangles, boneIndices;
            public float[] weights;
            public Matrix4x4[] skinMatrices, boneWorld;
            public Matrix4x4 meshWorld;
            public string packedVertices;
        }
        #endregion

        #region debug-point A-E:render-material-event
        [Serializable] sealed class RenderMaterialDebugEvent
        {
            public string sessionId = "v10-shadow-fabric";
            public string runId = "post-fix";
            public string hypothesisId = "A,B,C,D,E";
            public string location = "LengyueYummyCaptureProbe";
            public string msg = "[DEBUG] shadow and garment material baseline";
            public string qualityName, renderPipeline, graphicsDevice;
            public string lightShadows, shadowResolution, shadowProjection;
            public string materialName, shaderName, atlasName, atlasFilterMode;
            public int qualityLevel, qualityAntiAliasing, shadowCascades;
            public int captureMsaa, captureSupersample, lightShadowCustomResolution;
            public int atlasWidth, atlasHeight, atlasMipCount, atlasAniso;
            public float shadowDistance, lightShadowStrength;
            public float lightShadowBias, lightShadowNormalBias, lightShadowNearPlane;
            public float shadowSoftness, shadowThreshold, receiveShadows;
            public float skinShadowSoftness;
            public float diffuseWrap, unlitBlend, indirectStrength;
            public float specularIntensity, specularSoftness, specularThreshold;
            public float rimIntensity, rimPower;
            public float fabricDetailScale, fabricAlbedoStrength;
            public float fabricNormalStrength, fabricSheenIntensity, fabricSheenPower;
            public bool cameraAllowMsaa;
        }
        #endregion

        [Serializable]
        sealed class Report
        {
            public string unity;
            public string prefab;
            public string clip;
            public bool playMode;
            public bool toonVerified;
            public bool hairInitialized;
            public bool skirtInitialized;
            public bool sleeveInitialized;
            public int collarVertices;
            public int sleeveDrivenVertices;
            public int sleeveSkinInfluences;
            public float sleeveMaxOffsetM, sleeveMinimumClearanceM;
            public double sleeveAverageUpdateMs;
            public float sleeveGravityDropM, sleeveMotionLagM, sleeveSettledErrorM;
            public bool headPoseGuardDisabled;
            public int frameRate;
            public int frames;
            public int captureWidth;
            public int captureHeight;
            public int captureMsaa;
            public int captureSupersample;
            public bool cameraOrthographic;
            public float cameraOrthographicSize;
            public float cameraForwardAlignmentDeg;
            public Vector3 cameraPosition;
            public int rendererCount;
            public int hairConfiguredBones;
            public int hairSimulatedJoints;
            public int hairSubsteps;
            public int meshRenderingErrors;
            public int runtimeErrors;
            public int fineStrandsBound;
            public int fineVerticesBound;
            public int surfaceContactFrames;
            public string hairQuality;
            public float hairMaxBendLimitDeg;
            public float hairMaxChainBendLimitDeg;
            public float skirtResponseMultiplier;
            public float skirtMaxResponseDeg;
            public float skirtGravityResponse;
            public float skirtInertiaResponse;
            public float skirtSpringFrequency;
            public float skirtDampingRatio;
            public float skirtMaxSecondaryLimitDeg;
            public float hairMaxBoneAngleDeg;
            public float hairMaxChainAngleDeg;
            public float hairMaxLateralAngleDeg;
            public float hairMaxDepthAngleDeg;
            public float hairMaxLateralDrive;
            public float hairTipRelativeMovementM;
            public float hairTipLateralMovementM;
            public float hairGravityAlignedLateralM;
            public float positiveGravityTiltResponseM;
            public float negativeGravityTiltResponseM;
            public float fringeTipRelativeMovementM;
            public float fringeTipLateralMovementM;
            public float fringeTipDepthMovementM;
            public float leftSideTipRelativeMovementM;
            public float leftSideTipLateralMovementM;
            public float leftSideTipDepthMovementM;
            public float rightSideTipRelativeMovementM;
            public float rightSideTipLateralMovementM;
            public float rightSideTipDepthMovementM;
            public float skirtMaxBoneAngleDeg;
            public float skirtMaxLeftFlexDeg;
            public float skirtMaxRightFlexDeg;
            public float skirtMaxSecondaryAngleDeg;
            public float skirtMaxLateralSecondaryAngleDeg;
            public float skirtMaxGravityTargetDeg;
            public float skirtMaxInertiaTargetDeg;
            public float skirtHemMovementM;
            public float skirtRestHemHalfWidthM;
            public float skirtDrapeBaselineHalfWidthM;
            public float skirtDrapedHemHalfWidthM;
            public float skirtMinimumHemHalfWidthM;
            public float skirtMaximumHemHalfWidthM;
            public float skirtRestHemDepthRadiusM;
            public float skirtDrapeBaselineDepthRadiusM;
            public float skirtDrapedHemDepthRadiusM;
            public float skirtMinimumHemDepthRadiusM;
            public float skirtMaximumHemDepthRadiusM;
            public float skirtMaxDrapeWeight;
            public float skirtMaxAvoidanceWeight;
            public float skirtPositiveGravityTiltResponseDeg;
            public float skirtNegativeGravityTiltResponseDeg;
            public float leftHandMovementM;
            public float rightHandMovementM;
            public float leftEyeRotationMovementDeg;
            public float rightEyeRotationMovementDeg;
            public float eyeCloseLeftMax;
            public float eyeCloseRightMax;
            public float eyeWideLeftMax;
            public float eyeWideRightMax;
            public float browSadLeftMax;
            public float browSadRightMax;
            public float cheekPuffMax;
            public float jawOpenMax;
            public float mouthEMax;
            public float mouthShapeMax;
            public int eyesFullyOpenFrame;
            public int speechOpenSamples;
            public int speechClosedSamples;
            public float headNeckDistanceRatioDeviation;
            public bool passed;
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors++;
            if (message.Contains("SkinnedMeshRenderer: Rendering stopped"))
                meshRenderingErrors++;
        }

        static Transform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        static Vector3 CalibratedHeadLocal(
            Transform head, Quaternion restOffset,
            Vector3 worldPosition)
        {
            Quaternion frame =
                head.rotation * Quaternion.Inverse(restOffset);
            return Quaternion.Inverse(frame) *
                (worldPosition - head.position);
        }

        static int BlendShapeIndex(
            SkinnedMeshRenderer renderer, string suffix)
        {
            int found = -1;
            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
            {
                string name = renderer.sharedMesh.GetBlendShapeName(i);
                if (!name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                if (found >= 0)
                    throw new InvalidOperationException(
                        "Ambiguous BlendShape suffix: " + suffix);
                found = i;
            }
            if (found < 0)
                throw new InvalidOperationException(
                    "Missing BlendShape suffix: " + suffix);
            return found;
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

        IEnumerator Start()
        {
            if (!character || !clip)
                throw new InvalidOperationException("Yummy capture is not configured");

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Time.captureFramerate = frameRate;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != gameObject.scene)
                    foreach (var root in scene.GetRootGameObjects())
                        root.SetActive(false);
            }

            yield return null;

            var animator = character.GetComponent<Animator>();
            var headGuard = character.GetComponent<HeadPoseGuard>();
            var skirt = character.GetComponent<SkirtPoseGuard>();
            var hair = character.GetComponentInChildren<HairSpringPhysics>(true);
            if (!animator || !headGuard || !skirt || !hair)
                throw new InvalidOperationException("Canonical runtime components are missing");

            animator.enabled = false;
            headGuard.enabled = false;
            skirt.enabled = true;
            skirt.BindByName();
            skirt.Initialize();
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
            ConfigureEnhancedHair(hair);
            hair.Initialize();
            var surfaceContact = hair.GetComponent<LengyueHairSurfaceContact>();
            surfaceContact.Initialize();
            var sleeves = character.GetComponent<LengyueSleeveCloth>();
            if (!sleeves) sleeves = character.AddComponent<LengyueSleeveCloth>();
            sleeves.Initialize();

            #region debug-point A-E:render-material-baseline
            var renderGarment = character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "LavenderCasual_Garments");
            var renderMaterial = renderGarment.sharedMaterial;
            var renderSkinMaterial = character
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .First(m => m && m.name == "Lengyue_Toon_SkinCoverage");
            var renderAtlas = renderMaterial.GetTexture("_BaseMap") as Texture2D;
            var renderLight = FindObjectsOfType<Light>().First(l => l.type == LightType.Directional);
            var renderCamera = GetComponent<Camera>();
            string renderDebugUrl = File.ReadAllLines(Path.Combine(Path.GetFullPath(reportDirectory + "/../../../../.."), ".dbg/v10-shadow-fabric.env")).First(s => s.StartsWith("DEBUG_SERVER_URL=")).Substring("DEBUG_SERVER_URL=".Length);
            using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(renderDebugUrl, JsonUtility.ToJson(new RenderMaterialDebugEvent { qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()], qualityLevel = QualitySettings.GetQualityLevel(), renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "BuiltIn", graphicsDevice = SystemInfo.graphicsDeviceName, qualityAntiAliasing = QualitySettings.antiAliasing, shadowCascades = QualitySettings.shadowCascades, shadowDistance = QualitySettings.shadowDistance, shadowResolution = QualitySettings.shadowResolution.ToString(), shadowProjection = QualitySettings.shadowProjection.ToString(), captureMsaa = captureMsaa, captureSupersample = captureSupersample, cameraAllowMsaa = renderCamera.allowMSAA, lightShadows = renderLight.shadows.ToString(), lightShadowCustomResolution = renderLight.shadowCustomResolution, lightShadowStrength = renderLight.shadowStrength, lightShadowBias = renderLight.shadowBias, lightShadowNormalBias = renderLight.shadowNormalBias, lightShadowNearPlane = renderLight.shadowNearPlane, materialName = renderMaterial.name, shaderName = renderMaterial.shader.name, atlasName = renderAtlas ? renderAtlas.name : "null", atlasWidth = renderAtlas ? renderAtlas.width : 0, atlasHeight = renderAtlas ? renderAtlas.height : 0, atlasMipCount = renderAtlas ? renderAtlas.mipmapCount : 0, atlasFilterMode = renderAtlas ? renderAtlas.filterMode.ToString() : "null", atlasAniso = renderAtlas ? renderAtlas.anisoLevel : 0, shadowSoftness = renderMaterial.GetFloat("_ShadowSoftness"), skinShadowSoftness = renderSkinMaterial.GetFloat("_ShadowSoftness"), shadowThreshold = renderMaterial.GetFloat("_ShadowThreshold"), receiveShadows = renderMaterial.GetFloat("_ReceiveShadows"), diffuseWrap = renderMaterial.GetFloat("_DiffuseWrap"), unlitBlend = renderMaterial.GetFloat("_UnlitBlend"), indirectStrength = renderMaterial.GetFloat("_IndirectStrength"), specularIntensity = renderMaterial.GetFloat("_SpecularIntensity"), specularSoftness = renderMaterial.GetFloat("_SpecularSoftness"), specularThreshold = renderMaterial.GetFloat("_SpecularThreshold"), rimIntensity = renderMaterial.GetFloat("_RimIntensity"), rimPower = renderMaterial.GetFloat("_RimPower"), fabricDetailScale = renderMaterial.GetFloat("_FabricDetailScale"), fabricAlbedoStrength = renderMaterial.GetFloat("_FabricAlbedoStrength"), fabricNormalStrength = renderMaterial.GetFloat("_FabricNormalStrength"), fabricSheenIntensity = renderMaterial.GetFloat("_FabricSheenIntensity"), fabricSheenPower = renderMaterial.GetFloat("_FabricSheenPower") })); }
            #endregion

            #region debug-point sleeve-A-D:baseline
            var sleeveRenderer = character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "LavenderCasual_Garments");
            sleeveRenderer.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            var sleeveMesh = sleeves.SourceMesh;
            var sleeveParts = sleeveMesh.uv2.Select(uv => Mathf.RoundToInt(uv.x)).ToArray();
            var sleeveIds = Enumerable.Range(0, sleeveMesh.vertexCount).Where(i => sleeveParts[i] == 3 || sleeveParts[i] == 5).ToArray();
            var sleeveMap = sleeveIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
            var sleeveAllTriangles = sleeveMesh.triangles;
            var sleeveTriangles = Enumerable.Range(0, sleeveAllTriangles.Length / 3).Where(t => sleeveMap.ContainsKey(sleeveAllTriangles[t * 3]) && sleeveMap.ContainsKey(sleeveAllTriangles[t * 3 + 1]) && sleeveMap.ContainsKey(sleeveAllTriangles[t * 3 + 2])).SelectMany(t => new[] { sleeveMap[sleeveAllTriangles[t * 3]], sleeveMap[sleeveAllTriangles[t * 3 + 1]], sleeveMap[sleeveAllTriangles[t * 3 + 2]] }).ToArray();
            var sleeveRest = sleeveMesh.vertices;
            var sleeveBind = sleeveMesh.bindposes;
            var sleeveAllWeights = sleeveMesh.boneWeights;
            var sleeveWeights = sleeveIds.Select(i => sleeveAllWeights[i]).ToArray();
            var sleeveBaked = new Mesh();
            string sleeveDebugUrl = File.ReadAllLines(Path.Combine(Path.GetFullPath(reportDirectory + "/../../../../.."), ".dbg/sleeve-soft-dynamics.env")).First(s => s.StartsWith("DEBUG_SERVER_URL=")).Substring("DEBUG_SERVER_URL=".Length);
            using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(sleeveDebugUrl, JsonUtility.ToJson(new SleeveDebugEvent { frame = -1, vertexIds = sleeveIds, vertices = sleeveIds.Select(i => sleeveRest[i]).ToArray(), parts = sleeveIds.Select(i => sleeveParts[i]).ToArray(), triangles = sleeveTriangles, components = character.GetComponentsInChildren<Component>(true).Where(c => c is MonoBehaviour || c is UnityEngine.Cloth).Select(c => c.GetType().FullName + ":" + c.name + ":" + (c is Behaviour behaviour ? behaviour.enabled.ToString() : c.gameObject.activeInHierarchy.ToString())).ToArray(), boneNames = sleeveRenderer.bones.Select(b => b.name).ToArray(), boneIndices = sleeveWeights.SelectMany(w => new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }).ToArray(), weights = sleeveWeights.SelectMany(w => new[] { w.weight0, w.weight1, w.weight2, w.weight3 }).ToArray(), meshWorld = sleeveRenderer.transform.localToWorldMatrix, skinMatrices = sleeveRenderer.bones.Select((b,i) => sleeveRenderer.transform.worldToLocalMatrix * b.localToWorldMatrix * sleeveBind[i]).ToArray(), boneWorld = sleeveRenderer.bones.Select(b => b.localToWorldMatrix).ToArray() })); }
            #endregion

            #region debug-point A-C:rest-geometry
            var debugContact = hair.GetComponent<LengyueHairSurfaceContact>();
            debugContact.Initialize();
            var debugSkin = hair.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            var debugMesh = debugSkin.sharedMesh;
            var debugBind = debugMesh.bindposes;
            var debugWeights = debugMesh.boneWeights;
            string debugUrl = File.ReadAllLines(Path.Combine(Path.GetFullPath(reportDirectory + "/../../../../.."), ".dbg/hair-scalp-contact.env")).First(s => s.StartsWith("DEBUG_SERVER_URL=")).Substring("DEBUG_SERVER_URL=".Length);
            using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(debugUrl, JsonUtility.ToJson(new HairDebugEvent { frame = -1, vertices = debugContact.RestVertices, triangles = debugMesh.triangles, boneNames = debugSkin.bones.Select(b => b.name).ToArray(), boneIndices = debugWeights.SelectMany(w => new[] {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3}).ToArray(), weights = debugWeights.SelectMany(w => new[] {w.weight0,w.weight1,w.weight2,w.weight3}).ToArray(), skinMatrices = debugSkin.bones.Select((b,i) => debugSkin.transform.worldToLocalMatrix * b.localToWorldMatrix * debugBind[i]).ToArray(), joints = hair.bones.Select(b => debugSkin.transform.InverseTransformPoint(b.transform.position)).ToArray(), tails = hair.bones.Select(b => debugSkin.transform.InverseTransformPoint(b.transform.TransformPoint(b.tailLocal))).ToArray(), colliderCenters = hair.colliders.Select(c => debugSkin.transform.InverseTransformPoint(c.transform.TransformPoint(c.offset))).ToArray(), colliderRadii = hair.colliders.Select(c => c.radius * c.transform.lossyScale.x / debugSkin.transform.lossyScale.x).ToArray(), active = hair.bones.Select(b => b.full).ToArray(), boundFineStrands = debugContact.BoundFineStrands, boundFineVertices = debugContact.BoundFineVertices })); }
            #endregion

            var skirtBones = character.GetComponentsInChildren<Transform>(true)
                .Where(item =>
                    item.name.StartsWith(
                        "Skirt_", StringComparison.Ordinal) ||
                    item.name.StartsWith(
                        "OuterSkirt_", StringComparison.Ordinal))
                .ToArray();
            var initialSkirtRotations = skirtBones.ToDictionary(
                item => item, item => item.localRotation);
            var hairTip = Find(character, "H_Back_10_08")
                ?? Find(character, "H_SideL_01_08")
                ?? hair.bones.Last(item => item.transform).transform;
            var fringeTip = Find(character, "H_Fringe_04_06");
            var leftSideTip = Find(character, "H_SideL_01_08");
            var rightSideTip = Find(character, "H_SideR_16_08");
            var skirtHemBones = new[] {
                Find(character, "OuterSkirt_Front_02_L"),
                Find(character, "OuterSkirt_Front_02_R"),
                Find(character, "OuterSkirt_Side_02_L"),
                Find(character, "OuterSkirt_Side_02_R"),
                Find(character, "OuterSkirt_Back_03_L"),
                Find(character, "OuterSkirt_Back_03_R")
            };
            var head = Find(character, "Head 1");
            var neck = Find(character, "Neck");
            var leftHand = Find(character, "Hand_L");
            var rightHand = Find(character, "Hand_R");
            var leftEye = Find(character, "Eye_L");
            var rightEye = Find(character, "Eye_R");
            if (!hairTip || !fringeTip || !leftSideTip ||
                !rightSideTip || !head || !neck || !leftHand ||
                !rightHand || !leftEye || !rightEye ||
                skirtHemBones.Any(item => !item))
                throw new InvalidOperationException(
                    "Motion validation landmarks are missing");
            var face = character
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(item => item.name == "face");
            var brow = character
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(item => item.name == "brow");
            int eyeCloseLeft = BlendShapeIndex(
                face, "face_eye_close_L_BS");
            int eyeCloseRight = BlendShapeIndex(
                face, "face_eye_close_R_BS");
            int eyeWideLeft = BlendShapeIndex(
                face, "face_eye_wide_L_BS");
            int eyeWideRight = BlendShapeIndex(
                face, "face_eye_wide_R_BS");
            int browSadLeft = BlendShapeIndex(
                brow, "brow_sad_L");
            int browSadRight = BlendShapeIndex(
                brow, "brow_sad_R");
            int mouthO = BlendShapeIndex(face, "face_O_BS");
            int mouthSad = BlendShapeIndex(face, "face_sad_BS");
            int mouthSmile = BlendShapeIndex(
                face, "face_smile_BS");
            int cheekPuff = BlendShapeIndex(
                face, "face_Puff_BS");
            int jawOpen = BlendShapeIndex(
                face, "face_jaw_open_BS");
            int mouthE = BlendShapeIndex(
                face, "face_E_mouth_BS");

            clip.SampleAnimation(character, 0);
            Quaternion initialLeftEye = leftEye.localRotation;
            Quaternion initialRightEye = rightEye.localRotation;
            Quaternion headRestOffset =
                Quaternion.Inverse(character.transform.rotation) *
                head.rotation;
            skirt.ResetDynamics();
            hair.ResetSimulation();
            sleeves.ResetSimulation();
            for (int i = 0; i < 6; i++)
                yield return new WaitForEndOfFrame();

            string frames = Path.Combine(outputDirectory, "frames");
            if (Directory.Exists(frames))
                Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            Directory.CreateDirectory(reportDirectory);
            File.Delete(Path.Combine(reportDirectory, "unity_capture_error.txt"));

            var camera = GetComponent<Camera>();
            int renderWidth = captureWidth * captureSupersample;
            int renderHeight = captureHeight * captureSupersample;
            var target = new RenderTexture(
                renderWidth, renderHeight, 24,
                RenderTextureFormat.ARGB32) {
                antiAliasing = captureMsaa
            };
            target.Create();
            var resolved = new RenderTexture(
                captureWidth, captureHeight, 0,
                RenderTextureFormat.ARGB32);
            resolved.Create();
            var pixels = new Texture2D(
                captureWidth, captureHeight,
                TextureFormat.RGB24, false);
            camera.targetTexture = target;

            int simulatedJoints = 0;
            int surfaceContactFrames = 0;
            int substeps = 0;
            float hairMaxAngle = 0;
            float hairMaxChainAngle = 0;
            float hairMaxLateralAngle = 0;
            float hairMaxDepthAngle = 0;
            float hairMaxLateralDrive = 0;
            float skirtMaxAngle = 0;
            float skirtMaxLeft = 0;
            float skirtMaxRight = 0;
            float skirtMaxSecondary = 0;
            float skirtMaxLateralSecondary = 0;
            float skirtMaxGravityTarget = 0;
            float skirtMaxInertiaTarget = 0;
            float skirtDrapeBaselineHalfWidth = 0;
            float skirtDrapedHemHalfWidth = 0;
            float skirtMinimumHemHalfWidth = float.MaxValue;
            float skirtMaximumHemHalfWidth = 0;
            float skirtDrapeBaselineDepthRadius = 0;
            float skirtDrapedHemDepthRadius = 0;
            float skirtMinimumHemDepthRadius = float.MaxValue;
            float skirtMaximumHemDepthRadius = 0;
            float skirtMaxDrapeWeight = 0;
            float skirtMaxAvoidanceWeight = 0;
            float leftEyeMovement = 0;
            float rightEyeMovement = 0;
            float eyeCloseLeftMax = 0;
            float eyeCloseRightMax = 0;
            float eyeWideLeftMax = 0;
            float eyeWideRightMax = 0;
            float browSadLeftMax = 0;
            float browSadRightMax = 0;
            float cheekPuffMax = 0;
            float jawOpenMax = 0;
            float mouthEMax = 0;
            float mouthShapeMax = 0;
            int eyesFullyOpenFrame = -1;
            int speechOpenSamples = 0;
            int speechClosedSamples = 0;
            Vector3 tipMinimum = CalibratedHeadLocal(
                head, headRestOffset, hairTip.position);
            Vector3 tipMaximum = tipMinimum;
            Vector3 tipRestLocal = tipMinimum;
            float hairGravityAlignedLateral = 0;
            Vector3 fringeMinimum =
                CalibratedHeadLocal(
                    head, headRestOffset, fringeTip.position);
            Vector3 fringeMaximum = fringeMinimum;
            Vector3 leftSideMinimum =
                CalibratedHeadLocal(
                    head, headRestOffset, leftSideTip.position);
            Vector3 leftSideMaximum = leftSideMinimum;
            Vector3 rightSideMinimum =
                CalibratedHeadLocal(
                    head, headRestOffset, rightSideTip.position);
            Vector3 rightSideMaximum = rightSideMinimum;
            Vector3[] skirtHemMinimum = skirtHemBones
                .Select(item =>
                    skirt.hips.InverseTransformPoint(item.position))
                .ToArray();
            Vector3[] skirtHemMaximum =
                (Vector3[])skirtHemMinimum.Clone();
            Vector3 leftHandMinimum = leftHand.position;
            Vector3 leftHandMaximum = leftHand.position;
            Vector3 rightHandMinimum = rightHand.position;
            Vector3 rightHandMaximum = rightHand.position;
            float restHeadNeckDistance =
                Vector3.Distance(head.position, neck.position);
            float maxHeadNeckDistanceDeviation = 0;
            float sleeveMaxOffset = 0, sleeveMinimumClearance = float.MaxValue;
            double sleeveUpdateMs = 0;

            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = Mathf.Min(clip.length, frame / (float)frameRate);
                clip.SampleAnimation(character, time);
                yield return new WaitForEndOfFrame();
                if (surfaceContact.LastUpdateMilliseconds > 0)
                    surfaceContactFrames++;
                sleeveMaxOffset = Mathf.Max(sleeveMaxOffset, sleeves.MaximumOffset);
                sleeveMinimumClearance = Mathf.Min(sleeveMinimumClearance, sleeves.MinimumClearance);
                sleeveUpdateMs += sleeves.LastUpdateMilliseconds;

                #region debug-point sleeve-A-D:frame
                sleeveRenderer.BakeMesh(sleeveBaked);
                var sleeveBakedVertices = sleeveBaked.vertices;
                using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(sleeveDebugUrl, JsonUtility.ToJson(new SleeveDebugEvent { frame = frame, bakedVertices = sleeveIds.Select(i => sleeveBakedVertices[i]).ToArray(), meshWorld = sleeveRenderer.transform.localToWorldMatrix, skinMatrices = sleeveRenderer.bones.Select((b,i) => sleeveRenderer.transform.worldToLocalMatrix * b.localToWorldMatrix * sleeveBind[i]).ToArray(), boneWorld = sleeveRenderer.bones.Select(b => b.localToWorldMatrix).ToArray(), particles = sleeves.ParticlePositions, animatedParticles = sleeves.AnimatedPositions, maximumOffset = sleeves.MaximumOffset, minimumClearance = sleeves.MinimumClearance, contacts = sleeves.ContactCount, simulationMs = sleeves.LastUpdateMilliseconds })); }
                #endregion

                #region debug-point A-C:solved-frame
                using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(debugUrl, JsonUtility.ToJson(new HairDebugEvent { frame = frame, skinMatrices = debugSkin.bones.Select((b,i) => debugSkin.transform.worldToLocalMatrix * b.localToWorldMatrix * debugBind[i]).ToArray(), joints = hair.bones.Select(b => debugSkin.transform.InverseTransformPoint(b.transform.position)).ToArray(), tails = hair.bones.Select(b => debugSkin.transform.InverseTransformPoint(b.transform.TransformPoint(b.tailLocal))).ToArray(), maxResidual = hair.bones.Where(b => b.full).SelectMany(b => hair.colliders.Select(c => Mathf.Max(0, (c.radius + hair.particleRadius) * c.transform.lossyScale.x - Vector3.Distance(b.transform.TransformPoint(b.tailLocal), c.transform.TransformPoint(c.offset))))).DefaultIfEmpty().Max(), lateralDrive = hair.LateralDrive, depthOffsets = debugContact.DepthOffsets, correctedVertices = frame % 30 == 0 ? debugMesh.vertices : null, surfaceContactMilliseconds = debugContact.LastUpdateMilliseconds })); }
                #endregion

                simulatedJoints = Mathf.Max(
                    simulatedJoints, hair.LastSimulatedJoints);
                substeps += hair.LastSubsteps;
                hairMaxAngle = Mathf.Max(hairMaxAngle, hair.MaxBoneAngle);
                hairMaxChainAngle = Mathf.Max(
                    hairMaxChainAngle, hair.MaxChainAngle);
                hairMaxLateralAngle = Mathf.Max(
                    hairMaxLateralAngle, hair.MaxLateralAngle);
                hairMaxDepthAngle = Mathf.Max(
                    hairMaxDepthAngle, hair.MaxDepthAngle);
                hairMaxLateralDrive = Mathf.Max(
                    hairMaxLateralDrive,
                    Mathf.Abs(hair.LateralDrive));
                skirtMaxLeft = Mathf.Max(
                    skirtMaxLeft, Mathf.Abs(skirt.LastLeftFlex));
                skirtMaxRight = Mathf.Max(
                    skirtMaxRight, Mathf.Abs(skirt.LastRightFlex));
                skirtMaxSecondary = Mathf.Max(
                    skirtMaxSecondary,
                    skirt.SecondaryAngles.magnitude);
                skirtMaxLateralSecondary = Mathf.Max(
                    skirtMaxLateralSecondary,
                    Mathf.Abs(skirt.SecondaryAngles.y));
                skirtMaxGravityTarget = Mathf.Max(
                    skirtMaxGravityTarget,
                    skirt.LastGravityTargetAngle);
                skirtMaxInertiaTarget = Mathf.Max(
                    skirtMaxInertiaTarget,
                    skirt.LastInertiaTargetAngle);
                if (skirt.LastDrapeWeight >=
                    skirtMaxDrapeWeight)
                {
                    skirtDrapeBaselineHalfWidth =
                        skirt.LastBaselineHemHalfWidth;
                    skirtDrapedHemHalfWidth =
                        skirt.LastDrapedHemHalfWidth;
                    skirtDrapeBaselineDepthRadius =
                        skirt.LastBaselineHemDepthRadius;
                    skirtDrapedHemDepthRadius =
                        skirt.LastDrapedHemDepthRadius;
                }
                skirtMinimumHemHalfWidth = Mathf.Min(
                    skirtMinimumHemHalfWidth,
                    skirt.CurrentHemHalfWidth);
                skirtMaximumHemHalfWidth = Mathf.Max(
                    skirtMaximumHemHalfWidth,
                    skirt.CurrentHemHalfWidth);
                skirtMinimumHemDepthRadius = Mathf.Min(
                    skirtMinimumHemDepthRadius,
                    skirt.CurrentHemDepthRadius);
                skirtMaximumHemDepthRadius = Mathf.Max(
                    skirtMaximumHemDepthRadius,
                    skirt.CurrentHemDepthRadius);
                skirtMaxDrapeWeight = Mathf.Max(
                    skirtMaxDrapeWeight,
                    skirt.LastDrapeWeight);
                skirtMaxAvoidanceWeight = Mathf.Max(
                    skirtMaxAvoidanceWeight,
                    skirt.LastAvoidanceWeight);
                leftEyeMovement = Mathf.Max(
                    leftEyeMovement,
                    Quaternion.Angle(
                        initialLeftEye, leftEye.localRotation));
                rightEyeMovement = Mathf.Max(
                    rightEyeMovement,
                    Quaternion.Angle(
                        initialRightEye, rightEye.localRotation));
                float closeLeftWeight =
                    face.GetBlendShapeWeight(eyeCloseLeft);
                float closeRightWeight =
                    face.GetBlendShapeWeight(eyeCloseRight);
                float wideLeftWeight =
                    face.GetBlendShapeWeight(eyeWideLeft);
                float wideRightWeight =
                    face.GetBlendShapeWeight(eyeWideRight);
                eyeCloseLeftMax = Mathf.Max(
                    eyeCloseLeftMax, closeLeftWeight);
                eyeCloseRightMax = Mathf.Max(
                    eyeCloseRightMax, closeRightWeight);
                eyeWideLeftMax = Mathf.Max(
                    eyeWideLeftMax, wideLeftWeight);
                eyeWideRightMax = Mathf.Max(
                    eyeWideRightMax, wideRightWeight);
                if (frame >= 90 && eyesFullyOpenFrame < 0 &&
                    closeLeftWeight < 5f && closeRightWeight < 5f &&
                    wideLeftWeight > 85f && wideRightWeight > 85f)
                {
                    eyesFullyOpenFrame = frame;
                }
                browSadLeftMax = Mathf.Max(
                    browSadLeftMax,
                    brow.GetBlendShapeWeight(browSadLeft));
                browSadRightMax = Mathf.Max(
                    browSadRightMax,
                    brow.GetBlendShapeWeight(browSadRight));
                float puffWeight =
                    face.GetBlendShapeWeight(cheekPuff);
                float jawWeight =
                    face.GetBlendShapeWeight(jawOpen);
                float mouthEWeight =
                    face.GetBlendShapeWeight(mouthE);
                cheekPuffMax = Mathf.Max(
                    cheekPuffMax, puffWeight);
                jawOpenMax = Mathf.Max(
                    jawOpenMax, jawWeight);
                mouthEMax = Mathf.Max(
                    mouthEMax, mouthEWeight);
                mouthShapeMax = Mathf.Max(
                    mouthShapeMax,
                    face.GetBlendShapeWeight(mouthO),
                    face.GetBlendShapeWeight(mouthSad),
                    face.GetBlendShapeWeight(mouthSmile),
                    puffWeight, jawWeight, mouthEWeight);
                if ((frame == 148 || frame == 157 || frame == 166) &&
                    jawWeight > 25f)
                {
                    speechOpenSamples++;
                }
                if ((frame == 152 || frame == 161 || frame == 170) &&
                    jawWeight < 1f)
                {
                    speechClosedSamples++;
                }
                foreach (var bone in skirtBones)
                    skirtMaxAngle = Mathf.Max(skirtMaxAngle,
                        Quaternion.Angle(
                            initialSkirtRotations[bone], bone.localRotation));
                Vector3 relativeTip = CalibratedHeadLocal(
                    head, headRestOffset, hairTip.position);
                tipMinimum = Vector3.Min(tipMinimum, relativeTip);
                tipMaximum = Vector3.Max(tipMaximum, relativeTip);
                Quaternion headFrame =
                    head.rotation *
                    Quaternion.Inverse(headRestOffset);
                Vector3 localGravity =
                    Quaternion.Inverse(headFrame) *
                    Vector3.down;
                if (Mathf.Abs(localGravity.x) > .15f)
                {
                    hairGravityAlignedLateral = Mathf.Max(
                        hairGravityAlignedLateral,
                        (relativeTip.x - tipRestLocal.x) *
                        Mathf.Sign(localGravity.x));
                }
                Vector3 relativeFringe =
                    CalibratedHeadLocal(
                        head, headRestOffset, fringeTip.position);
                fringeMinimum = Vector3.Min(
                    fringeMinimum, relativeFringe);
                fringeMaximum = Vector3.Max(
                    fringeMaximum, relativeFringe);
                Vector3 relativeLeftSide =
                    CalibratedHeadLocal(
                        head, headRestOffset, leftSideTip.position);
                leftSideMinimum = Vector3.Min(
                    leftSideMinimum, relativeLeftSide);
                leftSideMaximum = Vector3.Max(
                    leftSideMaximum, relativeLeftSide);
                Vector3 relativeRightSide =
                    CalibratedHeadLocal(
                        head, headRestOffset, rightSideTip.position);
                rightSideMinimum = Vector3.Min(
                    rightSideMinimum, relativeRightSide);
                rightSideMaximum = Vector3.Max(
                    rightSideMaximum, relativeRightSide);
                for (int i = 0; i < skirtHemBones.Length; i++)
                {
                    Vector3 localHem = skirt.hips.InverseTransformPoint(
                        skirtHemBones[i].position);
                    skirtHemMinimum[i] = Vector3.Min(
                        skirtHemMinimum[i], localHem);
                    skirtHemMaximum[i] = Vector3.Max(
                        skirtHemMaximum[i], localHem);
                }
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

                camera.Render();
                #region debug-point sleeve-C:gpu-surface
                if (frame == 0 || frame == 85 || frame == 172)
                {
                    using (var buffer = sleeveRenderer.GetVertexBuffer())
                    {
                        var data = new float[buffer.count * buffer.stride / 4];
                        buffer.GetData(data);
                        int stride = data.Length / sleeveRenderer.sharedMesh.vertexCount;
                        var gpuPoints = sleeveIds.Select(i => new Vector3(data[i * stride], data[i * stride + 1], data[i * stride + 2])).ToArray();
                        var inputVertices = sleeveRenderer.sharedMesh.vertices;
                        using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(sleeveDebugUrl, JsonUtility.ToJson(new SleeveDebugEvent { frame = 2000 + frame, msg = "[DEBUG] actual GPU surface; contacts=stride; maximumOffset=renderer quality", bakedVertices = gpuPoints, vertices = sleeveIds.Select(i => inputVertices[i]).ToArray(), contacts = stride, maximumOffset = (int)sleeveRenderer.quality, meshWorld = sleeveRenderer.transform.localToWorldMatrix, boneWorld = new[] { sleeveRenderer.rootBone.localToWorldMatrix } })); }
                    }
                }
                #endregion
                Graphics.Blit(target, resolved);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = resolved;
                pixels.ReadPixels(
                    new Rect(0, 0, captureWidth, captureHeight),
                    0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(
                    Path.Combine(frames, $"frame_{frame:D4}.png"),
                    pixels.EncodeToPNG());

                #region debug-point A-B:shadow-isolation
                if (frame == 162)
                {
                    string diagnosticDirectory = Path.Combine(
                        Path.GetFullPath(reportDirectory + "/../../../../.."),
                        ".dbg/v10-shadow-fabric");
                    Directory.CreateDirectory(diagnosticDirectory);
                    float originalReceiveShadows =
                        renderSkinMaterial.GetFloat("_ReceiveShadows");
                    renderSkinMaterial.SetFloat("_ReceiveShadows", 0);
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture.active = resolved;
                    pixels.ReadPixels(
                        new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(
                        Path.Combine(diagnosticDirectory,
                            "frame_0162_skin_no_realtime_shadow.png"),
                        pixels.EncodeToPNG());
                    renderSkinMaterial.SetFloat(
                        "_ReceiveShadows", originalReceiveShadows);

                    float originalSoftness =
                        renderSkinMaterial.GetFloat("_ShadowSoftness");
                    renderSkinMaterial.SetFloat("_ShadowSoftness", .25f);
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture.active = resolved;
                    pixels.ReadPixels(
                        new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(
                        Path.Combine(diagnosticDirectory,
                            "frame_0162_skin_max_toon_softness.png"),
                        pixels.EncodeToPNG());
                    renderSkinMaterial.SetFloat(
                        "_ShadowSoftness", originalSoftness);
                    float originalUnlit =
                        renderSkinMaterial.GetFloat("_UnlitBlend");
                    renderSkinMaterial.SetFloat("_UnlitBlend", 1);
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture.active = resolved;
                    pixels.ReadPixels(
                        new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(
                        Path.Combine(diagnosticDirectory,
                            "frame_0162_skin_unlit.png"),
                        pixels.EncodeToPNG());
                    renderSkinMaterial.SetFloat(
                        "_UnlitBlend", originalUnlit);
                    renderSkinMaterial.SetFloat("_DebugUV", 1);
                    camera.Render();
                    Graphics.Blit(target, resolved);
                    RenderTexture.active = resolved;
                    pixels.ReadPixels(
                        new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(
                        Path.Combine(diagnosticDirectory,
                            "frame_0162_skin_uv.png"),
                        pixels.EncodeToPNG());
                    renderSkinMaterial.SetFloat("_DebugUV", 0);
                    using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(renderDebugUrl, JsonUtility.ToJson(new RenderMaterialDebugEvent { hypothesisId = "A,B", msg = "[DEBUG] frame162 shadow isolation captures written" })); }
                }
                #endregion
            }

            // Causal checks: identical pose without/with gravity, then a small
            // root translation followed by a two-second stationary hold.
            clip.SampleAnimation(character, 30f / frameRate);
            float sleeveGravity = sleeves.gravityScale;
            sleeves.gravityScale = 0;
            sleeves.ResetSimulation();
            for (int i = 0; i < 90; i++) sleeves.Evaluate(1f / 60f);
            Vector3[] noGravity = (Vector3[])sleeves.ParticlePositions.Clone();
            sleeves.gravityScale = sleeveGravity;
            sleeves.ResetSimulation();
            for (int i = 0; i < 90; i++) sleeves.Evaluate(1f / 60f);
            Vector3[] gravityRest = (Vector3[])sleeves.ParticlePositions.Clone();
            int[] hemNodes = Enumerable.Range(0, gravityRest.Length)
                .Where(i => i % 120 >= 100).ToArray();
            float sleeveGravityDrop = hemNodes.Average(i =>
                Vector3.Dot(gravityRest[i] - noGravity[i], Vector3.down));
            Vector3 rootRest = character.transform.position;
            Vector3 translation = new Vector3(.02f, 0, 0);
            character.transform.position += translation;
            sleeves.Evaluate(1f / 60f);
            float sleeveMotionLag = hemNodes.Max(i =>
                Vector3.Distance(sleeves.ParticlePositions[i], gravityRest[i] + translation));
            for (int i = 0; i < 120; i++) sleeves.Evaluate(1f / 60f);
            float sleeveSettledError = hemNodes.Max(i =>
                Vector3.Distance(sleeves.ParticlePositions[i], gravityRest[i] + translation));
            #region debug-point sleeve-C:causal-response
            using (var web = new System.Net.WebClient()) { web.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json"; web.UploadString(sleeveDebugUrl, JsonUtility.ToJson(new SleeveDebugEvent { frame = 1000, msg = "[DEBUG] gravity on/off and translate/hold checks; maximumOffset=lag; minimumClearance=settled error", vertices = noGravity, particles = gravityRest, animatedParticles = sleeves.ParticlePositions, maximumOffset = sleeveMotionLag, minimumClearance = sleeveSettledError })); }
            #endregion
            character.transform.position = rootRest;
            sleeves.ResetSimulation();

            float skirtHemMovement = 0;
            for (int i = 0; i < skirtHemBones.Length; i++)
                skirtHemMovement = Mathf.Max(
                    skirtHemMovement,
                    Vector3.Distance(
                        skirtHemMinimum[i], skirtHemMaximum[i]));

            skirt.enabled = false;
            clip.SampleAnimation(character, 0);
            Quaternion neutralCharacterRotation =
                character.transform.rotation;
            Vector3 characterTiltAxis =
                character.transform.forward.normalized;

            character.transform.rotation =
                Quaternion.AngleAxis(15, characterTiltAxis) *
                neutralCharacterRotation;
            skirt.ResetDynamics();
            for (int i = 0; i < 90; i++)
                skirt.Evaluate(1f / 90f);
            float skirtPositiveGravityTiltResponse =
                Mathf.Abs(skirt.SecondaryAngles.y);

            character.transform.rotation =
                Quaternion.AngleAxis(-15, characterTiltAxis) *
                neutralCharacterRotation;
            skirt.ResetDynamics();
            for (int i = 0; i < 90; i++)
                skirt.Evaluate(1f / 90f);
            float skirtNegativeGravityTiltResponse =
                Mathf.Abs(skirt.SecondaryAngles.y);

            character.transform.rotation =
                neutralCharacterRotation;
            skirt.ResetDynamics();
            clip.SampleAnimation(character, 0);
            Transform hairRoot = hair.transform;
            Quaternion neutralHairRootRotation = hairRoot.rotation;
            hair.ResetSimulation();
            for (int i = 0; i < 30; i++)
                yield return new WaitForEndOfFrame();
            Vector3 neutralTipLocal =
                hairRoot.InverseTransformPoint(hairTip.position);
            Vector3 neutralGravity =
                hairRoot.InverseTransformDirection(Vector3.down).normalized;
            Vector3 tiltAxis = camera.transform.forward.normalized;

            hairRoot.rotation = Quaternion.AngleAxis(20, tiltAxis) *
                neutralHairRootRotation;
            hair.ResetSimulation();
            for (int i = 0; i < 45; i++)
                yield return new WaitForEndOfFrame();
            Vector3 positiveTipLocal =
                hairRoot.InverseTransformPoint(hairTip.position);
            Vector3 positiveGravity =
                hairRoot.InverseTransformDirection(Vector3.down).normalized;
            Vector3 positiveLateralGravity =
                (positiveGravity - neutralGravity).normalized;
            float positiveGravityTiltResponse = Vector3.Dot(
                positiveTipLocal - neutralTipLocal,
                positiveLateralGravity);

            hairRoot.rotation = Quaternion.AngleAxis(-20, tiltAxis) *
                neutralHairRootRotation;
            hair.ResetSimulation();
            for (int i = 0; i < 45; i++)
                yield return new WaitForEndOfFrame();
            Vector3 negativeTipLocal =
                hairRoot.InverseTransformPoint(hairTip.position);
            Vector3 negativeGravity =
                hairRoot.InverseTransformDirection(Vector3.down).normalized;
            Vector3 negativeLateralGravity =
                (negativeGravity - neutralGravity).normalized;
            float negativeGravityTiltResponse = Vector3.Dot(
                negativeTipLocal - neutralTipLocal,
                negativeLateralGravity);
            hairRoot.rotation = neutralHairRootRotation;
            hair.ResetSimulation();

            var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool toonVerified = renderers.All(renderer =>
                renderer.sharedMaterials.All(material =>
                    material && material.shader &&
                    material.shader.name == "MateSoul/Character Toon"));
            var report = new Report {
                unity = Application.unityVersion,
                prefab = "Runtime/Prefabs/Lengyue_GameCharacter.prefab",
                clip = clip.name,
                playMode = Application.isPlaying,
                toonVerified = toonVerified,
                hairInitialized = hair.IsInitialized,
                skirtInitialized = skirt.IsInitialized,
                sleeveInitialized = sleeves.IsInitialized,
                collarVertices = sleeveParts.Count(part => part == 6),
                sleeveDrivenVertices = sleeves.DrivenVertexCount,
                sleeveSkinInfluences = sleeves.SkinInfluences,
                sleeveMaxOffsetM = sleeveMaxOffset,
                sleeveMinimumClearanceM = sleeveMinimumClearance,
                sleeveAverageUpdateMs = sleeveUpdateMs / frameCount,
                sleeveGravityDropM = sleeveGravityDrop,
                sleeveMotionLagM = sleeveMotionLag,
                sleeveSettledErrorM = sleeveSettledError,
                headPoseGuardDisabled = !headGuard.enabled,
                frameRate = frameRate,
                frames = frameCount,
                captureWidth = captureWidth,
                captureHeight = captureHeight,
                captureMsaa = captureMsaa,
                captureSupersample = captureSupersample,
                cameraOrthographic = camera.orthographic,
                cameraOrthographicSize =
                    camera.orthographicSize,
                cameraForwardAlignmentDeg = Vector3.Angle(
                    camera.transform.forward, Vector3.back),
                cameraPosition = camera.transform.position,
                rendererCount = renderers.Length,
                hairConfiguredBones = hair.bones.Length,
                hairSimulatedJoints = simulatedJoints,
                hairSubsteps = substeps,
                hairQuality = hair.quality.ToString(),
                hairMaxBendLimitDeg = hair.maxBendDegrees,
                hairMaxChainBendLimitDeg =
                    hair.maxChainBendDegrees,
                skirtResponseMultiplier =
                    skirt.responseMultiplier,
                skirtMaxResponseDeg =
                    skirt.maxResponseDegrees,
                skirtGravityResponse =
                    skirt.gravityResponse,
                skirtInertiaResponse =
                    skirt.inertiaResponse,
                skirtSpringFrequency =
                    skirt.springFrequency,
                skirtDampingRatio =
                    skirt.dampingRatio,
                skirtMaxSecondaryLimitDeg =
                    skirt.maxSecondaryDegrees,
                hairMaxBoneAngleDeg = hairMaxAngle,
                hairMaxChainAngleDeg = hairMaxChainAngle,
                hairMaxLateralAngleDeg =
                    hairMaxLateralAngle,
                hairMaxDepthAngleDeg =
                    hairMaxDepthAngle,
                hairMaxLateralDrive = hairMaxLateralDrive,
                hairTipRelativeMovementM = Vector3.Distance(
                    tipMinimum, tipMaximum),
                hairTipLateralMovementM =
                    tipMaximum.x - tipMinimum.x,
                hairGravityAlignedLateralM =
                    hairGravityAlignedLateral,
                positiveGravityTiltResponseM =
                    positiveGravityTiltResponse,
                negativeGravityTiltResponseM =
                    negativeGravityTiltResponse,
                fringeTipRelativeMovementM = Vector3.Distance(
                    fringeMinimum, fringeMaximum),
                fringeTipLateralMovementM =
                    fringeMaximum.x - fringeMinimum.x,
                fringeTipDepthMovementM =
                    fringeMaximum.z - fringeMinimum.z,
                leftSideTipRelativeMovementM = Vector3.Distance(
                    leftSideMinimum, leftSideMaximum),
                leftSideTipLateralMovementM =
                    leftSideMaximum.x - leftSideMinimum.x,
                leftSideTipDepthMovementM =
                    leftSideMaximum.z - leftSideMinimum.z,
                rightSideTipRelativeMovementM = Vector3.Distance(
                    rightSideMinimum, rightSideMaximum),
                rightSideTipLateralMovementM =
                    rightSideMaximum.x - rightSideMinimum.x,
                rightSideTipDepthMovementM =
                    rightSideMaximum.z - rightSideMinimum.z,
                skirtMaxBoneAngleDeg = skirtMaxAngle,
                skirtMaxLeftFlexDeg = skirtMaxLeft,
                skirtMaxRightFlexDeg = skirtMaxRight,
                skirtMaxSecondaryAngleDeg =
                    skirtMaxSecondary,
                skirtMaxLateralSecondaryAngleDeg =
                    skirtMaxLateralSecondary,
                skirtMaxGravityTargetDeg =
                    skirtMaxGravityTarget,
                skirtMaxInertiaTargetDeg =
                    skirtMaxInertiaTarget,
                skirtHemMovementM = skirtHemMovement,
                skirtRestHemHalfWidthM =
                    skirt.RestHemHalfWidth,
                skirtDrapeBaselineHalfWidthM =
                    skirtDrapeBaselineHalfWidth,
                skirtDrapedHemHalfWidthM =
                    skirtDrapedHemHalfWidth,
                skirtMinimumHemHalfWidthM =
                    skirtMinimumHemHalfWidth,
                skirtMaximumHemHalfWidthM =
                    skirtMaximumHemHalfWidth,
                skirtRestHemDepthRadiusM =
                    skirt.RestHemDepthRadius,
                skirtDrapeBaselineDepthRadiusM =
                    skirtDrapeBaselineDepthRadius,
                skirtDrapedHemDepthRadiusM =
                    skirtDrapedHemDepthRadius,
                skirtMinimumHemDepthRadiusM =
                    skirtMinimumHemDepthRadius,
                skirtMaximumHemDepthRadiusM =
                    skirtMaximumHemDepthRadius,
                skirtMaxDrapeWeight =
                    skirtMaxDrapeWeight,
                skirtMaxAvoidanceWeight =
                    skirtMaxAvoidanceWeight,
                skirtPositiveGravityTiltResponseDeg =
                    skirtPositiveGravityTiltResponse,
                skirtNegativeGravityTiltResponseDeg =
                    skirtNegativeGravityTiltResponse,
                leftHandMovementM = Vector3.Distance(
                    leftHandMinimum, leftHandMaximum),
                rightHandMovementM = Vector3.Distance(
                    rightHandMinimum, rightHandMaximum),
                leftEyeRotationMovementDeg = leftEyeMovement,
                rightEyeRotationMovementDeg = rightEyeMovement,
                eyeCloseLeftMax = eyeCloseLeftMax,
                eyeCloseRightMax = eyeCloseRightMax,
                eyeWideLeftMax = eyeWideLeftMax,
                eyeWideRightMax = eyeWideRightMax,
                browSadLeftMax = browSadLeftMax,
                browSadRightMax = browSadRightMax,
                cheekPuffMax = cheekPuffMax,
                jawOpenMax = jawOpenMax,
                mouthEMax = mouthEMax,
                mouthShapeMax = mouthShapeMax,
                eyesFullyOpenFrame = eyesFullyOpenFrame,
                speechOpenSamples = speechOpenSamples,
                speechClosedSamples = speechClosedSamples,
                headNeckDistanceRatioDeviation =
                    maxHeadNeckDistanceDeviation,
                meshRenderingErrors = meshRenderingErrors,
                runtimeErrors = runtimeErrors,
                fineStrandsBound = surfaceContact.BoundFineStrands,
                fineVerticesBound = surfaceContact.BoundFineVertices,
                surfaceContactFrames = surfaceContactFrames
            };
            report.passed = report.playMode && report.toonVerified &&
                report.hairInitialized && report.skirtInitialized &&
                report.sleeveInitialized && report.collarVertices == 0 &&
                report.sleeveDrivenVertices == 10623 &&
                report.sleeveMaxOffsetM > .01f &&
                report.sleeveMaxOffsetM < .10f &&
                report.sleeveMinimumClearanceM > -.001f &&
                report.sleeveGravityDropM > .002f &&
                report.sleeveMotionLagM > .002f &&
                report.sleeveSettledErrorM < .001f &&
                report.headPoseGuardDisabled && report.frames == 173 &&
                report.captureWidth >= 1920 &&
                report.captureHeight >= 1080 &&
                report.captureMsaa >= 4 &&
                report.captureSupersample >= 2 &&
                report.cameraOrthographic &&
                Mathf.Abs(
                    report.cameraOrthographicSize - .5f) < .001f &&
                report.cameraForwardAlignmentDeg < .1f &&
                report.hairQuality == "Full" &&
                report.hairSimulatedJoints >= 113 &&
                report.hairSimulatedJoints < 120 &&
                report.hairSubsteps > 0 &&
                report.hairMaxBoneAngleDeg > 5f &&
                report.hairMaxBoneAngleDeg <
                    EnhancedHairMaxBendDegrees + .1f &&
                report.hairMaxChainAngleDeg > 5f &&
                report.hairMaxChainAngleDeg <
                    EnhancedHairMaxChainBendDegrees + .1f &&
                report.hairMaxLateralAngleDeg > 5f &&
                report.hairMaxLateralAngleDeg < 15.1f &&
                report.hairMaxDepthAngleDeg > 1f &&
                report.hairMaxDepthAngleDeg < 8.1f &&
                report.hairTipRelativeMovementM > .03f &&
                report.hairTipRelativeMovementM < .2f &&
                report.hairTipLateralMovementM > .01f &&
                report.positiveGravityTiltResponseM > .005f &&
                report.negativeGravityTiltResponseM > .005f &&
                report.fringeTipRelativeMovementM > .028f &&
                report.fringeTipRelativeMovementM < .06f &&
                report.hairMaxLateralDrive > .03f &&
                report.fringeTipLateralMovementM > .03f &&
                report.fringeTipDepthMovementM < .02f &&
                report.leftSideTipLateralMovementM > .035f &&
                report.leftSideTipRelativeMovementM < .08f &&
                report.rightSideTipLateralMovementM > .035f &&
                report.rightSideTipRelativeMovementM < .08f &&
                report.skirtResponseMultiplier > 1.4f &&
                report.skirtMaxBoneAngleDeg > 27f &&
                report.skirtMaxSecondaryAngleDeg > 1f &&
                report.skirtMaxSecondaryAngleDeg <
                    EnhancedSkirtMaxSecondaryDegrees + .1f &&
                report.skirtMaxLateralSecondaryAngleDeg > .5f &&
                report.skirtMaxGravityTargetDeg > .2f &&
                report.skirtMaxInertiaTargetDeg > .5f &&
                report.skirtHemMovementM > .02f &&
                report.skirtHemMovementM < .3f &&
                report.skirtDrapedHemHalfWidthM <
                    report.skirtDrapeBaselineHalfWidthM - .015f &&
                report.skirtDrapedHemDepthRadiusM <
                    report.skirtDrapeBaselineDepthRadiusM - .005f &&
                report.skirtMinimumHemHalfWidthM > .08f &&
                report.skirtMaximumHemHalfWidthM <
                    report.skirtRestHemHalfWidthM * 1.35f &&
                report.skirtMaxDrapeWeight > .9f &&
                report.skirtMaxAvoidanceWeight > .9f &&
                report.skirtPositiveGravityTiltResponseDeg > 2f &&
                report.skirtNegativeGravityTiltResponseDeg > 2f &&
                report.leftHandMovementM > .1f &&
                report.rightHandMovementM > .1f &&
                report.eyeCloseLeftMax > 95f &&
                report.eyeCloseRightMax > 95f &&
                report.eyeWideLeftMax > 85f &&
                report.eyeWideRightMax > 85f &&
                report.eyesFullyOpenFrame >= 108 &&
                report.eyesFullyOpenFrame <= 111 &&
                report.browSadLeftMax > 65f &&
                report.browSadRightMax > 65f &&
                report.cheekPuffMax > 70f &&
                report.jawOpenMax > 30f &&
                report.mouthEMax > 15f &&
                report.speechOpenSamples == 3 &&
                report.speechClosedSamples == 3 &&
                report.headNeckDistanceRatioDeviation < .05f &&
                report.meshRenderingErrors == 0 &&
                report.runtimeErrors == 0 &&
                report.fineStrandsBound == 12 &&
                report.fineVerticesBound == 2952 &&
                report.surfaceContactFrames == frameCount;
            File.WriteAllText(
                Path.Combine(reportDirectory, "unity_playmode.json"),
                JsonUtility.ToJson(report, true));

            camera.targetTexture = null;
            target.Release();
            resolved.Release();
            Destroy(target);
            Destroy(resolved);
            Destroy(pixels);
            #region debug-point sleeve-A-D:release
            Destroy(sleeveBaked);
            #endregion
            Time.captureFramerate = 0;
            Debug.Log("LENGYUE_YUMMY_UNITY_" +
                (report.passed ? "PASS" : "FAIL") + "\n" +
                JsonUtility.ToJson(report, true));

            #if UNITY_EDITOR
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.Exit(report.passed ? 0 : 1);
            else
                UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit(report.passed ? 0 : 1);
            #endif
        }

        void OnDestroy()
        {
            Time.captureFramerate = 0;
        }
    }
}
