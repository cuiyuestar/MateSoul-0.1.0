using System;
using System.IO;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LengyueIntegration.Editor
{
    public static class ProductionCharacterValidation
    {
        public static void Run()
        {
            string output = Path.GetFullPath(Application.dataPath +
                "/../../ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261026/production_validation.json");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
                var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var face = skins.Single(s => s.name == "Face");
                var hair = skins.Single(s => s.name == "Hair");
                if (prefab.GetComponentsInChildren<Animator>(true).Length != 1 ||
                    face.sharedMesh.blendShapeCount != 61 ||
                    !AssetDatabase.GetAssetPath(face.sharedMesh).EndsWith("/Face_Direct54_GameExpressions.asset") ||
                    !AssetDatabase.GetAssetPath(hair.sharedMesh).EndsWith("/Hair_Direct55.asset"))
                    throw new InvalidOperationException("Production character does not use the current model.");
                var lines = skins.Single(s => s.name == "HairStrandLines_Direct55");
                if (!lines.bones.SequenceEqual(hair.bones) ||
                    !lines.sharedMesh.bindposes.SequenceEqual(hair.sharedMesh.bindposes) ||
                    !AssetDatabase.GetAssetPath(lines.sharedMesh).EndsWith("/HairStrandLines_Direct55.asset") ||
                    !lines.sharedMaterial.shader.name.Contains("Direct51 Hair Strand Lines"))
                    throw new InvalidOperationException("Production hair strand lines are not bound to Direct55.");
                foreach (var skin in skins)
                    if (!skin.sharedMesh || skin.bones.Any(b => !b) || skin.sharedMaterials.Any(m => !m || !m.shader))
                        throw new InvalidOperationException("Broken skin dependency: " + skin.name);
                if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c == null))
                    throw new InvalidOperationException("Production character has a missing script.");
                var cloth = skins.Single(s => s.name == "Lengyue_AdaptedGarments");
                if (!AssetDatabase.GetAssetPath(cloth.sharedMaterials[0]).Contains("SoftFit01"))
                    throw new InvalidOperationException("Production clothing does not use SoftFit01.");
                var bones = prefab.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                if (Mathf.Abs(bones["Foot_L"].position.z + .018681f) > .00001f ||
                    Mathf.Abs(bones["Foot_R"].position.z + .018681f) > .00001f)
                    throw new InvalidOperationException("Production foot rest position changed.");
                var scene = EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
                var motor = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AvatarMotor>(true)).Single();
                var sceneSkins = motor.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .ToDictionary(s => s.name);
                foreach (var skin in skins)
                    if (!sceneSkins.TryGetValue(skin.name, out var sceneSkin) ||
                        sceneSkin.sharedMesh != skin.sharedMesh ||
                        !sceneSkin.sharedMaterials.SequenceEqual(skin.sharedMaterials))
                        throw new InvalidOperationException("Game scene has a different skin: " + skin.name);
                File.WriteAllText(output, JsonUtility.ToJson(new Report {
                    pass = true, prefab = GameSceneBuilder.CharacterPath, scene = GameSceneBuilder.ScenePath,
                    face = AssetDatabase.GetAssetPath(face.sharedMesh),
                    hair = AssetDatabase.GetAssetPath(hair.sharedMesh),
                    clothing = AssetDatabase.GetAssetPath(cloth.sharedMesh),
                    footZ = bones["Foot_L"].position.z,
                    skins = skins.Length, scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true).Length
                }, true));
                Debug.Log("PRODUCTION_CHARACTER_VALIDATION_COMPLETE");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(output, JsonUtility.ToJson(new Report { pass = false, error = e.Message }, true));
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
        [Serializable] class Report
        {
            public bool pass;
            public string prefab, scene, face, hair, clothing, error;
            public float footZ;
            public int skins, scripts;
        }
    }
}
