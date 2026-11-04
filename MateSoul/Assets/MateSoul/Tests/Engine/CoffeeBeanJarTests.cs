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
    public sealed class CoffeeBeanJarTests
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
        public void ProductionAssetKeepsFiniteCapacityAndSingleBodyContracts()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeBeanJarBuilder.DefinitionPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id, Is.EqualTo("coffee-bean-jar"));
            Assert.That(definition.DisplayName, Is.EqualTo("咖啡豆罐"));
            Assert.That(definition.Category, Is.EqualTo(ItemCategory.Kitchenware));
            Assert.That(definition.CatalogVisible, Is.True);
            Assert.That(definition.InventoryLimit, Is.EqualTo(2));
            Assert.That(definition.AllowSurfacePlacement, Is.True);
            Assert.That(definition.Footprint, Is.EqualTo(new Vector2(.12f, .12f)));
            Assert.That(definition.PlacementHeight, Is.EqualTo(.194f));

            var prefab = definition.Prefab;
            var controller = prefab.GetComponent<CoffeeBeanJarController>();
            var lid = prefab.GetComponentInChildren<CoffeeBeanJarLid>(true);
            var interactable =
                prefab.GetComponentInChildren<CoffeeBeanJarLidInteractable>(true);
            Assert.That(controller, Is.Not.Null);
            Assert.That(lid, Is.Not.Null);
            Assert.That(interactable, Is.Not.Null);
            Assert.That(interactable.Lid, Is.SameAs(lid));
            Assert.That(controller.BeanCapacity, Is.EqualTo(336));
            Assert.That(controller.RemainingBeanCount, Is.EqualTo(336));
            Assert.That(controller.BeansPerRelease, Is.EqualTo(8));
            Assert.That(controller.PhysicalBeansPerRelease, Is.EqualTo(2));
            Assert.That(controller.MaximumPhysicalBeans, Is.EqualTo(24));
            Assert.That(controller.BeanFill.Capacity, Is.EqualTo(720));
            Assert.That(controller.BeanFill.VisibleCount, Is.EqualTo(720));
            Assert.That(controller.BeanFill.UsesGpuInstancing, Is.True);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(2),
                "The detachable lid must own an independent Rigidbody.");
            Assert.That(lid.GetComponent<PhysicsBody>(), Is.Not.Null);
            Assert.That(lid.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(interactable.gameObject, Is.SameAs(lid.gameObject));
            Assert.That(lid.PhysicalCollider.isTrigger, Is.False);
            Assert.That(prefab.GetComponentsInChildren<ItemInteractionFocusTarget>(true),
                Has.Length.EqualTo(1));

            var bean = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            Assert.That(bean, Is.Not.Null);
            Assert.That(bean.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1));
            Assert.That(bean.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(1));
            Assert.That(bean.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(1),
                "Every poured coffee bean must be one independent model.");
        }

        [UnityTest]
        public IEnumerator LidDetachesSnapsAndPouringUsesBoundedPhysicalPool()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Coffee Bean Jar Integration");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var jarObject = Object.Instantiate(prefab, root.transform);
            var jar = jarObject.GetComponent<CoffeeBeanJarController>();
            var lid = jarObject.GetComponent<CoffeeBeanJarLid>();
            if (lid == null)
                lid = root.GetComponentInChildren<CoffeeBeanJarLid>(true);
            Assert.That(jar, Is.Not.Null);
            Assert.That(lid, Is.Not.Null);
            Assert.That(lid.IsAttached, Is.True);
            Assert.That(lid.transform.parent, Is.SameAs(root.transform),
                "The lid must leave the jar's compound-body hierarchy at runtime.");

            Assert.That(jar.ApplyLidImpulse(lid.ForceThreshold - .01f), Is.False);
            Assert.That(lid.IsAttached, Is.True);
            Assert.That(jar.ApplyLidImpulse(lid.ForceThreshold), Is.True);
            Assert.That(lid.IsOpen, Is.True);

            var lidBody = lid.GetComponent<PhysicsBody>();
            lidBody.Rigidbody.velocity = Vector3.zero;
            lidBody.Rigidbody.angularVelocity = Vector3.zero;
            lidBody.Place(new Pose(
                lid.Socket.position + lid.Socket.right * (lid.SnapDistance * .9f),
                lid.Socket.rotation));
            lid.AdvanceForTesting(.36f);
            Assert.That(lid.IsAttached, Is.True,
                "A released, aligned lid close to the rim must reattach.");

            Assert.That(jar.ApplyLidImpulse(lid.ForceThreshold), Is.True);
            jar.AdvanceForTesting(.31f, true);
            Assert.That(jar.ReleasedBeanCount, Is.EqualTo(40));
            Assert.That(jar.FillRatio, Is.EqualTo(296f / 336f).Within(.001f));
            Assert.That(jar.BeanFill.VisibleCount, Is.EqualTo(634));
            Assert.That(jar.ReleasedBeans, Has.Count.EqualTo(10));
            jar.AdvanceForTesting(5f, true);
            Assert.That(jar.ReleasedBeanCount, Is.EqualTo(336));
            Assert.That(jar.RemainingBeanCount, Is.Zero);
            Assert.That(jar.FillRatio, Is.Zero);
            Assert.That(jar.BeanFill.gameObject.activeSelf, Is.False);
            jar.AdvanceForTesting(20f, true);
            Assert.That(jar.ReleasedBeanCount, Is.EqualTo(336),
                "An empty jar must never generate replacement beans.");

            var beans = root.GetComponentsInChildren<CoffeeBean>(true);
            Assert.That(beans, Has.Length.EqualTo(jar.MaximumPhysicalBeans),
                "The complete logical capacity must reuse a fixed physical pool.");
            Assert.That(beans.Sum(bean =>
                    bean.GetComponentsInChildren<Rigidbody>(true).Length),
                Is.EqualTo(jar.MaximumPhysicalBeans));
            Assert.That(beans.Sum(bean =>
                    bean.GetComponentsInChildren<Collider>(true).Length),
                Is.EqualTo(jar.MaximumPhysicalBeans));
            Assert.That(beans.All(bean =>
                    bean.GetComponentsInChildren<Renderer>(true).Length == 1),
                Is.True);
            Assert.That(jar.ReleasedBeans,
                Has.Count.LessThanOrEqualTo(jar.MaximumPhysicalBeans));
            var releasedBean = jar.ReleasedBeans[0];
            releasedBean.MarkLanded();
            releasedBean.AdvanceForTesting(4.5f);
            Assert.That(releasedBean.IsDisappearing, Is.True);
            Assert.That(releasedBean.transform.localScale.x, Is.LessThan(1f),
                "A landed bean must disappear gradually instead of vanishing.");
            yield return null;
        }
    }
}
