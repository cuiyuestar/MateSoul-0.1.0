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

namespace MateSoul.Experiments.YummyCookie
{
    /// <summary>Disposable scene-local interaction; the source clip/prefab are read-only.</summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class YummyCookieDemo : MonoBehaviour
    {
        public GameObject character, cookie;
        public Transform plate, servingPose, hand, upperArm, forearm, head;
        public AnimationClip clip;
        public Camera renderCamera;
        public string outputDirectory;
        public int cookieCount = 14;
        public int leadInFrames = 24;
        public int biteSourceFrame = 22;
        public int sourceFrames = 173;
        public int frameRate = 30;
        public Vector3 gripLocalPosition = new Vector3(.085f, .010f, -.032f);
        public Quaternion gripLocalRotation;
        public Vector3 mouthHeadLocal;
        public bool loop = true;

        SkinnedMeshRenderer face;
        HairSpringPhysics hair;
        SkirtPoseGuard skirt;
        LengyueSleeveCloth sleeves;
        Transform[] fingers;
        Quaternion[] closedFingers, openFingers;
        int jawShape;
        bool capture, ready, paused;
        float playbackFrame;
        int runtimeErrors;
        public string Phase { get; private set; }
        public float SourceFrame { get; private set; }

        [Serializable] public sealed class FrameObservation
        {
            public int frame;
            public float sourceFrame;
            public string phase;
            public bool cookieVisible, parentIsHand;
            public int onPlate;
            public Vector3 cookieCenter, mouth, handPosition;
            public float handAnchorErrorM, mouthCenterDistanceM, mouthSurfaceDistanceM;
        }
        [Serializable] sealed class ReplayReport
        {
            public string unity, baseline = "v10 ShadowFabric, no collar";
            public int frames, frameRate, pickupFrame, biteFrame, cookieCount;
            public int runtimeErrors, sleeveDrivenVertices;
            public bool hairInitialized, sleeveInitialized, rewindRestoresServing, repeatedBiteHidesCookie, passed;
            public float maximumHandAnchorErrorM, pickupJumpM, lastVisibleMouthSurfaceDistanceM;
            public FrameObservation[] observations;
        }
        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors++;
        }

        IEnumerator Start()
        {
            capture = Environment.GetCommandLineArgs().Contains("--cookie-capture");
            Application.runInBackground = true;
            if (capture)
            {
                QualitySettings.vSyncCount = 0;
                Time.captureFramerate = frameRate;
            }
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>())
                    camera.enabled = camera == renderCamera;
            // Editor additive preview must not run a second character/capture controller.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i) != gameObject.scene)
                    foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                        root.SetActive(false);
            character.GetComponent<Animator>().enabled = false;
            character.GetComponent<HeadPoseGuard>().enabled = false;
            face = character.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Single(r => r.name == "face");
            jawShape = Enumerable.Range(0, face.sharedMesh.blendShapeCount)
                .Single(i => face.sharedMesh.GetBlendShapeName(i).EndsWith("face_jaw_open_BS"));
            fingers = hand.GetComponentsInChildren<Transform>()
                .Where(t => t != hand).ToArray();
            clip.SampleAnimation(character, 30f / frameRate);
            openFingers = fingers.Select(t => t.localRotation).ToArray();
            clip.SampleAnimation(character, 0);
            closedFingers = fingers.Select(t => t.localRotation).ToArray();
            hair = character.GetComponentInChildren<HairSpringPhysics>();
            skirt = character.GetComponent<SkirtPoseGuard>();
            sleeves = character.GetComponent<LengyueSleeveCloth>();
            skirt.BindByName();
            skirt.Initialize();
            hair.Initialize();
            hair.GetComponent<LengyueHairSurfaceContact>().Initialize();
            sleeves.Initialize();
            ApplyFrame(0);
            skirt.ResetDynamics();
            hair.ResetSimulation();
            sleeves.ResetSimulation();
            for (int i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            ready = true;
            if (capture) yield return Capture();
        }

        void Update()
        {
            if (!ready || capture) return;
            if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (paused) return;
            ApplyFrame(playbackFrame);
            playbackFrame += Time.deltaTime * frameRate;
            if (loop && playbackFrame >= leadInFrames + sourceFrames + 30) Restart();
        }

        public void Restart()
        {
            playbackFrame = 0;
            ApplyFrame(0);
            skirt.ResetDynamics();
            hair.ResetSimulation();
            sleeves.ResetSimulation();
        }

