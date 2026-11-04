using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    // Match CookingSet's lathed pan profile without cooking a convex hull over
    // the open cavity. Both inventory definitions share these three small meshes.
    public static class FryingPanColliderBuilder
    {
        private const int Segments = 24;
        private const string MeshPath = "Assets/MateSoul/Content/Props/FryingPanCollision.asset";
        private const string CollisionRoot = "Pan Collision";

        [MenuItem("MateSoul/Physics/Rebuild Frying Pan Colliders")]
        public static void RebuildProduction()
        {
            foreach (var name in new[] { "FryingPan", "FryingPanBody" })
                Configure(AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"Assets/MateSoul/Content/Props/{name}.asset"));
            AssetDatabase.SaveAssets();
            Debug.Log("MATESOUL_PAN_COLLIDERS_BUILT");
        }

        public static void Configure(ItemDefinition definition)
        {
            var meshes = LoadMeshes();
            var path = AssetDatabase.GetAssetPath(definition.Prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var bodyRenderer = root.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(renderer => renderer.name == "FryingPan_Body");
                var handles = root.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.name.Contains("Handle") ||
                                       renderer.name.Contains("Grip") ||
                                       renderer.name.Contains("Collar")).ToArray();
                if (bodyRenderer == null || handles.Length == 0)
                    throw new InvalidOperationException("平底锅缺少锅体或手柄渲染节点。");
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
                var previous = root.transform.Find(CollisionRoot);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);

                var center = root.transform.InverseTransformPoint(bodyRenderer.bounds.center);
                center.y = root.transform.InverseTransformPoint(bodyRenderer.bounds.min).y;
                var collision = new GameObject(CollisionRoot);
                collision.layer = root.layer;
                collision.transform.SetParent(root.transform, false);
                collision.transform.localPosition = center;
                AddMesh(collision, "Floor", meshes[0], 0);
                for (var segment = 0; segment < Segments; segment++)
                {
                    var angle = segment * 360f / Segments;
                    AddMesh(collision, $"Lower Wall {segment:00}", meshes[1], angle);
                    AddMesh(collision, $"Upper Wall {segment:00}", meshes[2], angle);
                }

                foreach (var renderer in handles)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        throw new InvalidOperationException("平底锅手柄缺少碰撞网格。");
                    var handle = AddMesh(collision, renderer.name + " Collision",
                        filter.sharedMesh, 0);
                    handle.transform.SetPositionAndRotation(
                        renderer.transform.position, renderer.transform.rotation);
                    var parentScale = collision.transform.lossyScale;
                    handle.transform.localScale = Vector3.Scale(renderer.transform.lossyScale,
                        new Vector3(1 / parentScale.x, 1 / parentScale.y, 1 / parentScale.z));
                }
                var heat = root.GetComponent<CookwareHeatReceiver>() ??
                           root.AddComponent<CookwareHeatReceiver>();
                heat.Configure(new Vector3(center.x, center.y + .0045f, center.z),
                    .112f, .125f, .070f);
                PhysicsContentBuilder.ConfigureBody(root, definition.Physics);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static MeshCollider AddMesh(GameObject root, string name, Mesh mesh, float angle)
        {
            var part = new GameObject(name);
            part.layer = root.layer;
            part.transform.SetParent(root.transform, false);
            part.transform.localRotation = Quaternion.Euler(0, angle, 0);
            var shape = part.AddComponent<MeshCollider>();
            shape.convex = true;
            shape.sharedMesh = mesh;
            return shape;
        }

        private static Mesh[] LoadMeshes()
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>().ToArray();
            var floor = Floor();
            var lower = Wall("Pan Lower Wall", new[]
            {
                new Vector2(.112f, 0), new Vector2(.126f, .004f),
                new Vector2(.126f + .011f * (.008f / .021f), .012f),
                new Vector2(.126f, .012f), new Vector2(.112f, .005f)
            });
            var upper = Wall("Pan Upper Wall", new[]
            {
                new Vector2(.126f + .011f * (.008f / .021f), .012f),
                new Vector2(.137f, .025f), new Vector2(.14f, .052f),
                new Vector2(.14f, .056f), new Vector2(.136f, .054f),
                new Vector2(.126f, .012f)
            });
            if (existing.Length != 0)
            {
                var stored = new[] { AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath),
                    existing.Single(mesh => mesh.name == "Pan Lower Wall"),
                    existing.Single(mesh => mesh.name == "Pan Upper Wall") };
                var generated = new[] { floor, lower, upper };
                for (var index = 0; index < stored.Length; index++)
                {
                    generated[index].name = stored[index].name;
                    EditorUtility.CopySerialized(generated[index], stored[index]);
                    EditorUtility.SetDirty(stored[index]);
                    Object.DestroyImmediate(generated[index]);
                }
                AssetDatabase.SaveAssets();
                return stored;
            }
            AssetDatabase.CreateAsset(floor, MeshPath);
            AssetDatabase.AddObjectToAsset(lower, floor);
            AssetDatabase.AddObjectToAsset(upper, floor);
            AssetDatabase.SaveAssets();
            return new[] { floor, lower, upper };
        }

        private static Mesh Floor()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int count = Segments * 2;
            for (var level = 0; level < 2; level++)
                for (var index = 0; index < count; index++)
                {
                    var angle = index * Mathf.PI * 2 / count;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * .112f,
                        level * .005f, Mathf.Sin(angle) * .112f));
                }
            for (var index = 1; index < count - 1; index++)
            {
                triangles.AddRange(new[] { 0, index, index + 1,
                    count, count + index + 1, count + index });
            }
            for (var index = 0; index < count; index++)
            {
                var next = (index + 1) % count;
                triangles.AddRange(new[] { index, count + index, next,
                    next, count + index, count + next });
            }
            return CreateMesh("Pan Floor", vertices, triangles);
        }

        private static Mesh Wall(string name, Vector2[] profile)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var slice = 0; slice < 3; slice++)
            {
                // Adjacent convex hulls overlap slightly so exact seam rays
                // cannot pass through floating-point transform differences.
                var angle = (slice - 1) * (Mathf.PI / Segments + .05f * Mathf.Deg2Rad);
                foreach (var point in profile)
                    vertices.Add(new Vector3(Mathf.Cos(angle) * point.x,
                        point.y, Mathf.Sin(angle) * point.x));
            }
            var count = profile.Length;
            for (var slice = 0; slice < 2; slice++)
                for (var index = 0; index < count; index++)
                {
                    var a = slice * count + index;
                    var b = slice * count + (index + 1) % count;
                    triangles.AddRange(new[] { a, b, a + count,
                        b, b + count, a + count });
                }
            for (var index = 1; index < count - 1; index++)
                triangles.AddRange(new[] { 0, index + 1, index,
                    2 * count, 2 * count + index, 2 * count + index + 1 });
            return CreateMesh(name, vertices, triangles);
        }

        private static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
