using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class RuntimeDetailedColliderSet : MonoBehaviour
    {
        public int GeneratedCount { get; internal set; }
    }

    public static class RuntimeColliderRefiner
    {
        private const int MaximumGeneratedHulls = 16;
        private const int MaximumSourceVertices = 30000;
        private const float MinimumExtent = .003f;

        private const MeshColliderCookingOptions Cooking =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;

        public static bool Ensure(GameObject root)
        {
            if (root == null) return false;
            var existingSet = root.GetComponent<RuntimeDetailedColliderSet>();
            if (existingSet != null) return existingSet.GeneratedCount > 0;

            var physical = root.GetComponentsInChildren<Collider>(true)
                .Where(shape => shape.enabled && !shape.isTrigger)
                .ToArray();
            var set = root.AddComponent<RuntimeDetailedColliderSet>();
            if (physical.Length != 1 || !(physical[0] is BoxCollider coarse))
                return false;

            var candidates = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null &&
                                 filter.sharedMesh.vertexCount >= 4 &&
                                 filter.sharedMesh.vertexCount <= MaximumSourceVertices &&
                                 filter.GetComponent<MeshCollider>() == null &&
                                 IsSubstantial(filter.sharedMesh.bounds))
                .OrderByDescending(filter => Volume(filter.sharedMesh.bounds))
                .Take(MaximumGeneratedHulls)
                .ToArray();
            if (candidates.Length == 0) return false;

            var generated = new List<Collider>(candidates.Length);
            foreach (var filter in candidates)
            {
                var mesh = filter.sharedMesh;
                var baked = false;
                if (mesh.isReadable)
                {
                    try
                    {
                        Physics.BakeMesh(mesh.GetInstanceID(), true, Cooking);
                        baked = true;
                    }
                    catch (System.Exception error)
                    {
                        Debug.LogWarning(
                            $"{root.name}: detailed collider bake failed for {mesh.name}: " +
                            error.Message);
                    }
                }
                if (baked)
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.cookingOptions = Cooking;
                    collider.convex = true;
                    collider.sharedMesh = mesh;
                    generated.Add(collider);
                }
                else
                {
                    var collider = filter.gameObject.AddComponent<BoxCollider>();
                    collider.center = mesh.bounds.center;
                    collider.size = mesh.bounds.size;
                    generated.Add(collider);
                }
            }

            if (generated.Count == 0) return false;
            coarse.enabled = false;
            set.GeneratedCount = generated.Count;
            return true;
        }

        private static bool IsSubstantial(Bounds bounds)
        {
            var size = bounds.size;
            return Mathf.Max(size.x, size.y, size.z) >= MinimumExtent &&
                   size.x * size.y * size.z >= .00000001f;
        }

        private static float Volume(Bounds bounds)
        {
            var size = bounds.size;
            return size.x * size.y * size.z;
        }
    }
}
