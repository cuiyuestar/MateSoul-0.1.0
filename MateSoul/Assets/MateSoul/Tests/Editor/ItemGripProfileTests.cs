using System;
using System.Linq;
using System.Reflection;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Tests
{
    public sealed class ItemGripProfileTests
    {
        [Test]
        public void BuilderClassifiesAndCalibratesEveryVirtualItem()
        {
            var definitions = AssetDatabase.FindAssets(
                    "t:ItemDefinition",
                    new[] { "Assets/MateSoul/Content/Props" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .Where(item => item != null && item.Id != "apple-quarter")
                .ToArray();

            var configured = ItemActionContentBuilder.ConfigureCatalog(definitions);

            Assert.That(configured.Length,
                Is.EqualTo(definitions.Length + 1));
            foreach (var item in configured)
            {
                Assert.That(item.Interaction, Is.Not.Null, item.Id);
                Assert.DoesNotThrow(
                    item.Interaction.ValidateGrip, item.Id);
                Assert.That(item.Interaction.gripSize.x, Is.GreaterThan(0), item.Id);
                Assert.That(item.Interaction.gripSize.y, Is.GreaterThan(0), item.Id);
                Assert.That(item.Interaction.gripSize.z, Is.GreaterThan(0), item.Id);
                Assert.That(PhysicsBody.IsFinite(
                    item.Interaction.gripShapeOffset), Is.True, item.Id);
            }

            AssertKind(configured, "knife", ItemGripKind.Handle);
            AssertKind(configured, "frying-pan-body", ItemGripKind.Handle);
            AssertKind(configured, "spatula", ItemGripKind.Handle);
            AssertKind(configured, "spoon", ItemGripKind.Handle);
            AssertKind(configured, "chopsticks", ItemGripKind.Handle);
            AssertPose(configured, "spoon", CharacterHandPose.Pinch);
            AssertPose(configured, "chopsticks", CharacterHandPose.Pinch);
            AssertKind(configured, "mug", ItemGripKind.LoopHandle);
            AssertKind(configured, "radio", ItemGripKind.LoopHandle);
            AssertKind(configured, "television", ItemGripKind.LargeTwoHand);
            AssertKind(configured, "plate", ItemGripKind.ThinEdge);
            AssertKind(configured, "dartboard", ItemGripKind.ThinEdge);
            AssertKind(configured, "apple", ItemGripKind.Generic);
            AssertKind(configured, "plush-doll", ItemGripKind.Generic);
        }

        [Test]
        public void GripAnchorsKeepPhysicalVolumesOutsideThePalm()
        {
            var apple = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Apple.asset").Interaction;
            var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Knife.asset").Interaction;
            var plate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Plate.asset").Interaction;

            Assert.That(-apple.gripShapeOffset.y,
                Is.GreaterThanOrEqualTo(
                    apple.gripSize.y * .5f +
                    apple.fingerClearance + .0179f));
            Assert.That(-knife.gripShapeOffset.y,
                Is.GreaterThanOrEqualTo(
                    knife.GripRadius +
                    knife.fingerClearance + .0179f));
            Assert.That(plate.gripShapeOffset.z,
                Is.GreaterThanOrEqualTo(.0699f));
        }

        [Test]
        public void CanonicalKnifeSnapAlignsHandleAndPointsBladeAwayFromWrist()
        {
            var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Knife.asset");
            Assert.That(knife, Is.Not.Null);
            var profile = knife.Interaction;
            Assert.That(profile, Is.Not.Null);
            profile.ValidateGrip();

            var hand = new Pose(
                new Vector3(.3f, 1.1f, -.2f),
                Quaternion.Euler(12, 37, -8));
            var item = profile.ItemFromGrip(hand);
            var aligned = profile.WorldGrip(item);

            Assert.That(Vector3.Distance(aligned.position, hand.position),
                Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(aligned.rotation, hand.rotation),
                Is.LessThan(.01f));
            var bladeMidpoint = (profile.bladeStart + profile.bladeEnd) * .5f;
            var bladeDirection = item.rotation *
                (bladeMidpoint - profile.gripPosition).normalized;
            Assert.That(Vector3.Dot(
                    bladeDirection, hand.rotation * Vector3.forward),
                Is.GreaterThan(.8f));
        }

        [Test]
        public void SharedHandleContactModelFormsPowerGripAtEveryHandleRadius()
        {
            var contactMethod = typeof(AvatarItemRig).GetMethod(
                "HandleContactLocal",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(contactMethod, Is.Not.Null);
            var handles = new[] { "Knife", "FryingPanBody" }
                .Select(name => AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    "Assets/MateSoul/Content/Props/" + name + ".asset").Interaction)
                .ToArray();
            var fingerBases = new[]
            {
                new Vector3(-.026f, .03f, .041f),
                new Vector3(-.009f, .034f, .037f),
                new Vector3(.009f, .033f, .032f),
                new Vector3(.024f, .028f, .026f)
            };

            foreach (var handle in handles)
            {
                var radius = handle.GripRadius + handle.fingerClearance;
                var halfLength = handle.gripSize.z * .5f;
                foreach (var fingerBase in fingerBases)
                {
                    var contact = (Vector3)contactMethod.Invoke(
                        null, new object[] {
                            fingerBase, radius, halfLength, false
                        });
                    Assert.That(new Vector2(contact.x, contact.y).magnitude,
                        Is.EqualTo(radius).Within(1e-5f), handle.name);
                    Assert.That(contact.y, Is.LessThan(0), handle.name);
                    Assert.That(contact.z,
                        Is.EqualTo(fingerBase.z).Within(1e-5f), handle.name);
                }

                var thumbBase = new Vector3(-.035f, .015f, .018f);
                var thumb = (Vector3)contactMethod.Invoke(
                    null, new object[] {
                        thumbBase, radius, halfLength, true
                    });
                Assert.That(new Vector2(thumb.x, thumb.y).magnitude,
                    Is.EqualTo(radius).Within(1e-5f), handle.name);
                Assert.That(thumb.y, Is.GreaterThan(0), handle.name);
                Assert.That(Mathf.Sign(thumb.x),
                    Is.EqualTo(-Mathf.Sign(thumbBase.x)), handle.name);
                Assert.That(thumb.z,
                    Is.GreaterThanOrEqualTo(
                        halfLength * .549f), handle.name);
            }
        }

        [Test]
        public void SharedPowerGripUsesOrderedAnatomicalFlexion()
        {
            var method = typeof(AvatarItemRig).GetMethod(
                "PowerGripFlexion",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var handles = new[] {
                "Knife", "FryingPanBody", "Spatula"
            }.Select(name => AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/" + name + ".asset")
                .Interaction).ToArray();

            foreach (var handle in handles)
            {
                var previousMcp = 0f;
                for (var finger = 1; finger < 5; finger++)
                {
                    var flexion = (Vector3)method.Invoke(
                        null, new object[] {
                            handle.gripSize.x,
                            handle.fingerCurl,
                            finger
                        });
                    Assert.That(flexion.x,
                        Is.GreaterThan(previousMcp), handle.name);
                    Assert.That(flexion.y,
                        Is.GreaterThan(flexion.x), handle.name);
                    Assert.That(flexion.z,
                        Is.LessThan(flexion.y), handle.name);
                    Assert.That(flexion.x,
                        Is.InRange(35f, 70f), handle.name);
                    Assert.That(flexion.y,
                        Is.InRange(50f, 95f), handle.name);
                    Assert.That(flexion.z,
                        Is.InRange(25f, 65f), handle.name);
                    previousMcp = flexion.x;
                }
            }
        }

        [Test]
        public void TwoHandPoseAlignsBothLargeItemAnchors()
        {
            var television = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Television.asset");
            Assert.That(television, Is.Not.Null);
            var profile = television.Interaction;
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.RequiresTwoHands, Is.True);
            profile.ValidateGrip();

            var primary = new Pose(
                new Vector3(.28f, 1.05f, .45f),
                Quaternion.Euler(0, 20, 0));
            var localSpan = profile.secondaryGripPosition -
                            profile.gripPosition;
            var secondary = new Pose(
                primary.position + primary.rotation * localSpan,
                primary.rotation);
            var item = profile.ItemFromHands(primary, secondary);

            Assert.That(Vector3.Distance(
                    profile.WorldGrip(item).position, primary.position),
                Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(
                    profile.WorldSecondaryGrip(item).position,
                    secondary.position),
                Is.LessThan(1e-5f));
        }

        private static void AssertKind(
            ItemDefinition[] definitions, string id, ItemGripKind expected)
        {
            var item = definitions.Single(value =>
                string.Equals(value.Id, id, StringComparison.Ordinal));
            Assert.That(item.Interaction.gripKind,
                Is.EqualTo(expected), id);
        }

        private static void AssertPose(
            ItemDefinition[] definitions, string id,
            CharacterHandPose expected)
        {
            var item = definitions.Single(value =>
                string.Equals(value.Id, id, StringComparison.Ordinal));
            Assert.That(item.Interaction.handPose,
                Is.EqualTo(expected), id);
        }
    }
}
