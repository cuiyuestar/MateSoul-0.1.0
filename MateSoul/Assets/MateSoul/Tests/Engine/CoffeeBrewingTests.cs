using System.Collections;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class CoffeeBrewingTests
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
        public void ProductionAssetsRegisterExplicitPourAndBrewCapabilities()
        {
            var kettle = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                ElectricKettleBuilder.KettleDefinitionPath);
            var mortar = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.MortarDefinitionPath);
            var mug = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            var bowl = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Bowl.asset");

            Assert.That(kettle, Is.Not.Null);
            Assert.That(mortar, Is.Not.Null);
            Assert.That(mug, Is.Not.Null);
            Assert.That(bowl, Is.Not.Null);

            var liquidPourer = kettle.Prefab.GetComponent<LiquidPourer>();
            var kettleWater = kettle.Prefab.GetComponent<WaterReceiver>();
            Assert.That(liquidPourer, Is.Not.Null);
            Assert.That(liquidPourer.Source, Is.SameAs(kettleWater));
            Assert.That(liquidPourer.Outlet, Is.Not.Null);
            Assert.That(liquidPourer.StreamRenderer, Is.Not.Null);
            Assert.That(liquidPourer.StreamRenderer.GetComponent<Collider>(),
                Is.Null);

            var grinder = mortar.Prefab.GetComponent<CoffeeGrinderController>();
            var powderPourer =
                mortar.Prefab.GetComponent<CoffeePowderPourer>();
            Assert.That(powderPourer, Is.Not.Null);
            Assert.That(powderPourer.Source, Is.SameAs(grinder));
            Assert.That(powderPourer.Outlet, Is.Not.Null);
            Assert.That(powderPourer.PowderStream, Is.Not.Null);

            var brew = mug.Prefab.GetComponent<CoffeeBrewReceiver>();
            Assert.That(brew, Is.Not.Null);
            Assert.That(brew.Water,
                Is.SameAs(mug.Prefab.GetComponent<WaterReceiver>()));
            Assert.That(brew.GroundUnitsPerServing,
                Is.EqualTo(CoffeeBrewReceiver.DefaultGroundUnitsPerServing));
            Assert.That(brew.MinimumTemperatureCelsius,
                Is.EqualTo(
                    CoffeeBrewReceiver.DefaultMinimumTemperatureCelsius));
            var ingredientSensor = mug.Prefab
                .GetComponentInChildren<CoffeeBrewIngredientSensor>(true);
            Assert.That(ingredientSensor, Is.Not.Null);
            Assert.That(ingredientSensor.Receiver, Is.SameAs(brew));
            Assert.That(ingredientSensor.GetComponent<Collider>().isTrigger,
                Is.True);
            Assert.That(bowl.Prefab.GetComponent<CoffeeBrewReceiver>(),
                Is.Null);
        }

        [UnityTest]
        public IEnumerator HotWaterAndGroundsBrewRegardlessOfArrivalOrder()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Coffee Brewing Integration");
            var mugDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            var mug = Object.Instantiate(
                mugDefinition.Prefab, root.transform);
            var water = mug.GetComponent<WaterReceiver>();
            var brew = mug.GetComponent<CoffeeBrewReceiver>();
            var notifications = new SystemNotifications();
            brew.BindNotifications(notifications);
            var opening = mug.transform.TransformPoint(water.OpeningCenter);

            Assert.That(brew.ReceiveGroundCoffee(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing, opening),
                Is.EqualTo(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing));
            Assert.That(water.Receive(.08f, 79.9f, opening),
                Is.EqualTo(.08f).Within(.0001f));
            Assert.That(brew.IsCoffee, Is.False);
            Assert.That(notifications.Hint,
                Is.EqualTo("请用加热后的水冲泡咖啡"));
            Assert.That(water.Receive(.01f, 20f, opening),
                Is.EqualTo(.01f).Within(.0001f));
            Assert.That(notifications.Hints, Has.Count.EqualTo(1),
                "Continuous cold-water filling must not repeat the warning.");

            water.SetTemperature(
                CoffeeBrewReceiver.DefaultMinimumTemperatureCelsius);
            Assert.That(brew.IsCoffee, Is.True);
            Assert.That(brew.GroundUnits, Is.Zero);
            Assert.That(notifications.Hint, Is.EqualTo("咖啡冲泡完成"));
            var properties = new MaterialPropertyBlock();
            water.WaterVisual.GetComponent<Renderer>()
                .GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor").r,
                Is.LessThan(.2f));

            water.Empty();
            Assert.That(brew.IsCoffee, Is.False);
            Assert.That(brew.GroundUnits, Is.Zero);

            Assert.That(water.Receive(.08f, 100f, opening),
                Is.EqualTo(.08f).Within(.0001f));
            Assert.That(brew.ReceiveGroundCoffee(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing, opening),
                Is.EqualTo(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing));
            Assert.That(brew.IsCoffee, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WholeBeansWithColdOrHotWaterRequestGrinding()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Whole Bean Warning Integration");
            var mugDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            var beanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            var mug = Object.Instantiate(
                mugDefinition.Prefab, root.transform);
            var beanObject = Object.Instantiate(
                beanPrefab, root.transform);
            beanObject.SetActive(true);
            var bean = beanObject.GetComponent<CoffeeBean>();
            var water = mug.GetComponent<WaterReceiver>();
            var brew = mug.GetComponent<CoffeeBrewReceiver>();
            var sensor = mug.GetComponentInChildren<
                CoffeeBrewIngredientSensor>();
            var notifications = new SystemNotifications();
            brew.BindNotifications(notifications);
            var opening = mug.transform.TransformPoint(water.OpeningCenter);

            bean.Launch(new Pose(sensor.transform.position,
                    Quaternion.identity),
                Vector3.zero, Vector3.zero);
            Physics.SyncTransforms();
            yield return null;
            Assert.That(brew.WholeBeanCount, Is.EqualTo(1));
            Assert.That(notifications.Hints, Is.Empty,
                "A dry whole bean must not trigger the water-contact warning.");
            Assert.That(water.Receive(.08f, 20f, opening),
                Is.EqualTo(.08f).Within(.0001f));
            Assert.That(notifications.Hint,
                Is.EqualTo("请先将咖啡豆研磨成粉再冲泡"));
            Assert.That(notifications.Hints, Has.Count.EqualTo(1));

            water.SetTemperature(100f);
            Assert.That(notifications.Hints, Has.Count.EqualTo(1),
                "Temperature changes must not repeat a latched whole-bean warning.");

            water.Empty();
            notifications.Clear();
            Assert.That(water.Receive(.08f, 100f, opening),
                Is.EqualTo(.08f).Within(.0001f));
            Assert.That(notifications.Hint,
                Is.EqualTo("请先将咖啡豆研磨成粉再冲泡"));
            Assert.That(brew.IsCoffee, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TransfersConserveHotWaterAndGroundCoffee()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Coffee Transfer Integration");
            var kettlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ElectricKettleBuilder.KettlePrefabPath);
            var mortarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeGrinderBuilder.MortarPrefabPath);
            var beanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            var mugDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");

            var kettle = Object.Instantiate(kettlePrefab, root.transform);
            var mortar = Object.Instantiate(mortarPrefab, root.transform);
            var mug = Object.Instantiate(
                mugDefinition.Prefab, root.transform);
            var sourceWater = kettle.GetComponent<WaterReceiver>();
            var targetWater = mug.GetComponent<WaterReceiver>();
            var liquidPourer = kettle.GetComponent<LiquidPourer>();
            var grinder = mortar.GetComponent<CoffeeGrinderController>();
            var powderPourer = mortar.GetComponent<CoffeePowderPourer>();
            var brew = mug.GetComponent<CoffeeBrewReceiver>();
            var sourceOpening = kettle.transform.TransformPoint(
                sourceWater.OpeningCenter);
            var targetOpening = mug.transform.TransformPoint(
                targetWater.OpeningCenter);

            Assert.That(sourceWater.Receive(.2f, 100f, sourceOpening),
                Is.EqualTo(.2f).Within(.0001f));
            Assert.That(liquidPourer.TryPourInto(
                    targetWater, .1f, targetOpening),
                Is.EqualTo(.1f).Within(.0001f));
            Assert.That(sourceWater.CurrentLiters,
                Is.EqualTo(.1f).Within(.0001f));
            Assert.That(targetWater.CurrentLiters,
                Is.EqualTo(.1f).Within(.0001f));
            Assert.That(targetWater.CurrentTemperatureCelsius,
                Is.EqualTo(100f).Within(.001f));

            var beanObject = Object.Instantiate(
                beanPrefab, root.transform);
            beanObject.SetActive(true);
            var bean = beanObject.GetComponent<CoffeeBean>();
            bean.Launch(new Pose(Vector3.up, Quaternion.identity),
                Vector3.zero, Vector3.zero);
            Assert.That(grinder.TryAcceptBean(bean), Is.True);
            grinder.AdvanceGrindingForTesting(
                grinder.RequiredStirSeconds, true);
            Assert.That(grinder.GroundBeanCount, Is.EqualTo(1));
            Assert.That(powderPourer.TryPourInto(
                    brew, 1, targetOpening),
                Is.EqualTo(1));
            Assert.That(grinder.GroundBeanCount, Is.Zero);
            Assert.That(brew.GroundUnits, Is.EqualTo(1));
            Assert.That(brew.IsCoffee, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TiltedKettleAndGrinderBrewThroughPhysicalTargets()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Physical Coffee Brewing Integration");
            var kettlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ElectricKettleBuilder.KettlePrefabPath);
            var mortarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeGrinderBuilder.MortarPrefabPath);
            var beanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            var mugDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");

            var mug = Object.Instantiate(
                mugDefinition.Prefab, root.transform);
            var kettle = Object.Instantiate(
                kettlePrefab, root.transform);
            var mortar = Object.Instantiate(
                mortarPrefab, root.transform);
            var targetWater = mug.GetComponent<WaterReceiver>();
            var brew = mug.GetComponent<CoffeeBrewReceiver>();
            var targetOpening = mug.transform.TransformPoint(
                targetWater.OpeningCenter);

            var sourceWater = kettle.GetComponent<WaterReceiver>();
            var liquidPourer = kettle.GetComponent<LiquidPourer>();
            var sourceOpening = kettle.transform.TransformPoint(
                sourceWater.OpeningCenter);
            Assert.That(sourceWater.Receive(.2f, 100f, sourceOpening),
                Is.EqualTo(.2f).Within(.0001f));
            var kettleOutletLocal = kettle.transform.InverseTransformPoint(
                liquidPourer.Outlet.position);
            var kettleRotation = Quaternion.Euler(0, 0, -65f);
            kettle.transform.SetPositionAndRotation(
                targetOpening + Vector3.up * .18f -
                kettleRotation * kettleOutletLocal,
                kettleRotation);
            Physics.SyncTransforms();
            liquidPourer.AdvanceForTesting(.5f);
            Assert.That(liquidPourer.IsPouring, Is.True);
            Assert.That(targetWater.CurrentLiters,
                Is.EqualTo(.12f).Within(.002f));
            Assert.That(targetWater.CurrentTemperatureCelsius,
                Is.EqualTo(100f).Within(.001f));

            var grinder = mortar.GetComponent<CoffeeGrinderController>();
            for (var index = 0;
                 index < CoffeeBrewReceiver.DefaultGroundUnitsPerServing;
                 index++)
            {
                var beanObject = Object.Instantiate(
                    beanPrefab, root.transform);
                beanObject.SetActive(true);
                var bean = beanObject.GetComponent<CoffeeBean>();
                bean.Launch(new Pose(Vector3.up, Quaternion.identity),
                    Vector3.zero, Vector3.zero);
                Assert.That(grinder.TryAcceptBean(bean), Is.True);
            }
            grinder.AdvanceGrindingForTesting(
                grinder.RequiredStirSeconds, true);
            Assert.That(grinder.GroundBeanCount,
                Is.EqualTo(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing));

            var powderPourer = mortar.GetComponent<CoffeePowderPourer>();
            var powderOutletLocal = mortar.transform.InverseTransformPoint(
                powderPourer.Outlet.position);
            var mortarRotation = Quaternion.Euler(0, 0, -68f);
            mortar.transform.SetPositionAndRotation(
                targetOpening + Vector3.up * .16f -
                mortarRotation * powderOutletLocal,
                mortarRotation);
            Physics.SyncTransforms();
            powderPourer.AdvanceForTesting(.5f);

            Assert.That(powderPourer.IsPouring, Is.True);
            Assert.That(powderPourer.TransferredGroundUnits,
                Is.EqualTo(
                    CoffeeBrewReceiver.DefaultGroundUnitsPerServing));
            Assert.That(grinder.GroundBeanCount, Is.Zero);
            Assert.That(brew.IsCoffee, Is.True);
            yield return null;
        }
    }
}
