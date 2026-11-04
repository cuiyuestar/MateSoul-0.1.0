using System.Collections;
using System.IO;
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
    public sealed class ElectricKettleTests
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
        public void ProductionAssetsExposeOneKettleSetInventoryUnit()
        {
            var kettle = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                ElectricKettleBuilder.KettleDefinitionPath);
            var heatingBase = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                ElectricKettleBuilder.BaseDefinitionPath);
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                ElectricKettleBuilder.GroupDefinitionPath);

            Assert.That(kettle, Is.Not.Null);
            Assert.That(heatingBase, Is.Not.Null);
            Assert.That(group, Is.Not.Null);
            Assert.That(kettle.CatalogVisible, Is.False);
            Assert.That(heatingBase.CatalogVisible, Is.False);
            Assert.That(group.CatalogVisible, Is.True);
            Assert.That(group.Id, Is.EqualTo("electric-kettle"));
            Assert.That(group.DisplayName, Is.EqualTo("电热水壶"));
            Assert.That(group.InventoryLimit, Is.EqualTo(1));
            Assert.That(group.Category, Is.EqualTo(ItemCategory.Appliance));
            Assert.That(group.PlacementParts.Select(
                    part => part.Definition.Id),
                Is.EqualTo(new[]
                {
                    "electric-kettle-base",
                    "electric-kettle-body"
                }));
            Assert.That(heatingBase.FixedAfterPlacement, Is.True);

            var controller =
                kettle.Prefab.GetComponent<ElectricKettleController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.BaseDetectionRadius,
                Is.EqualTo(.12f).Within(.0001f));
            Assert.That(kettle.Prefab.GetComponent<WaterReceiver>(), Is.Not.Null);
            Assert.That(kettle.Prefab
                .GetComponentInChildren<ElectricKettlePowerButton>(true),
                Is.Not.Null);
            Assert.That(kettle.Prefab
                .GetComponentInChildren<ParticleSystem>(true), Is.Not.Null);
            Assert.That(heatingBase.Prefab
                .GetComponent<ElectricKettleBase>(), Is.Not.Null);
            var steel = kettle.Prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .First(material =>
                    material.name.Contains("BrushedStainless"));
            Assert.That(steel.GetFloat("_Metallic"), Is.GreaterThan(.9f));
            Assert.That(steel.GetFloat("_Smoothness"), Is.GreaterThan(.65f));
            Assert.That(steel.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(steel.GetTexture("_BumpMap"), Is.Not.Null);
            var baseMap = new Texture2D(
                2, 2, TextureFormat.RGBA32, false, true);
            var normalMap = new Texture2D(
                2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(
                    baseMap, File.ReadAllBytes(Path.GetFullPath(
                        AssetDatabase.GetAssetPath(
                            steel.GetTexture("_BaseMap"))))), Is.True);
                Assert.That(baseMap.GetPixels32().Average(pixel =>
                        (pixel.r + pixel.g + pixel.b) /
                        (3f * byte.MaxValue)),
                    Is.GreaterThan(.35f));
                Assert.That(ImageConversion.LoadImage(
                    normalMap, File.ReadAllBytes(Path.GetFullPath(
                        AssetDatabase.GetAssetPath(
                            steel.GetTexture("_BumpMap"))))), Is.True);
                Assert.That(normalMap.GetPixels32().Average(
                        pixel => pixel.b / (float)byte.MaxValue),
                    Is.GreaterThan(.75f));
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(normalMap);
            }
            var clips = kettle.Prefab.GetComponents<AudioSource>()
                .Select(source => source.clip).Where(clip => clip != null)
                .ToArray();
            Assert.That(clips, Does.Contain(
                AssetDatabase.LoadAssetAtPath<AudioClip>(
                    CookingEffectsAssetBuilder.InductionHumPath)));
        }

        [UnityTest]
        public IEnumerator EmptyKettleIsRejectedAndValidContactBoilsInSixtySeconds()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Electric Kettle Integration");
            var kettlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ElectricKettleBuilder.KettlePrefabPath);
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ElectricKettleBuilder.BasePrefabPath);
            var kettleObject = Object.Instantiate(kettlePrefab, root.transform);
            var baseObject = Object.Instantiate(basePrefab, root.transform);
            baseObject.GetComponent<Rigidbody>().isKinematic = true;
            var controller =
                kettleObject.GetComponent<ElectricKettleController>();
            var water = kettleObject.GetComponent<WaterReceiver>();
            var heatingBase =
                baseObject.GetComponent<ElectricKettleBase>();

            Assert.That(controller.TogglePower(), Is.False);
            Assert.That(controller.IsPoweredOn, Is.False);
            Assert.That(water.CurrentTemperatureCelsius,
                Is.EqualTo(WaterReceiver.AmbientTemperatureCelsius));

            kettleObject.transform.SetPositionAndRotation(
                heatingBase.ContactPoint.position +
                new Vector3(.045f, .065f, .020f),
                Quaternion.Euler(28f, 47f, 19f));
            var rigidBody = kettleObject.GetComponent<Rigidbody>();
            rigidBody.velocity = new Vector3(.8f, -.4f, .3f);
            rigidBody.angularVelocity = new Vector3(2f, 1f, -1f);
            Physics.SyncTransforms();
            controller.AdvanceForTesting(.11f);
            var kettleContact = kettleObject
                .GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "Kettle_BaseContactPoint");
            Assert.That(controller.IsDocked, Is.True);
            Assert.That(controller.SnapCount, Is.EqualTo(1));
            Assert.That(Vector3.Distance(
                    kettleContact.position, heatingBase.ContactPoint.position),
                Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(
                    kettleObject.transform.rotation,
                    baseObject.transform.rotation),
                Is.LessThan(.1f));
            Assert.That(rigidBody.isKinematic, Is.True);
            Assert.That(rigidBody.useGravity, Is.False);
            Assert.That(rigidBody.velocity, Is.EqualTo(Vector3.zero));
            Assert.That(rigidBody.angularVelocity, Is.EqualTo(Vector3.zero));

            var opening = kettleObject.transform.TransformPoint(
                water.OpeningCenter);
            Assert.That(water.Receive(1.2f, opening), Is.EqualTo(1.2f));

            Assert.That(controller.TogglePower(), Is.True);
            controller.RefreshContactForTesting();
            Assert.That(controller.IsPoweredOn, Is.True);
            Assert.That(controller.IsHeating, Is.True);
            Assert.That(controller.ActiveBase, Is.SameAs(heatingBase));
            controller.AdvanceForTesting(
                ElectricKettleController.BoilDurationSeconds - .1f);
            Assert.That(controller.IsPoweredOn, Is.True);
            Assert.That(water.CurrentTemperatureCelsius,
                Is.LessThan(ElectricKettleController.BoilingTemperatureCelsius));
            controller.AdvanceForTesting(.2f);
            Assert.That(controller.IsPoweredOn, Is.False);
            Assert.That(controller.IsHeating, Is.False);
            Assert.That(water.CurrentTemperatureCelsius,
                Is.EqualTo(
                    ElectricKettleController.BoilingTemperatureCelsius)
                    .Within(.001f));
            Assert.That(controller.ButtonSoundPlayCount, Is.EqualTo(1));
            var body = kettleObject.GetComponent<PhysicsBody>();
            body.BeginFollow(body.Pose);
            Assert.That(body.IsDocked, Is.False);
            Assert.That(rigidBody.isKinematic, Is.False);
            Assert.That(rigidBody.useGravity, Is.True);
            body.EndFollow();
            yield return null;
        }
    }
}
