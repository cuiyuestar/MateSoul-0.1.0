using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Lossless authoring inputs from the frozen production prefab.</summary>
public static class LengyueVisualSnapshot
{
    const string Prefab = "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab";
    static string Output => LengyueVisualBuilder.Work + "/snapshot";
    [Serializable] public class Weight { public int[] indices; public float[] weights; }
    [Serializable] public class Bone
    {
        public string name, path;
        public Vector3 localPosition, localScale;
        public Quaternion localRotation;
        public float[] world;
    }
    [Serializable] public class Morph
    {
        public string name;
        public float weight;
        public Vector3[] positions, normals, tangents;
    }
    [Serializable] public class Submesh { public int[] triangles; }
    [Serializable] public class Matrix { public float[] values; }
    [Serializable] public class Surface
    {
        public string name, path, mesh;
        public bool active;
        public string[] materials, bonePaths;
        public float[] localToWorld;
        public Vector3[] positions, worldPositions, normals;
        public Vector4[] tangents;
        public Vector2[] uv0, uv1;
        public int[] triangles;
        public Submesh[] submeshes;
        public Matrix[] bindposes;
        public Weight[] weights;
        public Morph[] morphs;
    }
    [Serializable] public class Skeleton { public string prefab, unity; public Bone[] bones; }
    static string PathOf(Transform t, Transform root) =>
        t == root ? "" : t.parent == root ? t.name : PathOf(t.parent, root) + "/" + t.name;
    static float[] MatrixOf(Matrix4x4 m) =>
        Enumerable.Range(0, 16).Select(i => m[i / 4, i % 4]).ToArray();
    public static void Run() => Export(Prefab, Output);
    public static void Export(string prefab, string output)
    {
        Directory.CreateDirectory(output);
        Action<string, object> Write = (name, value) =>
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonUtility.ToJson(value));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
        foreach (var b in go.GetComponentsInChildren<Behaviour>(true)) b.enabled = false;
        Write("skeleton", new Skeleton {
            prefab = prefab, unity = Application.unityVersion,
            bones = go.GetComponentsInChildren<Transform>(true).Select(t => new Bone {
                name = t.name, path = PathOf(t, go.transform), localPosition = t.localPosition,
                localRotation = t.localRotation, localScale = t.localScale, world = MatrixOf(t.localToWorldMatrix)
            }).ToArray()
        });
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = r.sharedMesh;
            var baked = new Mesh();
            r.BakeMesh(baked);
            var morphs = Enumerable.Range(0, mesh.blendShapeCount).SelectMany(i =>
                Enumerable.Range(0, mesh.GetBlendShapeFrameCount(i)).Select(j => {
                    var p = new Vector3[mesh.vertexCount];
                    var n = new Vector3[mesh.vertexCount];
                    var t = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, j, p, n, t);
                    return new Morph { name = mesh.GetBlendShapeName(i),
                        weight = mesh.GetBlendShapeFrameWeight(i, j), positions = p, normals = n, tangents = t };
                })).ToArray();
            Write(r.name, new Surface {
                name = r.name, path = PathOf(r.transform, go.transform), mesh = AssetDatabase.GetAssetPath(mesh),
                active = r.enabled && r.gameObject.activeInHierarchy,
                localToWorld = MatrixOf(r.transform.localToWorldMatrix),
                positions = mesh.vertices,
                worldPositions = baked.vertices.Select(v => r.transform.position + r.transform.rotation * v).ToArray(),
                normals = mesh.normals, tangents = mesh.tangents, uv0 = mesh.uv, uv1 = mesh.uv2,
                triangles = mesh.triangles,
                submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(i => new Submesh { triangles = mesh.GetTriangles(i) }).ToArray(),
                bindposes = mesh.bindposes.Select(m => new Matrix { values = MatrixOf(m) }).ToArray(),
                bonePaths = r.bones.Select(t => PathOf(t, go.transform)).ToArray(),
                weights = mesh.boneWeights.Select(w => new Weight {
                    indices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 },
                    weights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 }
                }).ToArray(),
                morphs = morphs, materials = r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()
            });
            Object.DestroyImmediate(baked);
        }
        Object.DestroyImmediate(go);
        Debug.Log("EXACT_SNAPSHOT_COMPLETE " + output);
    }
}
