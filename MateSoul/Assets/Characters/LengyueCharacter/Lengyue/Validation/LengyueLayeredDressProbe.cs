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
    public sealed class LengyueLayeredDressProbe : MonoBehaviour
    {
        public GameObject character;
        public string outputDirectory;
        public string reportDirectory;
        public bool appearanceCloseup;
        int meshRenderingErrors;

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }
        void OnLog(string message, string stack, LogType type)
        {
            if (message.Contains("SkinnedMeshRenderer: Rendering stopped"))
                meshRenderingErrors++;
        }

        [Serializable] sealed class MotionResult
        {
            public string motion;
            public int frames;
            public bool stateReached;
            public float handMovementM;
        }
        [Serializable] sealed class Report
        {
            public string unity, prefab;
            public bool playMode, toonVerified, broochAbsent, hairInitialized, guardsInitialized,
                sleeveRuffleAccentTextureVerified, strandHairVerified;
            public int hairJoints, hairSubsteps, rendererCount, frameRate, meshRenderingErrors;
            public MotionResult[] motions;
            public bool passed;
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != gameObject.scene)
                    foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
            }
            var animator = character.GetComponent<Animator>();
            var hair = character.GetComponentInChildren<HairSpringPhysics>(true);
            var skirt = character.GetComponent<SkirtPoseGuard>();
            var head = character.GetComponent<HeadPoseGuard>();
            var hand = character.GetComponentsInChildren<Transform>(true).First(t => t.name == "lengyue_hand_r");
            var camera = GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = appearanceCloseup ? .27f : .97f;
            float cameraHeight = appearanceCloseup ? 1.55f : .91f;
            camera.transform.position = new Vector3(.5f, cameraHeight, 3);
            camera.transform.LookAt(new Vector3(0, cameraHeight, 0));
            Time.captureFramerate = 30;
            int joints = 0, substeps = 0;
            var motions = new List<MotionResult>();
            string temp = Path.GetFullPath(Application.dataPath+"/../Temp/LayeredDressFrames");
            Directory.CreateDirectory(temp);
            var target = new RenderTexture(960, 960, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var pixels = new Texture2D(960, 960, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            yield return null;
            foreach (string motion in new[] { "walk", "running" })
            {
                string trigger = motion == "walk" ? "param_idletowalk" : "param_idletorunning";
                foreach (var param in animator.parameters)
                    if (param.type == AnimatorControllerParameterType.Bool)
                        animator.SetBool(param.name, param.name == trigger);
                for (int i = 0; i < 20; i++) yield return new WaitForEndOfFrame();
                string folder = temp+"/"+motion;
                Directory.CreateDirectory(folder);
                Vector3 minimum = hand.position, maximum = minimum;
                bool reached = false;
                for (int frame = 0; frame < 90; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    reached |= animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash(motion);
                    minimum = Vector3.Min(minimum, hand.position);
                    maximum = Vector3.Max(maximum, hand.position);
                    joints = Mathf.Max(joints, hair.LastSimulatedJoints);
                    substeps += hair.LastSubsteps;
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 960, 960), 0, 0); pixels.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(folder+$"/frame_{frame:D4}.png", pixels.EncodeToPNG());
                }
                motions.Add(new MotionResult {
                    motion = motion, frames = 90, stateReached = reached,
                    handMovementM = Vector3.Distance(minimum, maximum)
                });
            }
            var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var garment = renderers.Single(r => r.name == "lengyue_garment_main");
            var hairSkin = renderers.Single(r => r.name.Contains("Bob_Hair"));
            var garmentAtlas = garment.sharedMaterial.GetTexture("_BaseMap");
            var report = new Report {
                unity = Application.unityVersion,
                prefab = "Runtime/Prefabs/lengyue_character.prefab",
                playMode = Application.isPlaying,
                toonVerified = renderers.All(r => r.sharedMaterials.All(m =>
                    m && m.shader && m.shader.name == "MateSoul/Character Toon")),
                broochAbsent = !renderers.Any(r => r.name == "lengyue_star_brooch"),
                hairInitialized = hair.IsInitialized,
                guardsInitialized = skirt.IsInitialized && head.IsInitialized,
                sleeveRuffleAccentTextureVerified = garmentAtlas &&
                    garmentAtlas.name == "lengyue_tex_clothing_atlas",
                strandHairVerified = hairSkin.sharedMaterial.HasProperty("_HairMode") &&
                    hairSkin.sharedMaterial.GetFloat("_HairMode") > .5f &&
                    hairSkin.sharedMesh.tangents.Length == hairSkin.sharedMesh.vertexCount &&
                    (!hairSkin.sharedMaterial.HasProperty("_HairHighlightMap") ||
                     (hairSkin.sharedMaterial.GetTexture("_HairHighlightMap") &&
                      hairSkin.sharedMesh.uv2.Length == hairSkin.sharedMesh.vertexCount)) &&
                    renderers.Where(r => r != hairSkin).All(r => r.sharedMaterials.All(m =>
                        m.GetFloat("_HairMode") < .5f)),
                hairJoints = joints, hairSubsteps = substeps, rendererCount = renderers.Length,
                motions = motions.ToArray(), frameRate = 30,
                meshRenderingErrors = meshRenderingErrors
            };
            report.passed = report.playMode && report.toonVerified && report.broochAbsent &&
                report.hairInitialized && report.guardsInitialized && joints == 68 && substeps > 0 &&
                report.sleeveRuffleAccentTextureVerified && report.strandHairVerified &&
                meshRenderingErrors == 0 &&
                motions.All(m => m.stateReached && m.handMovementM > .02f);
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportDirectory+"/playmode.json", JsonUtility.ToJson(report, true));
            camera.targetTexture = null;
            target.Release(); Destroy(target); Destroy(pixels);
            Time.captureFramerate = 0;
            Debug.Log("LENGYUE_LAYERED_DRESS_PLAYMODE_"+(report.passed ? "PASS" : "FAIL"));
            #if UNITY_EDITOR
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.Exit(report.passed ? 0 : 1);
            else
                UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }
        void OnDestroy() { Time.captureFramerate = 0; }
    }
}
