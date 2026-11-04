using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class WaterDispenserTests
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
        public void ProductionAssetsKeepFurnitureAndReceiverContracts()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                WaterDispenserBuilder.DefinitionPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id, Is.EqualTo("water-dispenser"));
            Assert.That(definition.DisplayName, Is.EqualTo("饮水机"));
            Assert.That(definition.Category, Is.EqualTo(ItemCategory.Furniture));
            Assert.That(definition.CatalogVisible, Is.True);
            Assert.That(definition.InventoryLimit, Is.EqualTo(1));
            Assert.That(definition.FixedAfterPlacement, Is.True);
            Assert.That(definition.ImmovableAfterPlacement, Is.True);
            Assert.That(definition.PlacementPreview, Is.True);
            Assert.That(definition.AllowSurfacePlacement, Is.False);
            Assert.That(definition.Footprint,
                Is.EqualTo(new Vector2(.34f, .372f)));
            Assert.That(definition.PlacementHeight, Is.EqualTo(1.4f));
            Assert.That(definition.Physics.mass, Is.EqualTo(18f));

            var prefab = definition.Prefab;
            Assert.That(prefab.GetComponent<WaterDispenserController>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<WaterDispenserButton>(true),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<TrackedDeviceGraphicRaycaster>(true),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<GraphicRaycaster>(true),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<NavMeshObstacle>(true),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<BoxCollider>(true),
                Has.Length.EqualTo(4));
            var water = prefab.GetComponent<WaterDispenserController>();
            var button = prefab.GetComponentInChildren<WaterDispenserButton>(true);
            var buttonVisual = prefab.GetComponentsInChildren<Renderer>(true)
                .Single(renderer => renderer.name == "WaterDispenser_DropButton");
            var dropIcon = prefab.GetComponentsInChildren<Renderer>(true)
                .Single(renderer => renderer.name == "WaterDispenser_DropIcon");
            var iconIsPositiveZ = dropIcon.bounds.center.z >
                                  buttonVisual.bounds.center.z;
            var reliefGap = iconIsPositiveZ
                ? dropIcon.bounds.min.z - buttonVisual.bounds.max.z
                : buttonVisual.bounds.min.z - dropIcon.bounds.max.z;
            Assert.That(reliefGap, Is.GreaterThanOrEqualTo(.0004f),
                "The drop icon must be a separated relief, not coplanar with the button.");
            var focus = button.GetComponent<ItemInteractionFocusTarget>();
            Assert.That(focus, Is.Not.Null);
            Assert.That(focus.Control, Is.SameAs(button));
            Assert.That(focus.FocusMaterial, Is.SameAs(definition.FocusMaterial));
            Assert.That(focus.Renderers, Is.EqualTo(new[] { buttonVisual }));
            var buttonControl = (RectTransform)button.transform;
            Assert.That(Vector3.Distance(
                    buttonControl.position, buttonVisual.bounds.center),
                Is.LessThan(.025f),
                "The XR hit area must be centered over the visible button.");
            Assert.That(buttonControl.rect.size,
                Is.EqualTo(new Vector2(78, 52)));
            Assert.That(water.PuddlePoolSize, Is.EqualTo(6));
            Assert.That(water.Outlet.name, Is.EqualTo("WaterDispenser_Nozzle_Outlet"));
            Assert.That(water.WaterStream.GetComponent<Collider>(), Is.Null);
            Assert.That(water.WaterStream.GetComponent<Rigidbody>(), Is.Null);

            var receiverPaths = new[]
            {
                "Mug", "Bowl", "BowlMedium", "BowlLarge",
                "Plate", "PlateMedium", "PlateLarge"
            };
            foreach (var stem in receiverPaths)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"Assets/MateSoul/Content/Props/{stem}.asset");
                Assert.That(item, Is.Not.Null, stem);
                var receiver = item.Prefab.GetComponent<WaterReceiver>();
                Assert.That(receiver, Is.Not.Null, stem);
                Assert.That(receiver.CapacityLiters, Is.GreaterThan(0), stem);
                Assert.That(receiver.OpeningRadius, Is.GreaterThan(.005f), stem);
                Assert.That(receiver.WaterVisual, Is.Not.Null, stem);
                Assert.That(receiver.WaterVisual.GetComponent<Collider>(), Is.Null, stem);
                Assert.That(receiver.WaterVisual.gameObject.activeSelf, Is.False, stem);
            }
        }

        [UnityTest]
        public IEnumerator ButtonFillsMugAndPooledSpillEvaporates()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Water Dispenser Integration");
            var dispenserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WaterDispenserBuilder.PrefabPath);
            var mugDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            Assert.That(dispenserPrefab, Is.Not.Null);
            Assert.That(mugDefinition, Is.Not.Null);

            var dispenserObject = Object.Instantiate(
                dispenserPrefab, root.transform);
            var dispenser = dispenserObject.GetComponent<WaterDispenserController>();
            var button = dispenserObject.GetComponentInChildren<WaterDispenserButton>();
            var mug = Object.Instantiate(mugDefinition.Prefab, root.transform);
            var receiver = mug.GetComponent<WaterReceiver>();
            Assert.That(dispenser, Is.Not.Null);
            Assert.That(button, Is.Not.Null);
            Assert.That(receiver, Is.Not.Null);

            var openingTarget = dispenser.Outlet.position + Vector3.down * .14f;
            mug.transform.position = openingTarget - receiver.OpeningCenter;
            Physics.SyncTransforms();

            var events = new GameObject("Water Button Events").AddComponent<EventSystem>();
            events.transform.SetParent(root.transform);
            var releasedPosition = button.ButtonPivot.localPosition;
            var pointer = new PointerEventData(events)
            {
                pointerId = 31,
                button = PointerEventData.InputButton.Left
            };
            ExecuteEvents.Execute(button.gameObject, pointer,
                ExecuteEvents.pointerDownHandler);
            yield return Wait(.06f);
            Assert.That(button.PressAmount, Is.GreaterThan(.8f));
            Assert.That(Vector3.Distance(
                    button.ButtonPivot.localPosition, releasedPosition),
                Is.GreaterThan(.0035f));
            ExecuteEvents.Execute(button.gameObject, pointer,
                ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer,
                ExecuteEvents.pointerClickHandler);
            Assert.That(dispenser.IsDispensing, Is.True);
            yield return Wait(.08f);
            Assert.That(button.PressAmount, Is.GreaterThan(.8f),
                "A fast click must still complete its visible press pulse.");
            yield return Wait(.18f);
            Assert.That(button.PressAmount, Is.EqualTo(0).Within(.03f));
            Assert.That(Vector3.Distance(
                    button.ButtonPivot.localPosition, releasedPosition),
                Is.LessThan(.0001f));

            receiver.Empty();
            dispenser.AdvanceForTesting(1f);
            Assert.That(receiver.CurrentLiters,
                Is.EqualTo(dispenser.FlowLitersPerSecond).Within(.015f));
            Assert.That(receiver.WaterVisual.gameObject.activeSelf, Is.True);
            Assert.That(dispenser.WaterStream.gameObject.activeSelf, Is.True);

            for (var index = 0; index < 4; index++)
                dispenser.AdvanceForTesting(1f);
            Assert.That(receiver.CurrentLiters,
                Is.EqualTo(receiver.CapacityLiters).Within(.001f));
            Assert.That(receiver.FillRatio, Is.EqualTo(1f).Within(.001f));
            Assert.That(dispenser.PuddlePoolSize, Is.EqualTo(6));
            Assert.That(dispenser.ActivePuddleCount,
                Is.GreaterThan(0).And.LessThanOrEqualTo(6));

            ExecuteEvents.Execute(button.gameObject, pointer,
                ExecuteEvents.pointerClickHandler);
            Assert.That(dispenser.IsDispensing, Is.False);
            Assert.That(dispenser.WaterStream.gameObject.activeSelf, Is.False);
            dispenser.AdvanceForTesting(3f);
            Assert.That(dispenser.ActivePuddleCount, Is.Zero);
            Assert.That(dispenser.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1),
                "Water rendering must not allocate droplet rigidbodies.");
            Assert.That(dispenser.GetComponentsInChildren<Renderer>(true)
                    .Count(renderer => renderer.name.StartsWith("Pooled Water Spot")),
                Is.EqualTo(6));
            yield return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
