using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class CoffeeGrinderTests
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
        public void ProductionAssetsKeepGroupedKitchenwareAndReceiverContracts()
        {
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.GroupDefinitionPath);
            var mortar = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.MortarDefinitionPath);
            var pestle = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.PestleDefinitionPath);
            Assert.That(group, Is.Not.Null);
            Assert.That(mortar, Is.Not.Null);
            Assert.That(pestle, Is.Not.Null);
            Assert.That(group.Id, Is.EqualTo("coffee-grinder"));
            Assert.That(group.DisplayName, Is.EqualTo("咖啡研磨套装"));
            Assert.That(group.Category, Is.EqualTo(ItemCategory.Kitchenware));
            Assert.That(group.CatalogVisible, Is.True);
            Assert.That(group.InventoryLimit, Is.EqualTo(1));
            Assert.That(group.IsPlacementGroup, Is.True);
            Assert.That(group.PlacementParts.Select(part => part.Definition.Id),
                Is.EquivalentTo(new[]
                {
                    "coffee-grinder-mortar", "coffee-grinder-pestle"
                }));
            Assert.That(group.PlacementParts.All(
                part => !part.Definition.CatalogVisible), Is.True);

            var controller =
                mortar.Prefab.GetComponent<CoffeeGrinderController>();
            var receiver =
                mortar.Prefab.GetComponentInChildren<CoffeeGrinderReceiver>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(receiver, Is.Not.Null);
            Assert.That(receiver.Grinder, Is.SameAs(controller));
            Assert.That(receiver.GetComponent<Collider>().isTrigger, Is.True);
            Assert.That(controller.RequiredStirSeconds, Is.EqualTo(4f));
            Assert.That(controller.BeanVolume.Capacity, Is.EqualTo(336));
            Assert.That(controller.PowderVisual.activeSelf, Is.False);
            var particles =
                mortar.Prefab.GetComponentInChildren<ParticleSystem>(true);
            Assert.That(particles, Is.Not.Null);
            Assert.That(particles.main.playOnAwake, Is.False);
            Assert.That(particles.emission.burstCount, Is.EqualTo(1));
            var bursts = new ParticleSystem.Burst[1];
            particles.emission.GetBursts(bursts);
            Assert.That(bursts[0].maxCount, Is.EqualTo(72));
            Assert.That(pestle.Prefab.GetComponent<CoffeePestle>(), Is.Not.Null);
            Assert.That(pestle.Prefab.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1));

            foreach (var path in new[]
                     {
                         CoffeeBeanJarBuilder.PrefabPath,
                         "Assets/MateSoul/Content/Props/Bowl.prefab",
                         "Assets/MateSoul/Content/Props/Mug.prefab"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab.GetComponentInChildren<CoffeeGrinderReceiver>(true),
                    Is.Null, $"{path} must not receive coffee beans.");
            }
        }

        [UnityTest]
        public IEnumerator AcceptedBeansPersistUntilFourSecondsOfGrinding()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Coffee Grinder Integration");
            var mortarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeGrinderBuilder.MortarPrefabPath);
            var beanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            Assert.That(mortarPrefab, Is.Not.Null);
            Assert.That(beanPrefab, Is.Not.Null);

            var mortar = Object.Instantiate(mortarPrefab, root.transform);
            var grinder = mortar.GetComponent<CoffeeGrinderController>();
            var notifications = new SystemNotifications();
            grinder.BindNotifications(notifications);
            var beanObject = Object.Instantiate(beanPrefab, root.transform);
            beanObject.SetActive(true);
            var bean = beanObject.GetComponent<CoffeeBean>();
            bean.Launch(new Pose(Vector3.up, Quaternion.identity),
                Vector3.zero, Vector3.zero);

            Assert.That(grinder.TryAcceptBean(bean), Is.True);
            Assert.That(grinder.ReceivedBeanCount, Is.EqualTo(1));
            Assert.That(grinder.BeanVolume.VisibleCount, Is.EqualTo(1));
            Assert.That(bean.IsCaptured, Is.True);
            grinder.AdvanceGrindingForTesting(3.99f, true);
            Assert.That(grinder.HasPowder, Is.False);
            Assert.That(grinder.ReceivedBeanCount, Is.EqualTo(1));
            grinder.AdvanceGrindingForTesting(.01f, false);
            Assert.That(grinder.HasPowder, Is.False,
                "Stationary pestle time must not count as grinding.");
            grinder.AdvanceGrindingForTesting(.01f, true);

            Assert.That(grinder.ReceivedBeanCount, Is.Zero);
            Assert.That(grinder.GroundBeanCount, Is.EqualTo(1));
            Assert.That(grinder.BeanVolume.VisibleCount, Is.Zero);
            Assert.That(grinder.PowderVisual.activeSelf, Is.True);
            Assert.That(notifications.Hint, Is.EqualTo("咖啡粉磨制完成"));
            yield return null;
        }
    }
}
