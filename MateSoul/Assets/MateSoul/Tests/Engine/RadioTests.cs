using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class RadioTests
    {
        private GameObject root;
        private RadioController radio;
        private EventSystem events;
        private Camera camera;
        private RenderTexture cameraTarget;

        private IEnumerator SetUp()
        {
            root = new GameObject("Radio Integration");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RadioBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            radio = Object.Instantiate(prefab, root.transform).GetComponent<RadioController>();
            events = new GameObject("Radio Test Events").AddComponent<EventSystem>();
            events.transform.SetParent(root.transform);
            camera = new GameObject("Radio Test Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.tag = "MainCamera";
            cameraTarget = new RenderTexture(1024, 1024, 16);
            camera.targetTexture = cameraTarget;
            camera.transform.position = new Vector3(0, .14f, -.8f);
            camera.transform.LookAt(new Vector3(0, .12f, 0));
            foreach (var canvas in radio.GetComponentsInChildren<Canvas>())
                canvas.worldCamera = camera;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            if (cameraTarget != null) Object.Destroy(cameraTarget);
            cameraTarget = null;
            if (!Application.isPlaying) yield break;
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator PowerVolumeAndStationControlsOwnOneRadioInstance()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            radio.SetTransitionDurationsForTesting(.4f, .3f);
            var power = radio.GetComponentInChildren<RadioPowerButton>();
            var knobs = radio.GetComponentsInChildren<RadioKnobControl>();
            var tuning = knobs.Single(value => value.Kind == RadioKnobKind.Tuning);
            var volume = knobs.Single(value => value.Kind == RadioKnobKind.Volume);
            Assert.That(power, Is.Not.Null);
            Assert.That(radio.IsPoweredOn, Is.False);

            ExecuteEvents.Execute(power.gameObject, new PointerEventData(events)
            {
                pointerId = 1,
                button = PointerEventData.InputButton.Left
            }, ExecuteEvents.pointerClickHandler);
            yield return Wait(.12f);
            Assert.That(radio.IsPoweredOn, Is.True);
            Assert.That(radio.ActiveChannel, Is.Not.Null);
            Assert.That(radio.ActiveChannel.isPlaying, Is.True);

            volume.SetNormalizedValue(.8f);
            Assert.That(radio.Volume, Is.EqualTo(.8f).Within(.001f));
            Assert.That(radio.OutputVolume, Is.EqualTo(.576f).Within(.001f));
            tuning.SetNormalizedValue(0);
            yield return Wait(.2f);
            Assert.That(radio.IsTuningTransition, Is.True);
            Assert.That(radio.ActiveChannel.isPlaying, Is.True);
            Assert.That(radio.TransitionChannel.isPlaying, Is.True);
            Assert.That(radio.NoiseChannel.isPlaying, Is.True);
            Assert.That(radio.ActiveChannel.volume, Is.GreaterThan(0));
            Assert.That(radio.TransitionChannel.volume, Is.GreaterThan(0));
            Assert.That(radio.NoiseChannel.volume, Is.GreaterThan(0));
            yield return Wait(.3f);
            Assert.That(radio.IsTuningTransition, Is.False);
            Assert.That(radio.StationIndex, Is.Zero);
            Assert.That(radio.ActiveChannel.clip, Is.SameAs(radio.Stations[0]));
        }

        [UnityTest]
        public IEnumerator ItemCenterFocusTargetsEveryRadioControlAtOnce()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            Assert.That(radio, Is.Not.Null);
            var targets = radio.GetComponentsInChildren<ItemInteractionFocusTarget>(true);
            Assert.That(targets, Has.Length.EqualTo(3));
            var item = radio.gameObject.AddComponent<GameItem>();

            item.SetFocused(true);
            Assert.That(targets.All(target => target.IsFocusRequested), Is.True);
            item.SetFocused(false);
            Assert.That(targets.All(target => !target.IsFocusRequested), Is.True);
        }

        [UnityTest]
        public IEnumerator TuningTraversesAdjacentStationsAndMixesStaticAtEachStep()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            radio.SetTransitionDurationsForTesting(.18f, .3f);
            radio.SetPower(true);
            yield return Wait(.05f);
            radio.SetTuning(0);
            yield return Wait(.22f);
            Assert.That(radio.StationIndex, Is.Zero);

            var visited = new List<int>();
            radio.StationChanged += visited.Add;
            radio.SetTuning(1);
            yield return Wait(.48f);
            Assert.That(visited, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(radio.StationIndex, Is.EqualTo(2));
            Assert.That(radio.ActiveChannel.clip, Is.SameAs(radio.Stations[2]));
        }

        [UnityTest]
        public IEnumerator TrackEndCrossfadesIntoItsOwnBeginningBeforeStoppingOldChannel()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            radio.SetTransitionDurationsForTesting(.3f, .25f);
            radio.SetPower(true);
            yield return Wait(.05f);
            var clip = radio.ActiveChannel.clip;
            radio.SetPlaybackTimeForTesting(clip.length - .12f);
            yield return null;
            Assert.That(radio.IsLoopTransition, Is.True);
            Assert.That(radio.TransitionChannel.clip, Is.SameAs(clip));
            Assert.That(radio.ActiveChannel.isPlaying, Is.True);
            Assert.That(radio.TransitionChannel.isPlaying, Is.True);
            Assert.That(radio.NoiseChannel.isPlaying, Is.False);
            yield return Wait(.3f);
            Assert.That(radio.IsLoopTransition, Is.False);
            Assert.That(radio.ActiveChannel.clip, Is.SameAs(clip));
            Assert.That(radio.ActiveChannel.time, Is.LessThan(1f));

            radio.SetPower(false);
            yield return Wait(.4f);
            Assert.That(radio.ActiveChannel, Is.Null);
            Assert.That(radio.GetComponents<AudioSource>().Any(source => source.isPlaying), Is.False);
        }

        [UnityTest]
        public IEnumerator ControlsMoveContinuouslyTowardAuthoredTargetsAndStayInsideFrames()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var renderers = radio.GetComponentsInChildren<Renderer>(true);
            var knobs = radio.GetComponentsInChildren<RadioKnobControl>();
            var tuning = knobs.Single(value => value.Kind == RadioKnobKind.Tuning);
            var volume = knobs.Single(value => value.Kind == RadioKnobKind.Volume);
            var power = radio.GetComponentInChildren<RadioPowerButton>();

            var lowLabel = renderers.Single(value => value.name == "Radio_Label_Low");
            var volumeLabel = renderers.Single(value => value.name == "Radio_Label_Volume");
            var highLabel = renderers.Single(value => value.name == "Radio_Label_High");
            var volumePointer =
                renderers.Single(value => value.name == "Radio_VolumeKnob_Pointer");
            var initialValue = volume.Value;
            var down = PointerAtWorld(lowLabel.bounds.center);
            ExecuteEvents.Execute(volume.gameObject, down, ExecuteEvents.pointerDownHandler);
            Assert.That(volume.Value, Is.EqualTo(initialValue).Within(.0001f),
                "Pressing a knob must latch its current value instead of jumping.");
            ExecuteEvents.Execute(volume.gameObject, down, ExecuteEvents.pointerUpHandler);

            volume.SetNormalizedValue(0);
            AssertPointerAligned(volume, volumePointer, lowLabel);
            var low = PointerAtWorld(lowLabel.bounds.center);
            var high = PointerAtWorld(highLabel.bounds.center);
            ExecuteEvents.Execute(volume.gameObject, low, ExecuteEvents.pointerDownHandler);
            Assert.That(volume.IsDraggingForTesting, Is.True);
            var previousValue = volume.Value;
            var previousOutput = radio.OutputVolume;
            for (var index = 1; index <= 10; index++)
            {
                var step = new PointerEventData(events)
                {
                    pointerId = 13,
                    button = PointerEventData.InputButton.Left,
                    position = Vector2.Lerp(low.position, high.position, index / 10f)
                };
                ExecuteEvents.Execute(volume.gameObject, step, ExecuteEvents.dragHandler);
                Assert.That(volume.Value, Is.GreaterThan(previousValue));
                Assert.That(volume.Value - previousValue, Is.LessThan(.12f),
                    $"Volume must not jump while crossing the knob center. Step={index}, " +
                    $"previous={previousValue:F4}, current={volume.Value:F4}, " +
                    $"low={low.position}, high={high.position}, pointer={step.position}");
                Assert.That(radio.OutputVolume, Is.GreaterThan(previousOutput));
                previousValue = volume.Value;
                previousOutput = radio.OutputVolume;
                if (index == 5)
                {
                    Assert.That(volume.Value, Is.EqualTo(.5f).Within(.03f));
                    AssertPointerAligned(volume, volumePointer, volumeLabel);
                }
            }
            ExecuteEvents.Execute(volume.gameObject, high, ExecuteEvents.pointerUpHandler);
            Assert.That(volume.Value, Is.GreaterThan(.97f));
            AssertPointerAligned(volume, volumePointer, highLabel);

            var offLabel = renderers.Single(value => value.name == "Radio_Label_Off");
            var onLabel = renderers.Single(value => value.name == "Radio_Label_On");
            var socket = renderers.Single(value => value.name == "Radio_PowerSwitch_Socket");
            var beforeOn = Vector3.Distance(power.SwitchPivot.position, onLabel.bounds.center);
            var beforeOff = Vector3.Distance(power.SwitchPivot.position, offLabel.bounds.center);
            radio.SetPower(true);
            yield return Wait(.15f);
            Assert.That(Vector3.Distance(power.SwitchPivot.position, onLabel.bounds.center),
                Is.LessThan(beforeOn));
            Assert.That(Vector3.Distance(power.SwitchPivot.position, offLabel.bounds.center),
                Is.GreaterThan(beforeOff));
            var slider = power.SwitchPivot.GetComponentInChildren<Renderer>().bounds;
            var switchDirection = (onLabel.bounds.center - offLabel.bounds.center).normalized;
            var socketCenter = Vector3.Dot(socket.bounds.center, switchDirection);
            var socketHalf = ProjectedExtent(socket.bounds.extents, switchDirection);
            var sliderCenter = Vector3.Dot(slider.center, switchDirection);
            var sliderHalf = ProjectedExtent(slider.extents, switchDirection);
            Assert.That(sliderCenter - sliderHalf,
                Is.GreaterThanOrEqualTo(socketCenter - socketHalf - .0001f));
            Assert.That(sliderCenter + sliderHalf,
                Is.LessThanOrEqualTo(socketCenter + socketHalf + .0001f),
                "The powered switch visual must stay inside its socket.");

            var window = renderers.Single(value => value.name == "Radio_Frequency_Window");
            var needle = renderers.Single(value => value.name == "Radio_DialNeedle");
            tuning.SetNormalizedValue(0, false);
            yield return null;
            AssertNeedleInside(window.bounds, needle.bounds);
            Assert.That(Vector3.Distance(tuning.LinkedIndicator.localPosition,
                tuning.IndicatorMinimumLocal), Is.LessThan(.00001f));
            tuning.SetNormalizedValue(1, false);
            yield return null;
            AssertNeedleInside(window.bounds, needle.bounds);
            Assert.That(Vector3.Distance(tuning.LinkedIndicator.localPosition,
                tuning.IndicatorMaximumLocal), Is.LessThan(.00001f));
            tuning.SetNormalizedValue(.5f, false);
            Assert.That(Vector3.Distance(tuning.LinkedIndicator.localPosition,
                Vector3.Lerp(tuning.IndicatorMinimumLocal,
                    tuning.IndicatorMaximumLocal, .5f)), Is.LessThan(.00001f));
        }

        [Test]
        public void ProductionAssetHasThreeStreamingStationsAndCompleteXrControls()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                RadioBuilder.DefinitionPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id, Is.EqualTo("radio"));
            Assert.That(definition.CatalogVisible, Is.True);
            Assert.That(definition.AllowSurfacePlacement, Is.True);
            var radioPrefab = definition.Prefab.GetComponent<RadioController>();
            Assert.That(radioPrefab, Is.Not.Null);
            Assert.That(radioPrefab.Stations.Count, Is.EqualTo(3));
            Assert.That(radioPrefab.Stations.Select(clip => clip.length),
                Is.EqualTo(new[] { 37f, 90f, 90f }).Within(.05f));
            foreach (var clip in radioPrefab.Stations)
            {
                Assert.That(clip.channels, Is.EqualTo(1));
                var importer = (AudioImporter)AssetImporter.GetAtPath(
                    AssetDatabase.GetAssetPath(clip));
                Assert.That(importer.defaultSampleSettings.loadType,
                    Is.EqualTo(AudioClipLoadType.Streaming));
                Assert.That(importer.defaultSampleSettings.compressionFormat,
                    Is.EqualTo(AudioCompressionFormat.Vorbis));
            }
            Assert.That(radioPrefab.NoiseChannel.clip, Is.Not.Null);
            Assert.That(radioPrefab.GetComponentsInChildren<RadioPowerButton>(true), Has.Length.EqualTo(1));
            Assert.That(radioPrefab.GetComponentsInChildren<RadioKnobControl>(true), Has.Length.EqualTo(2));
            var focusTargets =
                radioPrefab.GetComponentsInChildren<ItemInteractionFocusTarget>(true);
            Assert.That(focusTargets, Has.Length.EqualTo(3));
            Assert.That(focusTargets.Select(target => target.Control),
                Is.EquivalentTo(radioPrefab.GetComponentsInChildren<Selectable>(true)));
            Assert.That(focusTargets.All(target =>
                target.FocusMaterial == definition.FocusMaterial &&
                target.Renderers.Count > 0), Is.True);
            Assert.That(radioPrefab.GetComponentsInChildren<TrackedDeviceGraphicRaycaster>(true),
                Has.Length.EqualTo(3));
            Assert.That(radioPrefab.GetComponentsInChildren<GraphicRaycaster>(true),
                Has.Length.EqualTo(3));
            Assert.That(radioPrefab.transform.Find("Model"), Is.Not.Null);
        }

        private static IEnumerator Wait(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }

        private PointerEventData PointerAtWorld(Vector3 worldTarget)
        {
            return new PointerEventData(events)
            {
                pointerId = 13,
                button = PointerEventData.InputButton.Left,
                position = camera.WorldToScreenPoint(worldTarget)
            };
        }

        private static void AssertNeedleInside(Bounds window, Bounds needle)
        {
            Assert.That(needle.min.x, Is.GreaterThanOrEqualTo(window.min.x - .0001f));
            Assert.That(needle.max.x, Is.LessThanOrEqualTo(window.max.x + .0001f));
        }

        private static float ProjectedExtent(Vector3 extents, Vector3 direction)
            => Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;

        private static void AssertPointerAligned(RadioKnobControl knob, Renderer pointer,
            Renderer label)
        {
            var control = (RectTransform)knob.transform;
            var pointerPoint = control.InverseTransformPoint(pointer.bounds.center);
            var labelPoint = control.InverseTransformPoint(label.bounds.center);
            var pointerAngle = Mathf.Atan2(pointerPoint.x, pointerPoint.y) * Mathf.Rad2Deg;
            var labelAngle = Mathf.Atan2(labelPoint.x, labelPoint.y) * Mathf.Rad2Deg;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(pointerAngle, labelAngle)), Is.LessThan(12f),
                $"Knob pointer {pointerAngle:F1} must align with label {labelAngle:F1}.");
        }
    }
}
