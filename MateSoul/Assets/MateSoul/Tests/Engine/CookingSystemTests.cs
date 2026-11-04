using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class CookingSystemTests
    {
        private GameObject root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            if (!Application.isPlaying) yield break;
            yield return null;
            yield return new ExitPlayMode();
        }

        [Test]
        public void FormulaThresholdsAndContactAreaAreDeterministic()
        {
            Assert.That(CookableFood.CalculateDonenessGain(60, 50),
                Is.EqualTo(50).Within(.0001f));
            Assert.That(CookableFood.CalculateDonenessGain(100, 50),
                Is.EqualTo(100).Within(.0001f));
            Assert.That(CookableFood.Classify(49.99f), Is.EqualTo(FoodDoneness.Raw));
            Assert.That(CookableFood.Classify(50), Is.EqualTo(FoodDoneness.Medium));
            Assert.That(CookableFood.Classify(70), Is.EqualTo(FoodDoneness.MediumWell));
            Assert.That(CookableFood.Classify(100), Is.EqualTo(FoodDoneness.WellDone));
            Assert.That(CookableFood.Classify(100.01f), Is.EqualTo(FoodDoneness.Burned));

            var panArea = Mathf.PI * .112f * .112f;
            Assert.That(HeatingBaseController.CircleOverlapArea(.115f, .112f, 0),
                Is.EqualTo(panArea).Within(.000001f));
            Assert.That(HeatingBaseController.CircleOverlapArea(.115f, .112f, .3f),
                Is.Zero);
        }

        [Test]
        public void ProductionAssetsExposeOnlyRawSteakAsCookable()
        {
            var baseDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                HeatingBaseBuilder.DefinitionPath);
            var panDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/FryingPanBody.asset");
            var rawSteak = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/RawSteak.asset");
            var cupcake = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Cupcake.asset");

            Assert.That(baseDefinition, Is.Not.Null);
            Assert.That(baseDefinition.Id, Is.EqualTo("heating-base"));
            Assert.That(baseDefinition.Category, Is.EqualTo(ItemCategory.Appliance));
            Assert.That(baseDefinition.FixedAfterPlacement, Is.True);
            Assert.That(baseDefinition.ImmovableAfterPlacement, Is.False);
            Assert.That(baseDefinition.PlacementPreview, Is.False);
            Assert.That(baseDefinition.AllowSurfacePlacement, Is.True);
            Assert.That(baseDefinition.Footprint,
                Is.EqualTo(new Vector2(.330f, .3135f)));
            var controller =
                baseDefinition.Prefab.GetComponent<HeatingBaseController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.HeatingRadius, Is.EqualTo(.115f));
            Assert.That(controller.ButtonAudio, Is.Not.Null);
            Assert.That(controller.OperatingAudio, Is.Not.Null);
            Assert.That(controller.OperatingAudio.loop, Is.True);
            Assert.That(controller.OperatingAudio.clip, Is.Not.Null);
            var heatingSurface = baseDefinition.Prefab
                .GetComponentsInChildren<Renderer>(true)
                .Single(value => value.name == "HeatingBase_HeatingSurface");
            Assert.That(controller.HeatingSurface.position.y,
                Is.EqualTo(heatingSurface.bounds.max.y).Within(.0001f),
                "Heat contact must use the visible plate top, not the FBX pivot.");
            Assert.That(baseDefinition.Prefab
                .GetComponentsInChildren<HeatingBaseButton>(true), Has.Length.EqualTo(3));
            Assert.That(baseDefinition.Prefab
                .GetComponentsInChildren<TrackedDeviceGraphicRaycaster>(true),
                Has.Length.EqualTo(3));
            Assert.That(baseDefinition.Prefab
                .GetComponentsInChildren<GraphicRaycaster>(true), Has.Length.EqualTo(3));
            Assert.That(baseDefinition.Prefab
                .GetComponentsInChildren<TMP_Text>(true), Has.Length.EqualTo(3));
            Assert.That(baseDefinition.Prefab
                .GetComponentsInChildren<Image>(true)
                .SingleOrDefault(value => value.name == "Power Icon"), Is.Not.Null);
            var displayUi = baseDefinition.Prefab
                .GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "Temperature Display UI");
            var displaySurface = baseDefinition.Prefab
                .GetComponentsInChildren<Renderer>(true)
                .Single(value =>
                    value.name == "HeatingBase_TemperatureDisplay");
            var outward = Vector3.ProjectOnPlane(
                displaySurface.bounds.center -
                baseDefinition.Prefab.transform.position,
                baseDefinition.Prefab.transform.up).normalized;
            Assert.That(Vector3.Dot(-displayUi.forward, outward),
                Is.GreaterThan(.99f),
                "The world-space canvas front must face away from the base.");
            AssertControlSurfaceGap(baseDefinition.Prefab,
                "Temperature Display UI", "HeatingBase_TemperatureDisplay");
            AssertControlSurfaceGap(baseDefinition.Prefab,
                "Power Button", "HeatingBase_PowerButton_Cap");
            AssertControlSurfaceGap(baseDefinition.Prefab,
                "Temperature Minus", "HeatingBase_MinusButton_Cap");
            AssertControlSurfaceGap(baseDefinition.Prefab,
                "Temperature Plus", "HeatingBase_PlusButton_Cap");

            Assert.That(panDefinition.Prefab.GetComponent<CookwareHeatReceiver>(),
                Is.Not.Null);
            Assert.That(rawSteak.Cooking.enabled, Is.True);
            var food = rawSteak.Prefab.GetComponent<CookableFood>();
            Assert.That(food, Is.Not.Null);
            Assert.That(food.RawVisualRoot, Is.Not.Null);
            Assert.That(food.CookedVisualRoot, Is.Not.Null);
            Assert.That(food.RawVisualRoot.transform.localPosition,
                Is.EqualTo(food.CookedVisualRoot.transform.localPosition));
            Assert.That(food.RawVisualRoot.transform.localRotation,
                Is.EqualTo(food.CookedVisualRoot.transform.localRotation));
            Assert.That(food.RawVisualRoot.transform.localScale,
                Is.EqualTo(food.CookedVisualRoot.transform.localScale));
            Assert.That(food.RawVisualRoot.activeSelf, Is.True);
            Assert.That(food.CookedVisualRoot.activeSelf, Is.False);
            Assert.That(rawSteak.Prefab
                .GetComponent<CookingSpontaneousReplyTrigger>(), Is.Not.Null);
            var effects = rawSteak.Prefab.GetComponent<CookingEffects>();
            Assert.That(effects, Is.Not.Null);
            Assert.That(effects.SizzleClip, Is.Not.Null);
            Assert.That(effects.SmokeMaterial, Is.Not.Null);
            Assert.That(cupcake.Cooking.enabled, Is.False);
            Assert.That(cupcake.Prefab.GetComponent<CookableFood>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator ButtonsClampTemperatureAndAutomaticallyRebound()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Heating Base Button Integration");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HeatingBaseBuilder.PrefabPath);
            var instance = Object.Instantiate(prefab, root.transform);
            var controller = instance.GetComponent<HeatingBaseController>();
            var buttons = instance.GetComponentsInChildren<HeatingBaseButton>(true);
            var power = buttons.Single(value => value.Kind == HeatingBaseButtonKind.Power);
            var increase =
                buttons.Single(value => value.Kind == HeatingBaseButtonKind.Increase);
            var decrease =
                buttons.Single(value => value.Kind == HeatingBaseButtonKind.Decrease);
            var events = new GameObject("Heating Base Test Events")
                .AddComponent<EventSystem>();
            events.transform.SetParent(root.transform);
            var pointer = new PointerEventData(events)
            {
                pointerId = 71,
                button = PointerEventData.InputButton.Left
            };

            Assert.That(controller.IsPoweredOn, Is.False);
            Assert.That(controller.Temperature, Is.EqualTo(60));
            Assert.That(increase.IsInteractable(), Is.False);
            ExecuteEvents.Execute(power.gameObject, pointer,
                ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(power.gameObject, pointer,
                ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(power.gameObject, pointer,
                ExecuteEvents.pointerClickHandler);
            Assert.That(controller.IsPoweredOn, Is.True);
            Assert.That(controller.ButtonSoundPlayCount, Is.EqualTo(1));
            Assert.That(controller.IsOperating, Is.False,
                "The induction loop must wait for valid cookware contact.");
            Assert.That(instance.GetComponentsInChildren<TMP_Text>(true)
                    .Single(value => value.transform.parent.name ==
                                     "Temperature Display UI").text,
                Is.EqualTo("60°C"));
            power.AdvanceAnimationForTesting(.05f);
            Assert.That(power.PressAmount, Is.GreaterThan(.6f));

            for (var index = 0; index < 8; index++)
                ExecuteEvents.Execute(increase.gameObject, pointer,
                    ExecuteEvents.pointerClickHandler);
            Assert.That(controller.Temperature, Is.EqualTo(100));
            Assert.That(increase.IsInteractable(), Is.False);
            for (var index = 0; index < 10; index++)
                ExecuteEvents.Execute(decrease.gameObject, pointer,
                    ExecuteEvents.pointerClickHandler);
            Assert.That(controller.Temperature, Is.EqualTo(40));
            Assert.That(decrease.IsInteractable(), Is.False);
            for (var index = 0; index < 6; index++)
                power.AdvanceAnimationForTesting(.05f);
            Assert.That(power.PressAmount, Is.EqualTo(0).Within(.04f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PanContactAndContainmentGateCookingWithoutPerFrameMeshChecks()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Cooking Contact Integration");
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HeatingBaseBuilder.PrefabPath);
            var panDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/FryingPanBody.asset");
            var steakDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/RawSteak.asset");
            var baseObject = Object.Instantiate(basePrefab, root.transform);
            var panObject = Object.Instantiate(panDefinition.Prefab, root.transform);
            var steakObject = Object.Instantiate(steakDefinition.Prefab, root.transform);
            baseObject.GetComponent<Rigidbody>().isKinematic = true;
            panObject.GetComponent<Rigidbody>().isKinematic = true;
            steakObject.GetComponent<Rigidbody>().isKinematic = true;
            var controller = baseObject.GetComponent<HeatingBaseController>();
            var receiver = panObject.GetComponent<CookwareHeatReceiver>();
            var food = steakObject.GetComponent<CookableFood>();
            food.Configure(steakDefinition.Cooking);

            Physics.SyncTransforms();
            panObject.transform.position +=
                controller.HeatingSurface.position - receiver.ContactCenter;
            Physics.SyncTransforms();
            var foodOffset = food.WorldCenter - steakObject.transform.position;
            steakObject.transform.position =
                receiver.ContactCenter + receiver.ContactNormal * .022f +
                panObject.transform.right * .075f - foodOffset;
            Physics.SyncTransforms();

            controller.SetPower(true);
            controller.RefreshContactForTesting();
            Assert.That(controller.ActiveReceiver, Is.SameAs(receiver));
            Assert.That(controller.IsOperating, Is.True);
            Assert.That(controller.OperatingAudio.loop, Is.True);
            Assert.That(controller.CurrentContactRatio,
                Is.GreaterThanOrEqualTo(controller.MinimumContactRatio));
            Assert.That(receiver.Contains(food), Is.True);
            receiver.AdvanceForTesting(50f);
            Assert.That(food.IsHeating, Is.True);
            Assert.That(food.Effects.SizzleSource.loop, Is.True);
            Assert.That(food.Effects.SizzleSource.clip, Is.Not.Null);
            Assert.That(food.Effects.Smoke, Is.Not.Null);
            Assert.That(food.Effects.Smoke.emission.enabled, Is.True);
            Assert.That(food.Doneness, Is.EqualTo(50).Within(.01f));
            Assert.That(food.Stage, Is.EqualTo(FoodDoneness.Medium));
            Assert.That(food.RawVisualRoot.activeSelf, Is.False);
            Assert.That(food.CookedVisualRoot.activeSelf, Is.True);
            Assert.That(food.RawVisualRoot.transform.localPosition,
                Is.EqualTo(food.CookedVisualRoot.transform.localPosition));
            Assert.That(food.RawVisualRoot.transform.localRotation,
                Is.EqualTo(food.CookedVisualRoot.transform.localRotation));

            steakObject.transform.position += Vector3.right * .25f;
            Physics.SyncTransforms();
            receiver.AdvanceForTesting(10f);
            Assert.That(food.Doneness, Is.EqualTo(50).Within(.01f),
                "Food outside the pan must not receive heat.");
            Assert.That(food.IsHeating, Is.False);
            Assert.That(food.Effects.Smoke.emission.enabled, Is.False);

            panObject.transform.position += Vector3.right * .20f;
            Physics.SyncTransforms();
            controller.RefreshContactForTesting();
            Assert.That(controller.ActiveReceiver, Is.Null);
            Assert.That(controller.IsOperating, Is.False);
            Assert.That(receiver.IsHeated, Is.False);
        }

        private static IEnumerator Wait(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static void AssertControlSurfaceGap(
            GameObject rootObject, string panelName, string rendererName)
        {
            var panel = rootObject.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == panelName);
            var renderer = rootObject.GetComponentsInChildren<Renderer>(true)
                .Single(value => value.name == rendererName);
            var panelLocal = rootObject.transform.InverseTransformPoint(
                panel.position);
            var centerLocal = rootObject.transform.InverseTransformPoint(
                renderer.bounds.center);
            var minimumLocal = rootObject.transform.InverseTransformPoint(
                renderer.bounds.min);
            var maximumLocal = rootObject.transform.InverseTransformPoint(
                renderer.bounds.max);
            var surface = centerLocal.z < 0
                ? minimumLocal.z
                : maximumLocal.z;
            Assert.That(Mathf.Abs(panelLocal.z - surface),
                Is.EqualTo(.0006f).Within(.00015f),
                $"{panelName} must sit flush with {rendererName}.");
        }
    }
}
