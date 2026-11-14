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
                "/../../ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261029/production_validation.json");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
                var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var body = skins.Single(s => s.name == "lengyue_body_root");
                var face = skins.Single(s => s.name == "lengyue_face_root");
                var hair = skins.Single(s => s.name == "lengyue_hair_root");
                if (prefab.GetComponentsInChildren<Animator>(true).Length != 1 ||
                    face.sharedMesh.blendShapeCount != 61 ||
                    !AssetDatabase.GetAssetPath(face.sharedMesh).EndsWith("/Face_Direct54_GameExpressions.asset") ||
                    !AssetDatabase.GetAssetPath(hair.sharedMesh).EndsWith("/Hair_Direct59.asset"))
                    throw new InvalidOperationException("Production character does not use the current model.");
                if (!AssetDatabase.GetAssetPath(body.sharedMaterials[0]).EndsWith("/BodySkin_Direct56.mat") ||
                    !AssetDatabase.GetAssetPath(face.sharedMaterials[2]).EndsWith("/FaceExpression_Direct57.mat"))
                    throw new InvalidOperationException("Production character does not use the current skin textures.");
                var lines = skins.Single(s => s.name == "lengyue_hair_strand_lines");
                if (!lines.bones.SequenceEqual(hair.bones) ||
                    !lines.sharedMesh.bindposes.SequenceEqual(hair.sharedMesh.bindposes) ||
                    !AssetDatabase.GetAssetPath(lines.sharedMesh).EndsWith("/HairStrandLines_Direct59.asset") ||
                    !lines.sharedMaterial.shader.name.Contains("Direct51 Hair Strand Lines"))
                    throw new InvalidOperationException("Production hair strand lines are not bound to Direct59.");
                foreach (var skin in skins)
                    if (!skin.sharedMesh || skin.bones.Any(b => !b) || skin.sharedMaterials.Any(m => !m || !m.shader))
                        throw new InvalidOperationException("Broken skin dependency: " + skin.name);
                if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c == null))
                    throw new InvalidOperationException("Production character has a missing script.");
                var cloth = skins.Single(s => s.name == "lengyue_garment_adapted");
                if (!AssetDatabase.GetAssetPath(cloth.sharedMaterials[0]).Contains("SoftFit01"))
                    throw new InvalidOperationException("Production clothing does not use SoftFit01.");
                var bones = prefab.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                if (Mathf.Abs(bones["lengyue_foot_l"].position.z + .018681f) > .00001f ||
                    Mathf.Abs(bones["lengyue_foot_r"].position.z + .018681f) > .00001f)
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
                    bodyMaterial = AssetDatabase.GetAssetPath(body.sharedMaterials[0]),
                    faceExpressionMaterial = AssetDatabase.GetAssetPath(face.sharedMaterials[2]),
                    clothing = AssetDatabase.GetAssetPath(cloth.sharedMesh),
                    footZ = bones["lengyue_foot_l"].position.z,
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
            public string prefab, scene, face, hair, bodyMaterial, faceExpressionMaterial, clothing, error;
            public float footZ;
            public int skins, scripts;
        }
    }
}
