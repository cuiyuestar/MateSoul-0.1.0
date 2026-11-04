using System;
using System.Collections;
using System.IO;
using System.Linq;
using MateSoul.Animation;
using MateSoul.Cloth;
using MateSoul.Wardrobe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MateSoul.Validation
{
    /// <summary>Actual PlayMode garment integration, rendering and lifecycle proof.</summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class LengyuePajamasProbe : MonoBehaviour
    {
        public GameObject character;
        public AnimationClip[] clips;
        public string outputDirectory;
        Camera capture;
        RenderTexture target;
        Texture2D pixels;
        int errors;
        int events;
        readonly Report report = new Report();

        [Serializable] sealed class Report
        {
            public bool playMode, newOutfitActive, oldOutfitHidden, hatBoundToHead;
            public bool defaultClothDisabled, defaultClothRestored, animatorPreserved;
            public bool skeletonCountPreserved, repeatedSwitchesStable, invalidIdRejected;
            public int frames, successfulSwitches, runtimeErrors, activeSleepRenderers;
            public float hatHeadRelativeErrorM, maxBakedVertexLengthM;
            public string unity, currentOutfit, error;
            public string[] sampledClips;
            public bool passed;
        }

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            report.playMode = Application.isPlaying;
            report.unity = Application.unityVersion;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors++;
        }

        IEnumerator Start()
        {
            var routine = Run();
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception exception)
                {
                    report.error = exception.ToString();
                    report.passed = false;
                    Finish();
                    yield break;
                }
                if (!more) break;
                yield return routine.Current;
            }
            Finish();
        }

        IEnumerator Run()
        {
            Application.runInBackground = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != gameObject.scene)
                    foreach (GameObject root in scene.GetRootGameObjects()) root.SetActive(false);
            }
            Time.captureFramerate = 30;
            Directory.CreateDirectory(outputDirectory + "/renders/frames");
            capture = GetComponent<Camera>();
            target = new RenderTexture(1000,1000,24,RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            pixels = new Texture2D(1000,1000,TextureFormat.RGB24,false);
            capture.targetTexture = target;
            var wardrobe = character.GetComponent<LengyueWardrobeController>();
            var animator = character.GetComponent<Animator>();
            var animatorId = animator.GetInstanceID();
            var animatorController = animator.runtimeAnimatorController;
            var sleeves = character.GetComponent<LengyueSleeveCloth>();
            var skirt = character.GetComponent<SkirtPoseGuard>();
            var initial = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == "LavenderCasual_Garments");
            Transform[] skeleton = initial.bones;
            int transformsBefore = skeleton[0].root.GetComponentsInChildren<Animator>(true).Length;
            var head = character.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Head 1");
            wardrobe.OutfitChanged += _ => events++;
            yield return null;
            // Check the real Animator while running, then use existing authored
            // clips deterministically for the capture and deformation sweep.
            for (int i = 0; i < 4; i++) yield return null;
            var stateBefore = animator.GetCurrentAnimatorStateInfo(0);
            Switch(wardrobe, "pink-pajamas");
            var stateAfter = animator.GetCurrentAnimatorStateInfo(0);
            report.animatorPreserved = animator.GetInstanceID() == animatorId &&
                animator.runtimeAnimatorController == animatorController &&
                stateBefore.fullPathHash == stateAfter.fullPathHash &&
                Mathf.Abs(stateBefore.normalizedTime-stateAfter.normalizedTime) < .0001f;
            yield return null;
            Switch(wardrobe, "lavender-casual");
            yield return null;
            animator.enabled = false;
            var headGuard = character.GetComponent<HeadPoseGuard>();
            if (headGuard) headGuard.enabled = false;
            var rest = character.GetComponentsInChildren<Transform>(true)
                .Select(t => (t,t.localPosition,t.localRotation,t.localScale)).ToArray();
            Vector3 hatReference = Vector3.zero;
            Mesh baked = new Mesh();
            for (int frame = 0; frame < 330; frame++)
            {
                if (frame == 45) Switch(wardrobe, "pink-pajamas");
                if (frame == 285) Switch(wardrobe, "lavender-casual");
                foreach (var p in rest)
                    if (p.t)
                    {
                        p.t.localPosition = p.localPosition;
                        p.t.localRotation = p.localRotation;
                        p.t.localScale = p.localScale;
                    }
                int clipIndex = frame < 45 || frame >= 285 ? 0 : (frame-45)/60;
                float time = clipIndex == 0 ? .2f : ((frame-45)%60)/59f*clips[clipIndex].length;
                clips[clipIndex].SampleAnimation(character,time);
                yield return new WaitForEndOfFrame();
                if (frame >= 45 && frame < 285)
                {
                    var active = character.GetComponentsInChildren<SkinnedMeshRenderer>()
                        .Where(r => r.name.StartsWith("PinkPajamas_")).ToArray();
                    Require(active.Length == 4, "Sleep outfit must have exactly four renderer parts");
                    var hat = active.Single(r => r.name == "PinkPajamas_SleepCap");
                    report.activeSleepRenderers = active.Length;
                    report.newOutfitActive = wardrobe.CurrentOutfitId == "pink-pajamas";
                    report.oldOutfitHidden = !initial.gameObject.activeInHierarchy;
                    report.defaultClothDisabled = !sleeves.enabled && !sleeves.IsInitialized && !skirt.enabled;
                    report.hatBoundToHead = hat.bones.Where((b,i) =>
                        hat.sharedMesh.boneWeights.Any(w => (w.weight0 > 0 && w.boneIndex0 == i) ||
                            (w.weight1 > 0 && w.boneIndex1 == i))).All(b => b == head);
                    if (frame%10 == 5)
                    {
                        foreach (var renderer in active)
                        {
                            renderer.BakeMesh(baked);
                            foreach (Vector3 v in baked.vertices)
                            {
                                Require(!float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                                    !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                                    !float.IsNaN(v.z) && !float.IsInfinity(v.z), "Nonfinite skinned vertex");
                                report.maxBakedVertexLengthM = Mathf.Max(report.maxBakedVertexLengthM,
                                    renderer.transform.TransformPoint(v).magnitude);
                            }
                        }
                        hat.BakeMesh(baked);
                        Vector3 pos = head.InverseTransformPoint(
                            hat.transform.position + hat.transform.rotation*baked.vertices[0]);
                        if (frame == 45) hatReference = pos;
                        else report.hatHeadRelativeErrorM = Mathf.Max(report.hatHeadRelativeErrorM,
                            Vector3.Distance(hatReference,pos));
                    }
                }
                Capture(outputDirectory + $"/renders/frames/frame_{frame:D4}.png");
                if (frame == 20) Capture(outputDirectory + "/renders/unity_before.png");
                if (frame == 75)
                {
                    Capture(outputDirectory + "/renders/unity_pajamas_front.png");
                    foreach (var view in new[] { "side", "back", "three_quarter" })
                    {
                        capture.transform.position = new Vector3(
                            view == "side" ? 4 : view == "three_quarter" ? 2 : 0,
                            .94f, view == "side" ? 0 : view == "back" ? -4 : 4);
                        capture.transform.LookAt(new Vector3(0,.94f,0));
                        Capture(outputDirectory + "/renders/unity_pajamas_" + view + ".png");
                    }
                    capture.transform.position = new Vector3(0,.94f,4);
                    capture.transform.LookAt(new Vector3(0,.94f,0));
                }
                if (frame == 305) Capture(outputDirectory + "/renders/unity_restored.png");
                report.frames++;
            }
            Destroy(baked);
            report.defaultClothRestored = sleeves.enabled && sleeves.IsInitialized &&
                sleeves.target == initial && skirt.enabled && initial.gameObject.activeInHierarchy;
            Require(report.defaultClothRestored, "Original cloth did not restart");
            for (int i = 0; i < 6; i++)
            {
                Switch(wardrobe, "pink-pajamas");
                yield return null;
                Switch(wardrobe, "lavender-casual");
                yield return null;
            }
            report.repeatedSwitchesStable = !wardrobe.IsUsingRuntimeOutfit &&
                character.GetComponentsInChildren<Transform>(true)
                    .All(t => !t.name.StartsWith("Lengyue_Outfit_")) &&
                sleeves.SourceMesh && sleeves.IsInitialized;
            report.invalidIdRejected = !wardrobe.TrySwitchOutfit("missing-pajamas",out _) &&
                wardrobe.CurrentOutfitId == "lavender-casual" && initial.gameObject.activeInHierarchy;
            report.skeletonCountPreserved = skeleton.SequenceEqual(initial.bones) &&
                transformsBefore == character.GetComponentsInChildren<Animator>(true).Length;
            report.successfulSwitches = events;
            report.currentOutfit = wardrobe.CurrentOutfitId;
            report.sampledClips = clips.Select(c => c.name).ToArray();
            report.passed = report.playMode && report.animatorPreserved &&
                report.newOutfitActive && report.oldOutfitHidden && report.hatBoundToHead &&
                report.defaultClothDisabled && report.defaultClothRestored &&
                report.repeatedSwitchesStable && report.skeletonCountPreserved && report.invalidIdRejected &&
                report.hatHeadRelativeErrorM < .0001f && report.maxBakedVertexLengthM < 3 &&
                errors == 0 && report.frames == 330 && events == 16;
        }

        static void Switch(LengyueWardrobeController wardrobe, string id)
        {
            Require(wardrobe.TrySwitchOutfit(id,out string error), error);
        }

        void Capture(string path)
        {
            capture.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,1000,1000),0,0);
            pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path,pixels.EncodeToPNG());
        }

        void Finish()
        {
            report.runtimeErrors = errors;
            Directory.CreateDirectory(outputDirectory + "/reports");
            File.WriteAllText(outputDirectory + "/reports/unity_playmode.json",JsonUtility.ToJson(report,true));
            Debug.Log("PINK_PAJAMAS_PLAYMODE_" + (report.passed ? "PASS" : "FAIL") +
                "\n" + JsonUtility.ToJson(report,true));
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }

        static void Require(bool condition,string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Time.captureFramerate = 0;
            if (capture) capture.targetTexture = null;
            if (target) { target.Release(); Destroy(target); }
            if (pixels) Destroy(pixels);
        }
    }
}
