using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    // Changes only shading data. Rest coordinates remain attached during skinning.
    public static class LengyueReferenceHair
    {
        public static void Prepare()
        {
            string root = LengyueLayeredDressBuilder.Root;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(root + "/Runtime/Meshes/lengyue_mesh_bob_hair.asset");
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            var weights = mesh.boneWeights;
            var bind = mesh.bindposes;
            var uv = mesh.uv;
            int shapes = mesh.blendShapeCount;
            var normals = new Vector3[vertices.Length];
            var macro = new Vector2[vertices.Length];
            // Source coordinates: Z up, -Y toward the face. A capsule-shaped
            // normal field removes faceted grooves without flattening the cap.
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                float centerZ = Mathf.Clamp(p.z, 1.49f, 1.61f);
                normals[i] = new Vector3(p.x / (.125f * .125f),
                    (p.y - .006f) / (.13f * .13f),
                    (p.z - centerZ) / (.125f * .125f)).normalized;
                macro[i] = new Vector2(
                    .5f + Mathf.Atan2(p.x, -(p.y - .006f)) / (2 * Mathf.PI),
                    (p.z - 1.40f) / .35f);
            }
            mesh.normals = normals;
            mesh.uv2 = macro;
            mesh.RecalculateTangents();
            if (!vertices.SequenceEqual(mesh.vertices) || !indices.SequenceEqual(mesh.triangles) ||
                !weights.SequenceEqual(mesh.boneWeights) || !bind.SequenceEqual(mesh.bindposes) ||
                !uv.SequenceEqual(mesh.uv) || shapes != mesh.blendShapeCount)
                throw new InvalidOperationException("Hair geometry invariant failed");
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            string output = Path.GetFullPath(Application.dataPath +
                "/../../ArtSource/Lengyue/Appearance/ReferenceMatch/reports");
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/hair_mesh.json",
                "{\"vertices\":" + vertices.Length + ",\"triangles\":" + indices.Length / 3 +
                ",\"geometryWeightsBindposesUV0BlendShapesUnchanged\":true," +
                "\"normalField\":\"smooth capsule; no normal map\",\"uv2\":\"rest angle/height\"}");
        }
    }
}
