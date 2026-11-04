using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Gameplay;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class CarrotCuttingTests
    {
        private readonly List<Object> runtimeObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in runtimeObjects)
                if (value != null) Object.DestroyImmediate(value);
            runtimeObjects.Clear();
        }

        [Test]
        public void ProductionCarrotHasValidatedCuttableAssets()
        {
            var definition =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    CarrotBuilder.DefinitionPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id, Is.EqualTo("carrot"));
            Assert.That(definition.Category, Is.EqualTo(ItemCategory.Food));
            Assert.That(definition.CatalogVisible, Is.True);
            Assert.That(definition.InventoryLimit, Is.EqualTo(5));
            Assert.That(definition.RestockDaily, Is.True);
            Assert.That(definition.Capabilities,
                Does.Contain("runtime-cuttable"));
            Assert.That(definition.Cuttable.enabled, Is.True);
            Assert.That(definition.Cuttable.interiorMaterial, Is.Not.Null);
            Assert.That(definition.Cuttable.collisionProxy, Is.Not.Null);
            Assert.That(definition.Cuttable.collisionProxy.isReadable, Is.True);
            Assert.That(definition.Cuttable.collisionProxy.triangles.Length / 3,
                Is.LessThan(255));
            Assert.DoesNotThrow(definition.Cuttable.Validate);
            Assert.That(definition.Prefab.GetComponent<RuntimeCuttable>(),
                Is.Not.Null);
            var collider = definition.Prefab.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.convex, Is.True);
        }

        [Test]
        public void HorizontalCutPreservesVolumeAndClosesBothHalves()
        {
            var definition = Carrot();
            var source = definition.Prefab
                .GetComponent<RuntimeCuttable>().SurfaceMesh;
            var originalVolume = MeshPlaneSlicer.CalculateVolume(source);
            var result = MeshPlaneSlicer.Slice(source,
                new Plane(Vector3.up, new Vector3(0, .13f, 0)),
                definition.Cuttable, "Horizontal Carrot");
            Track(result);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Positive.Mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.Negative.Mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.Positive.Mesh.GetTriangles(1).Length,
                Is.GreaterThan(0));
            Assert.That(result.Negative.Mesh.GetTriangles(1).Length,
                Is.GreaterThan(0));
            Assert.That(result.Positive.Volume + result.Negative.Volume,
                Is.EqualTo(originalVolume).Within(originalVolume * .002f));
            AssertClosed(result.Positive.Mesh);
            AssertClosed(result.Negative.Mesh);
        }

        [Test]
        public void ObliqueCutStoresDirectionalMaterialAlignment()
        {
            var definition = Carrot();
            var source = definition.Prefab
                .GetComponent<RuntimeCuttable>().SurfaceMesh;
            var normal = Quaternion.AngleAxis(45f, Vector3.forward) * Vector3.up;
            var result = MeshPlaneSlicer.Slice(source,
                new Plane(normal, new Vector3(0, .14f, 0)),
                definition.Cuttable, "Oblique Carrot");
            Track(result);

            Assert.That(result, Is.Not.Null);
            var capIndices = result.Positive.Mesh.GetTriangles(1);
            var data = result.Positive.Mesh.uv3;
            Assert.That(capIndices, Is.Not.Empty);
            var expected = Mathf.Abs(Vector3.Dot(normal.normalized,
                definition.Cuttable.Axis));
            foreach (var index in capIndices.Distinct())
                Assert.That(data[index].x,
                    Is.EqualTo(expected).Within(.0001f));
            Assert.That(expected, Is.InRange(.70f, .71f));
        }

        [Test]
        public void CutHistoryReplaysTheSameCollisionVolume()
        {
            var definition = Carrot();
            var first = InstantiateCuttable(definition);
            var second = InstantiateCuttable(definition);
            var plane = new Plane(Vector3.up, new Vector3(0, .12f, 0));
            using (var pair = first.CreateCut(plane))
            {
                Assert.That(pair, Is.Not.Null);
                first.ApplyPiece(pair.Positive);
            }
            var saved = first.CaptureState();
            second.Restore(saved);

            Assert.That(second.CutDepth, Is.EqualTo(1));
            Assert.That(MeshPlaneSlicer.CalculateVolume(second.CollisionMesh),
                Is.EqualTo(MeshPlaneSlicer.CalculateVolume(first.CollisionMesh))
                    .Within(.00000001f));
            Assert.That(second.VolumeRatio,
                Is.EqualTo(first.VolumeRatio).Within(.0001f));
        }

        [Test]
        public void RepeatedCutsStopAtTheAuthoredDepth()
        {
            var definition = Carrot();
            var cuttable = InstantiateCuttable(definition);
            for (var depth = 0; depth < definition.Cuttable.maximumCutDepth;
                 depth++)
            {
                var bounds = cuttable.CollisionMesh.bounds;
                var plane = new Plane(Vector3.up,
                    new Vector3(0, bounds.center.y, 0));
                using (var pair = cuttable.CreateCut(plane))
                {
                    Assert.That(pair, Is.Not.Null, "depth " + depth);
                    cuttable.ApplyPiece(pair.Positive);
                }
                Assert.That(TriangleCount(cuttable.CollisionMesh),
                    Is.LessThanOrEqualTo(255));
            }
            var rejected = cuttable.CreateCut(new Plane(Vector3.right,
                cuttable.CollisionMesh.bounds.center));
            Assert.That(rejected, Is.Null);
        }

        [Test]
        public void SaveValidationRejectsInvalidCutPlane()
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "matesoul-carrot-cut-save.json");
            if (System.IO.File.Exists(root)) System.IO.File.Delete(root);
            var store = new JsonGameSaveStore(root);
            var data = new GameSaveData
            {
                roomAnchorId = "room",
                items = new[]
                {
                    new ItemSaveData
                    {
                        instanceId = "piece",
                        definitionId = "carrot",
                        qw = 1,
                        cutState = new CutStateSaveData
                        {
                            planes = new[]
                            {
                                new CutPlaneSaveData
                                {
                                    nx = 2,
                                    positiveSide = true
                                }
                            }
                        }
                    }
                }
            };
            Assert.Throws<System.IO.InvalidDataException>(() => store.Save(data));
            if (System.IO.File.Exists(root)) System.IO.File.Delete(root);
        }

        [Test]
        public void LegacyEmptyCutStateRemainsAValidUncutItem()
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "matesoul-legacy-cut-save.json");
            if (System.IO.File.Exists(root)) System.IO.File.Delete(root);
            var store = new JsonGameSaveStore(root);
            var data = new GameSaveData
            {
                roomAnchorId = "room",
                items = new[]
                {
                    new ItemSaveData
                    {
                        instanceId = "legacy",
                        definitionId = "table",
                        qw = 1,
                        cutState = new CutStateSaveData
                        {
                            planes = Array.Empty<CutPlaneSaveData>()
                        }
                    }
                }
            };
            Assert.DoesNotThrow(() => store.Save(data));
            Assert.That(store.Load().items[0].cutState.planes, Is.Empty);
            System.IO.File.Delete(root);
        }

        private ItemDefinition Carrot()
        {
            var definition =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    CarrotBuilder.DefinitionPath);
            Assert.That(definition, Is.Not.Null,
                "Build carrot content before running its tests.");
            return definition;
        }

        private RuntimeCuttable InstantiateCuttable(ItemDefinition definition)
        {
            var root = Object.Instantiate(definition.Prefab);
            runtimeObjects.Add(root);
            var cuttable = root.GetComponent<RuntimeCuttable>();
            cuttable.Bind(definition.Cuttable);
            return cuttable;
        }

        private void Track(MeshSliceResult result)
        {
            if (result == null) return;
            runtimeObjects.Add(result.Positive.Mesh);
            runtimeObjects.Add(result.Negative.Mesh);
        }

        private static void AssertClosed(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var edges = new Dictionary<string, int>();
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                for (var index = 0; index < triangles.Length; index += 3)
                {
                    CountEdge(edges, vertices[triangles[index]],
                        vertices[triangles[index + 1]]);
                    CountEdge(edges, vertices[triangles[index + 1]],
                        vertices[triangles[index + 2]]);
                    CountEdge(edges, vertices[triangles[index + 2]],
                        vertices[triangles[index]]);
                }
            }
            Assert.That(edges.Values.All(count => count == 2), Is.True,
                "Every welded geometric edge must be shared by exactly two triangles.");
        }

        private static void CountEdge(Dictionary<string, int> edges,
            Vector3 first, Vector3 second)
        {
            var firstKey = VertexKey(first);
            var secondKey = VertexKey(second);
            var key = string.CompareOrdinal(firstKey, secondKey) <= 0
                ? firstKey + "|" + secondKey
                : secondKey + "|" + firstKey;
            edges.TryGetValue(key, out var count);
            edges[key] = count + 1;
        }

        private static string VertexKey(Vector3 value)
            => $"{Mathf.RoundToInt(value.x * 100000)}:" +
               $"{Mathf.RoundToInt(value.y * 100000)}:" +
               $"{Mathf.RoundToInt(value.z * 100000)}";

        private static int TriangleCount(Mesh mesh)
        {
            var count = 0;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                count += mesh.GetTriangles(submesh).Length / 3;
            return count;
        }
    }
}
