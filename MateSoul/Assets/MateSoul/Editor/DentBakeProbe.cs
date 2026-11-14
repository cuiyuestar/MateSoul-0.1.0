using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    // Bakes every skinned renderer of the production avatar after the Animator
    // settles into its default (idle) state, so offline analysis sees the real
    // in-game pose instead of the bind pose.
    public static class DentBakeProbe
    {
        public static void BakeGarmentInIdlePose()
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MateSoul/Content/lengyue_avatar_runtime.prefab");
            if (!prefab)
            {
                File.WriteAllText(LogPath(), "prefab not found");
                return;
            }
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator)
            {
                animator.applyRootMotion = false;
                for (int i = 0; i < 180; i++) animator.Update(1f / 60f);
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.sharedMesh) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var m = smr.transform.localToWorldMatrix;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "# renderer={0} mesh={1} verts={2} m={3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10:F6},{11:F6},{12:F6},{13:F6},{14:F6},{15:F6},{16:F6},{17:F6},{18:F6},{19:F6}",
                    smr.name, smr.sharedMesh.name, baked.vertexCount,
                    m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13,
                    m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33));
                var verts = baked.vertices;
                foreach (var v in verts)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F5},{1:F5},{2:F5}", v.x, v.y, v.z));
            }
            Object.DestroyImmediate(go);
            File.WriteAllText(LogPath(), sb.ToString());
            Debug.Log("DentBakeProbe done");
        }

        static string LogPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".uploads", "analysis", "dent_bake_probe.txt"));
        }
    }
}
