using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Gameplay;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class TelevisionTests
    {
        private GameObject root;
        private TelevisionController television;
        private TelevisionPowerButton button;
        private Camera camera;
        private EventSystem events;
        private static string Artifacts => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../.artifacts/television"));

        private IEnumerator SetUp()
        {
            root = new GameObject("Television Integration");
            var floor = new GameObject("TV support", typeof(BoxCollider));
            floor.transform.SetParent(root.transform);
            floor.transform.position = Vector3.down * .1f;
            floor.GetComponent<BoxCollider>().size = new Vector3(6, .2f, 6);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TelevisionBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            television = Object.Instantiate(prefab, root.transform).GetComponent<TelevisionController>();
            button = television.GetComponentInChildren<TelevisionPowerButton>();
            camera = new GameObject("TV Test Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, .33f, -1.1f);
            camera.transform.LookAt(new Vector3(0, .27f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.11f, .1f, .15f);
            foreach (var canvas in television.GetComponentsInChildren<Canvas>()) canvas.worldCamera = camera;
            events = new GameObject("TV Test Events").AddComponent<EventSystem>();
            events.transform.SetParent(root.transform);
            var light = new GameObject("TV Test Light").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator PointerHighlightsPressesReboundsAndTogglesPower()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExercisePointer();
        }

        private IEnumerator ExercisePointer()
        {
            television.SetVideoClip(null);
            var data = new PointerEventData(events)
            {
                pointerId = 1, button = PointerEventData.InputButton.Left,
                position = camera.WorldToScreenPoint(button.transform.position)
            };
            var hits = new List<RaycastResult>();
            button.GetComponent<GraphicRaycaster>().Raycast(data, hits);
            Assert.That(hits.Any(hit => hit.gameObject == button.gameObject), Is.True,
                "Mouse must hit the actual power control in front of the housing.");
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off));
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerEnterHandler);
            yield return Wait(.15f);
            Assert.That(button.Highlight, Is.GreaterThan(.9f));
            Capture("hover");
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            yield return Wait(.09f);
            Assert.That(button.PressAmount, Is.GreaterThan(.9f));
            Assert.That(television.IsPoweredOn, Is.False, "Pointer down alone must not toggle power.");
            Capture("pressed");
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
            yield return Wait(.4f);
            Assert.That(button.PressAmount, Is.LessThan(.01f));
            Assert.That(television.State, Is.EqualTo(TelevisionState.NoSignal));
            Assert.That(television.Player.targetTexture, Is.Null, "No source must not allocate a decoder target.");
            Capture("no-signal");
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerExitHandler);
            yield return Wait(.2f);
            Assert.That(button.Highlight, Is.LessThan(.01f));
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off));
        }

        [UnityTest]
        public IEnumerator XrRayHitsOnlyButtonAndRespectsOcclusionAndBackFace()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseRay();
        }

        private IEnumerator ExerciseRay()
        {
            var raycaster = button.GetComponent<TrackedDeviceGraphicRaycaster>();
            var front = button.transform.position + Vector3.back;
            var data = new TrackedDeviceEventData(events)
            {
                pointerId = 3, layerMask = Physics.DefaultRaycastLayers,
                rayPoints = new List<Vector3> { front, front + Vector3.forward * 2 }
            };
            var hits = new List<RaycastResult>();
            raycaster.Raycast(data, hits);
            Assert.That(hits.Count, Is.EqualTo(1));
            data.rayPoints = new List<Vector3> { front + Vector3.right * .12f,
                front + Vector3.right * .12f + Vector3.forward * 2 };
            hits.Clear();
            raycaster.Raycast(data, hits);
            Assert.That(hits, Is.Empty, "Housing is not a power switch.");
            data.rayPoints = new List<Vector3> { front + Vector3.forward * 2, front };
            raycaster.Raycast(data, hits);
            Assert.That(hits, Is.Empty, "Power button cannot be clicked through the TV.");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.SetParent(root.transform);
            blocker.transform.position = front + Vector3.forward * .5f;
            blocker.transform.localScale = Vector3.one * .1f;
            Physics.SyncTransforms();
            data.rayPoints = new List<Vector3> { front, front + Vector3.forward * 2 };
            raycaster.Raycast(data, hits);
            Assert.That(hits, Is.Empty, "Physical occlusion blocks the UI ray.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrabBindingDoesNotOverrideScreenAndPowerIsIsolatedPerInstance()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseGrabBinding();
        }

        private IEnumerator ExerciseGrabBinding()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(TelevisionBuilder.DefinitionPath);
            var registry = new ItemRegistry();
            var item = television.gameObject.AddComponent<GameItem>();
            item.Bind(definition, registry, null, "test-tv");
            var second = Object.Instantiate(definition.Prefab, root.transform).GetComponent<TelevisionController>();
            second.transform.position = Vector3.right;
            var screen = television.GetComponentsInChildren<Renderer>()
                .Single(renderer => renderer.name == "TV_CRT_ConvexGlass");
            var buttonRenderer = television.GetComponentsInChildren<Renderer>()
                .Single(renderer => renderer.name == "TV_Power_Button");
            var focusTarget = button.GetComponent<ItemInteractionFocusTarget>();
            var material = screen.sharedMaterial;
            Assert.That(focusTarget, Is.Not.Null);
            Assert.That(focusTarget.Renderers, Is.EqualTo(new[] { buttonRenderer }));
            item.SetFocused(true);
            Assert.That(focusTarget.IsFocused, Is.True);
            Assert.That(buttonRenderer.sharedMaterials, Does.Contain(definition.FocusMaterial));
            Assert.That(television.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer != buttonRenderer)
                .All(renderer => !renderer.sharedMaterials.Contains(definition.FocusMaterial)), Is.True,
                "Only the declared power button may receive the focus outline.");
            television.TogglePower();
            yield return null;
            Assert.That(screen.sharedMaterial, Is.SameAs(material));
            item.SetFocused(false);
            Assert.That(buttonRenderer.sharedMaterials.Contains(definition.FocusMaterial),
                Is.False);
            Assert.That(second.State, Is.EqualTo(TelevisionState.Off));
            var grab = item.GetComponent<XRGrabInteractable>();
            Assert.That(grab.colliders, Has.Count.EqualTo(1));
            Assert.That(grab.colliders[0], Is.SameAs(item.GetComponent<BoxCollider>()));
            var scale = television.transform.localScale;
            television.gameObject.SetActive(false);
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off));
            Assert.That(television.Player.targetTexture, Is.Null);
            television.gameObject.SetActive(true);
            Assert.That(television.transform.localScale, Is.EqualTo(scale));
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off));
        }

        [UnityTest]
        public IEnumerator PrefabStartsWithLoopingCenterCroppedProgram()
        {
            yield return new EnterPlayMode();
            yield return SetUp();

            Assert.That(television.Programs.Count, Is.EqualTo(1));
            Assert.That(television.ProgramIndex, Is.Zero);
            Assert.That(television.CurrentProgram, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(television.CurrentProgram),
                Is.EqualTo(TelevisionBuilder.FirstProgramPath));
            Assert.That(television.CurrentProgram.width, Is.EqualTo(1280));
            Assert.That(television.CurrentProgram.height, Is.EqualTo(720));
            Assert.That(television.CurrentProgram.length, Is.EqualTo(15.072).Within(.1));
            Assert.That(television.Player.aspectRatio, Is.EqualTo(VideoAspectRatio.FitOutside));
            Assert.That(television.Player.isLooping, Is.True);
        }

        [UnityTest]
        public IEnumerator VirtualFingerDepthDrivesOnePhysicalClickPerPress()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseFinger();
        }

        private IEnumerator ExerciseFinger()
        {
            television.SetVideoClip(null);
            var surface = television.GetComponentInChildren<TelevisionPhysicalPressSurface>(true);
            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.GetComponent<BoxCollider>().isTrigger, Is.True);
            var finger = new GameObject("Virtual Index Fingertip");
            finger.transform.SetParent(root.transform);
            finger.AddComponent<SphereCollider>().radius = .004f;
            var body = finger.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            finger.AddComponent<PhysicalPressInteractor>().Configure(finger.transform);

            body.position = surface.transform.position - surface.transform.forward * .03f;
            Physics.SyncTransforms();
            yield return EngineTestFrames.FixedStep();
            body.MovePosition(surface.transform.position + surface.transform.forward * .007f);
            yield return EngineTestFrames.FixedStep();
            yield return Wait(.1f);
            Assert.That(television.State, Is.EqualTo(TelevisionState.NoSignal));
            Assert.That(button.PressAmount, Is.GreaterThan(.8f));
            Assert.That(button.Highlight, Is.GreaterThan(.5f));
            yield return EngineTestFrames.FixedStep();
            Assert.That(television.State, Is.EqualTo(TelevisionState.NoSignal),
                "Holding the fingertip down must not repeatedly toggle power.");

            body.MovePosition(surface.transform.position - surface.transform.forward * .03f);
            yield return EngineTestFrames.FixedStep();
            yield return Wait(.2f);
            Assert.That(button.PressAmount, Is.LessThan(.05f));
            Assert.That(button.Highlight, Is.LessThan(.05f));
            body.MovePosition(surface.transform.position + surface.transform.forward * .007f);
            yield return EngineTestFrames.FixedStep();
            yield return Wait(.1f);
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off),
                "A full release must arm the next physical press.");
            finger.SetActive(false);
            yield return Wait(.2f);
            Assert.That(button.PressAmount, Is.LessThan(.05f),
                "Disabling a virtual fingertip inside the trigger must release the button.");
            Object.Destroy(finger);
        }

        [UnityTest]
        public IEnumerator InvalidSourceReportsErrorAndClearingItRecoversWithoutPowerCycle()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseInvalidSource();
        }

        private IEnumerator ExerciseInvalidSource()
        {
            Assert.Throws<ArgumentException>(() => television.SetVideoUrl("not-a-video-url"));
            television.SetVideoClip(null);
            var serialized = new SerializedObject(television);
            serialized.FindProperty("videoUrl").stringValue = "invalid-inspector-value";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            television.SetPower(true);
            Assert.That(television.State, Is.EqualTo(TelevisionState.Error));
            Assert.That(television.LastError, Is.Not.Empty);
            television.SetVideoClip(null);
            Assert.That(television.State, Is.EqualTo(TelevisionState.NoSignal));
            Assert.That(television.LastError, Is.Null);
            Assert.That(television.Player.targetTexture, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OptionalDiagnosticVideoDecodesLoopsAndReleasesOnPowerOff()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseDiagnosticVideo();
        }

        private IEnumerator ExerciseDiagnosticVideo()
        {
            var path = Path.Combine(Artifacts, "diagnostic.mp4");
            if (!File.Exists(path)) Assert.Ignore("Generate the optional diagnostic clip; see Television.md.");
            television.SetVideoUrl(new Uri(path).AbsoluteUri);
            television.SetPower(true);
            var deadline = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < deadline &&
                   (television.State == TelevisionState.Preparing || television.Player.frame < 2))
                yield return null;
            Assert.That(television.State, Is.EqualTo(TelevisionState.Playing), television.LastError);
            Assert.That(television.Player.frame, Is.GreaterThanOrEqualTo(2));
            Assert.That(television.Player.targetTexture.IsCreated(), Is.True);
            var texture = television.Player.targetTexture;
            var pixels = ReadPixels(texture);
            var colors = pixels.GetPixels();
            Assert.That(colors.Max(color => color.r + color.g + color.b) -
                        colors.Min(color => color.r + color.g + color.b), Is.GreaterThan(.5f),
                "The decoded screen must contain the diagnostic pattern, not black.");
            Object.Destroy(pixels);
            Capture("video");
            var loops = 0;
            television.Player.loopPointReached += _ => loops++;
            deadline = Time.realtimeSinceStartup +
                       Mathf.Max(6, (float)television.Player.length + 3);
            while (loops == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(loops, Is.GreaterThan(0));
            television.SetPower(false);
            Assert.That(television.Player.isPlaying, Is.False);
            Assert.That(television.Player.targetTexture, Is.Null);
            Assert.That(texture == null || !texture.IsCreated(), Is.True);
            television.SetPower(true);
            television.SetPower(false);
            yield return Wait(.4f);
            Assert.That(television.State, Is.EqualTo(TelevisionState.Off),
                "A cancelled Prepare must not reactivate the TV.");
        }

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private void Capture(string name)
        {
            var target = RenderTexture.GetTemporary(960, 800, 24);
            camera.targetTexture = target;
            camera.Render();
            var image = ReadPixels(target);
            Directory.CreateDirectory(Artifacts);
            File.WriteAllBytes(Path.Combine(Artifacts, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
        }

        private static Texture2D ReadPixels(RenderTexture source)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }
    }
}