        public void ApplyFrame(float frame)
        {
            SourceFrame = Mathf.Clamp(frame - leadInFrames, 0, sourceFrames - 1);
            clip.SampleAnimation(character, SourceFrame / frameRate);
            if (frame < leadInFrames)
            {
                float reach = Mathf.SmoothStep(0, 1, frame / (leadInFrames - 1));
                Vector3 target = hand.position + Vector3.up * (.065f * (1 - reach));
                SolveReach(target);
                float close = Mathf.SmoothStep(0, 1,
                    Mathf.InverseLerp(leadInFrames * .35f, leadInFrames - 1, frame));
                for (int i = 0; i < fingers.Length; i++)
                    fingers[i].localRotation = Quaternion.Slerp(openFingers[i], closedFingers[i], close);
            }
            // The old reconstructed facial clip has no opening during the ingestion gesture.
            // Add the mouth opening only to this instance, then return to its original puff/chew.
            float open = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(13, 19, SourceFrame)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(22, 28, SourceFrame)));
            face.SetBlendShapeWeight(jawShape, Mathf.Max(face.GetBlendShapeWeight(jawShape), open * 34));
            if (frame < leadInFrames)
            {
                Phase = "Reach";
                cookie.SetActive(true);
                cookie.transform.SetParent(plate, false);
                cookie.transform.SetPositionAndRotation(servingPose.position, servingPose.rotation);
            }
            else
            {
                bool eaten = SourceFrame >= biteSourceFrame;
                Phase = eaten ? "Chew" : "Carry";
                cookie.transform.SetParent(hand, false);
                cookie.transform.localPosition = gripLocalPosition;
                cookie.transform.localRotation = gripLocalRotation;
                cookie.SetActive(!eaten);
            }
        }

        void SolveReach(Vector3 target)
        {
            // Short lead-in only: preserve the sampled elbow side with a two-joint CCD reach.
            for (int i = 0; i < 8; i++)
            {
                forearm.rotation = Quaternion.FromToRotation(
                    hand.position - forearm.position, target - forearm.position) * forearm.rotation;
                upperArm.rotation = Quaternion.FromToRotation(
                    hand.position - upperArm.position, target - upperArm.position) * upperArm.rotation;
            }
        }

        FrameObservation Observe(int frame)
        {
            Vector3 mouth = head.TransformPoint(mouthHeadLocal);
            Vector3 local = cookie.transform.InverseTransformPoint(mouth);
            // Cookie is Y-up, diameter 50 mm and centered through its thickness.
            float radial = new Vector2(local.x, local.z).magnitude;
            float plane = Mathf.Max(Mathf.Abs(local.y) - .0055f, 0);
            float edge = Mathf.Max(radial - .025f, 0);
            return new FrameObservation {
                frame = frame, sourceFrame = SourceFrame, phase = Phase,
                cookieVisible = cookie.activeSelf,
                parentIsHand = cookie.transform.parent == hand,
                onPlate = plate.GetComponentsInChildren<MeshFilter>()
                    .Count(f => f.sharedMesh == cookie.GetComponent<MeshFilter>().sharedMesh),
                cookieCenter = cookie.transform.position,
                handPosition = hand.position, mouth = mouth,
                handAnchorErrorM = Vector3.Distance(cookie.transform.position,
                    hand.TransformPoint(gripLocalPosition)),
                mouthCenterDistanceM = Vector3.Distance(cookie.transform.position, mouth),
                mouthSurfaceDistanceM = Mathf.Sqrt(edge * edge + plane * plane)
            };
        }

        IEnumerator Capture()
        {
            string framesPath = Path.Combine(outputDirectory, "frames");
            Directory.CreateDirectory(framesPath);
            Directory.CreateDirectory(Path.Combine(outputDirectory, "reports"));
            const int width = 1920, height = 1080;
            var target = new RenderTexture(width * 2, height * 2, 24,
                RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            target.Create();
            resolved.Create();
            renderCamera.targetTexture = target;
            var observations = new List<FrameObservation>();
            for (int f = 0; f < leadInFrames + sourceFrames; f++)
            {
                ApplyFrame(f);
                yield return new WaitForEndOfFrame();
                observations.Add(Observe(f));
                renderCamera.Render();
                Graphics.Blit(target, resolved);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = resolved;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(framesPath, $"frame_{f:D4}.png"),
                    pixels.EncodeToPNG());
            }
            renderCamera.targetTexture = null;
            target.Release();
            resolved.Release();
            Destroy(target);
            Destroy(resolved);
            Destroy(pixels);
            int bite = leadInFrames + biteSourceFrame;
            var report = new ReplayReport {
                unity = Application.unityVersion, frames = observations.Count,
                frameRate = frameRate, pickupFrame = leadInFrames, biteFrame = bite,
                cookieCount = cookieCount, runtimeErrors = runtimeErrors,
                hairInitialized = hair.IsInitialized, sleeveInitialized = sleeves.IsInitialized,
                sleeveDrivenVertices = sleeves.DrivenVertexCount,
                maximumHandAnchorErrorM = observations.Where(f => f.phase == "Carry")
                    .Max(f => f.handAnchorErrorM),
                pickupJumpM = Vector3.Distance(observations[leadInFrames - 1].cookieCenter,
                    observations[leadInFrames].cookieCenter),
                lastVisibleMouthSurfaceDistanceM = observations[bite - 1].mouthSurfaceDistanceM,
                observations = observations.ToArray()
            };
            ApplyFrame(0);
            report.rewindRestoresServing = cookie.activeSelf && cookie.transform.parent == plate &&
                Vector3.Distance(cookie.transform.position, servingPose.position) < .00001f &&
                Observe(0).onPlate == cookieCount;
            ApplyFrame(bite);
            report.repeatedBiteHidesCookie = !cookie.activeSelf &&
                Observe(bite).onPlate == cookieCount - 1;
            report.passed = runtimeErrors == 0 && report.frames == leadInFrames + sourceFrames &&
                report.hairInitialized && report.sleeveInitialized && report.sleeveDrivenVertices == 10623 &&
                report.rewindRestoresServing && report.repeatedBiteHidesCookie &&
                report.maximumHandAnchorErrorM < .00001f && report.pickupJumpM < .001f &&
                report.lastVisibleMouthSurfaceDistanceM < .02f &&
                observations.Take(leadInFrames).All(f => f.onPlate == cookieCount) &&
                observations.Skip(leadInFrames).All(f => f.onPlate == cookieCount - 1) &&
                observations.Take(bite).All(f => f.cookieVisible) &&
                observations.Skip(bite).All(f => !f.cookieVisible) &&
                observations.Where(f => f.phase == "Carry").All(f => f.parentIsHand);
            File.WriteAllText(Path.Combine(outputDirectory, "reports/replay.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("YUMMY_COOKIE_REPLAY_" + (report.passed ? "PASS" : "FAIL"));
            Application.Quit(report.passed ? 0 : 1);
        }
    }
}
