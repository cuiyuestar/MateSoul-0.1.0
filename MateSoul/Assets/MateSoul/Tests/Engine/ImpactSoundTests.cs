using System;
using System.Collections;
using System.Linq;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class ImpactSoundTests
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
        public void ProductionFurnitureSharesOneValidatedThreeLevelImpactProfile()
        {
            var definitions = new[] { "Table", "Chair", "Television" }
                .Select(name => AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"Assets/MateSoul/Content/Props/{name}.asset"))
                .ToArray();
            Assert.That(definitions.All(value => value != null), Is.True);
            var settings = definitions.Select(value => value.ImpactSound).ToArray();
            Assert.That(settings.All(value => value.enabled), Is.True);
            Assert.That(settings.Select(value => value.sound).Distinct().Count(), Is.EqualTo(1));
            Assert.That(settings[0].sound.channels, Is.EqualTo(1));
            Assert.That(settings[0].sound.length, Is.EqualTo(2.906f).Within(.02f));
            var onsetSamples = new float[Mathf.CeilToInt(settings[0].sound.frequency * .02f)];
            Assert.That(settings[0].sound.GetData(onsetSamples, 0), Is.True);
            var onset = Array.FindIndex(onsetSamples, sample => Mathf.Abs(sample) > .002f);
            Assert.That(onset, Is.GreaterThanOrEqualTo(0));
            Assert.That(onset, Is.LessThan(settings[0].sound.frequency * .012f),
                "The impact transient must begin within 12 ms of the playback request.");
            Assert.That(settings[0].lightForceThreshold, Is.EqualTo(120f));
            Assert.That(settings[0].mediumForceThreshold, Is.EqualTo(350f));
            Assert.That(settings[0].heavyForceThreshold, Is.EqualTo(850f));
            foreach (var definition in definitions)
                Assert.That(definition.Prefab.GetComponent<ImpactSoundEmitter>(), Is.Null,
                    "The per-instance emitter is attached by GameItem from content configuration.");
            Assert.Throws<ArgumentException>(() =>
                new ImpactSoundSettings(100, 90, 300, settings[0].sound).Validate());
        }

        [UnityTest]
        public IEnumerator ThresholdsDriveLightMediumAndHeavySpatialPlayback()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Impact Sound Integration");
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Television.asset");
            var view = Object.Instantiate(definition.Prefab, root.transform);
            view.transform.position = Vector3.up * 5f;
            var item = view.AddComponent<GameItem>();
            item.Bind(definition, new MateSoul.Gameplay.ItemRegistry(), null, "impact-tv");
            var impact = view.GetComponent<ImpactSoundEmitter>();
            Assert.That(impact, Is.Not.Null);
            var settings = definition.ImpactSound;

            impact.ApplyImpactForTesting(settings.lightForceThreshold - 1);
            Assert.That(impact.PlayCount, Is.Zero);
            impact.ApplyImpactForTesting(settings.lightForceThreshold + 1);
            AssertPlayback(impact, ImpactSoundSeverity.Light, .46f);
            yield return Wait(.2f);
            impact.ApplyImpactForTesting(settings.mediumForceThreshold + 1);
            AssertPlayback(impact, ImpactSoundSeverity.Medium, .72f);
            yield return Wait(.2f);
            impact.ApplyImpactForTesting(settings.heavyForceThreshold + 1);
            AssertPlayback(impact, ImpactSoundSeverity.Heavy, .95f);
            Assert.That(impact.PlayCount, Is.EqualTo(3));
            Assert.That(impact.Source.clip, Is.SameAs(settings.sound));
            Assert.That(impact.Source.spatialBlend, Is.EqualTo(1f));
            Assert.That(impact.Source.minDistance, Is.EqualTo(.35f));
            Assert.That(impact.Source.maxDistance, Is.EqualTo(7f));
        }

        [UnityTest]
        public IEnumerator RealFurnitureDropTriggersImpactAfterArmingDelay()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Impact Drop Integration");
            var floor = new GameObject("Floor", typeof(BoxCollider));
            floor.transform.SetParent(root.transform);
            floor.transform.position = Vector3.down * .1f;
            floor.GetComponent<BoxCollider>().size = new Vector3(6, .2f, 6);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Chair.asset");
            var view = Object.Instantiate(definition.Prefab, root.transform);
            view.transform.position = Vector3.up * 2f;
            var item = view.AddComponent<GameItem>();
            item.Bind(definition, new MateSoul.Gameplay.ItemRegistry(), null, "drop-chair");
            var impact = view.GetComponent<ImpactSoundEmitter>();

            yield return EngineTestFrames.Seconds(1.2f);
            Assert.That(impact.PlayCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(impact.LastSeverity, Is.Not.EqualTo(ImpactSoundSeverity.None));
            Assert.That(impact.LastForce,
                Is.GreaterThanOrEqualTo(definition.ImpactSound.lightForceThreshold));
        }

        private static void AssertPlayback(ImpactSoundEmitter impact,
            ImpactSoundSeverity severity, float volume)
        {
            Assert.That(impact.LastSeverity, Is.EqualTo(severity));
            Assert.That(impact.LastVolume, Is.EqualTo(volume).Within(.001f));
            Assert.That(impact.Source.volume, Is.EqualTo(volume).Within(.001f));
        }

        private static IEnumerator Wait(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
