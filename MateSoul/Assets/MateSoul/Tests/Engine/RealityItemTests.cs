using System;
using System.Linq;
using System.Threading;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace MateSoul.Tests
{
    public sealed class RealityItemTests
    {
        [Test]
        public void CatalogClassifiesEverySdkSemanticAndKeepsInteractionMappingExplicit()
        {
            Assert.That(RealitySemanticCatalog.All.Count, Is.EqualTo(23));
            Assert.That(RealitySemanticCatalog.Items.Count, Is.EqualTo(15));
            Assert.That(RealitySemanticCatalog.All.Select(item => item.Id).Distinct().Count(),
                Is.EqualTo(RealitySemanticCatalog.All.Count));
            Assert.That(RealitySemanticCatalog.Get(RealitySemantic.Floor).Role,
                Is.EqualTo(RealitySemanticRole.Surface));
            Assert.That(RealitySemanticCatalog.Get(RealitySemantic.Human).Role,
                Is.EqualTo(RealitySemanticRole.Person));
            Assert.That(RealitySemanticCatalog.Get(RealitySemantic.VirtualWall).Role,
                Is.EqualTo(RealitySemanticRole.SyntheticBoundary));
            Assert.That(RealityInteractionCatalog.For(RealitySemantic.Chair)
                .Select(item => item.Id), Is.EquivalentTo(new[] { RealityInteractionIds.Sit }));
            Assert.That(RealityInteractionCatalog.For(RealitySemantic.Bed)
                .Select(item => item.Id), Is.EquivalentTo(new[] { RealityInteractionIds.Lie }));
            Assert.That(RealityInteractionCatalog.For(RealitySemantic.Table), Is.Empty);
        }

        [Test]
        public void DirectorySeparatesObservedItemsFromValidatedActionTargets()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            {
                directory.Load(new[]
                {
                    new RealityItemObservation("bed-a", RealitySemantic.Bed,
                        Vector3.zero, Quaternion.identity, new Vector3(2, .6f, 1.5f)),
                    new RealityItemObservation("table-a", RealitySemantic.Table,
                        Vector3.right, Quaternion.identity, Vector3.one)
                });

                Assert.That(directory.Count, Is.EqualTo(2));
                Assert.That(registry.Count, Is.Zero,
                    "Observed semantics are not executable until an interaction resolver validates them.");

                directory.PublishInteraction("geometry-chair", RealitySemantic.Chair,
                    Vector3.forward, Quaternion.identity, new Vector3(.8f, 1f, .7f),
                    true, RealityInteractionIds.Sit);
                Assert.That(registry.TryGet("geometry-chair", out var chair), Is.True);
                Assert.That(chair.Supports(RealityInteractionIds.Sit), Is.True);
                Assert.That(directory.TryGet("geometry-chair", out var record), Is.True);
                Assert.That(record.GeometryInferred, Is.True);

                Assert.Throws<InvalidOperationException>(() =>
                    directory.PublishInteraction("invalid-chair", RealitySemantic.Chair,
                        Vector3.zero, Quaternion.identity, Vector3.one,
                        false, RealityInteractionIds.Lie));
                registry.Register(new ItemInstance("collision", "virtual-chair",
                    "虚拟椅子", Array.Empty<string>()));
                Assert.Throws<InvalidOperationException>(() =>
                    directory.PublishInteraction("collision", RealitySemantic.Chair,
                        Vector3.zero, Quaternion.identity, Vector3.one,
                        false, RealityInteractionIds.Sit));
                Assert.That(directory.TryGet("collision", out _), Is.False);

                using (var reservation = registry.Reserve(
                           "geometry-chair", RealityInteractionIds.Sit, this))
                {
                    directory.WithdrawInteraction("geometry-chair", RealityInteractionIds.Sit);
                    Assert.That(reservation.IsValid, Is.False);
                    Assert.That(registry.TryGet("geometry-chair", out _), Is.False);
                }
                Assert.That(directory.TryGet("geometry-chair", out _), Is.False);
                Assert.That(directory.TryGet("reality-bed-a", out var bed), Is.True);
                Assert.That(bed.DeclaredInteractions.Select(item => item.Id),
                    Is.EquivalentTo(new[] { RealityInteractionIds.Lie }));
                Assert.That(bed.AvailableInteractions, Is.Empty);
            }
        }

        [Test]
        public void MockProviderPublishesAllItemGeometryWithoutAdvertisingActions()
        {
            var json = new TextAsset(@"[
                {
                    ""Guid"": ""floor"",
                    ""SemanticLabel"": ""Floor"",
                    ""PolygonVertices"": [
                        {""x"": -2, ""y"": -2}, {""x"": -2, ""y"": 2},
                        {""x"": 2, ""y"": 2}, {""x"": 2, ""y"": -2}
                    ]
                },
                {
                    ""Guid"": ""bed"",
                    ""SemanticLabel"": ""Bed"",
                    ""Box3DInfo"": {
                        ""Offset"": {""x"": 0, ""y"": 0.3, ""z"": 0},
                        ""Rotation"": {""w"": 1},
                        ""Extent"": {""x"": 2, ""y"": 0.6, ""z"": 1.5}
                    }
                },
                {
                    ""Guid"": ""lamp"",
                    ""SemanticLabel"": ""Lamp"",
                    ""Box3DInfo"": {
                        ""Offset"": {""x"": 1, ""y"": 0.8, ""z"": 0},
                        ""Rotation"": {""w"": 1},
                        ""Extent"": {""x"": 0.3, ""y"": 1.6, ""z"": 0.3}
                    }
                },
                {
                    ""Guid"": ""door"",
                    ""SemanticLabel"": ""Door"",
                    ""Box2DInfo"": {
                        ""Offset"": {""x"": 0, ""y"": 1},
                        ""Extent"": {""x"": 0.9, ""y"": 2}
                    }
                }
            ]");

            var snapshot = new PicoMockRoomMapProvider(json)
                .LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(snapshot.RealityItems.Select(item => item.Semantic),
                Is.EquivalentTo(new[]
                {
                    RealitySemantic.Bed, RealitySemantic.Lamp, RealitySemantic.Door
                }));
            Assert.That(snapshot.Obstacles.Select(item => item.Kind),
                Does.Contain(RoomObstacleKind.Bed));
            Assert.That(snapshot.Obstacles.Select(item => item.Kind),
                Does.Contain(RoomObstacleKind.Lamp));
            Assert.That(snapshot.RealityItems.Single(item => item.Semantic == RealitySemantic.Door)
                .Size.z, Is.EqualTo(.08f).Within(.001f));
        }
    }
}
