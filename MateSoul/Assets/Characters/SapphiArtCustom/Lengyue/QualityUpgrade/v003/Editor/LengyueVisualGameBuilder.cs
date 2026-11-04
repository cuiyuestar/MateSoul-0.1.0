using System;
using System.IO;
using System.Linq;
using MateSoul.CharacterVisual;
using MateSoul.Hair;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LengyueVisualGameBuilder
{
    public static string ScenePath => LengyueVisualBuilder.Root+"/Scenes/MateSoul_Visual_v003.unity";
    public static string PrefabPath => LengyueVisualBuilder.Root+"/Prefabs/Lengyue_VisualRuntime_v003.prefab";

    [MenuItem("MateSoul/冷月外观 v003/生成游戏入口")]
    public static void Run()
    {
        var visual=AssetDatabase.LoadAssetAtPath<GameObject>(
            LengyueVisualBuilder.Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab");
        var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MateSoul/Content/Lengyue_Runtime.prefab");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(original);
        var replacements=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .ToDictionary(r=>AnimationUtility.CalculateTransformPath(r.transform,visual.transform));
        foreach(var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string path=AnimationUtility.CalculateTransformPath(skin.transform,go.transform);
            if(!replacements.TryGetValue(path,out var source))continue;
            if(skin.name!="Lengyue_Bob_Hair_v002" &&
                !skin.sharedMesh.bindposes.SequenceEqual(source.sharedMesh.bindposes))
                throw new InvalidOperationException("Game wrapper bindpose mismatch: "+path);
            skin.sharedMesh=source.sharedMesh;
            skin.sharedMaterials=source.sharedMaterials;
            skin.localBounds=source.localBounds;
            PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
        }
        // Clone the new rig in local coordinates, including its sole solver's
        // serialized references; this does not require external authoring JSON.
        foreach(var solver in go.GetComponentsInChildren<HairSpringPhysics>(true))
        { solver.enabled=false; PrefabUtility.RecordPrefabInstancePropertyModifications(solver); }
        var head=go.transform.Find(LengyueVisualBuilder.HeadPath);
        var sourceRoot=visual.transform.Find(LengyueVisualBuilder.HeadPath+"/VisualHairRoot");
        var root=Object.Instantiate(sourceRoot.gameObject,head,false);
        root.name="VisualHairRoot";
        var hair=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.name=="Lengyue_Bob_Hair_v002");
        var sourceHair=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.name==hair.name);
        hair.bones=sourceHair.bones.Select(t=>go.transform.Find(
            AnimationUtility.CalculateTransformPath(t,visual.transform))).ToArray();
        hair.rootBone=root.transform;
        var spring=root.GetComponent<HairSpringPhysics>();
        spring.directionReference=head;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hair);
        var animator=go.GetComponent<Animator>();
        animator.runtimeAnimatorController=LengyueVisualExpressions.Create(animator.runtimeAnimatorController,go,"Game");
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        go.AddComponent<VisualFaceControl>();
        var candidate=PrefabUtility.SaveAsPrefabAsset(go,PrefabPath);
        Object.DestroyImmediate(go);
        var scene=EditorSceneManager.OpenScene("Assets/MateSoul/Scenes/MateSoul.unity",OpenSceneMode.Single);
        var bootstrap=Object.FindObjectOfType<GameBootstrap>();
        var serialized=new SerializedObject(bootstrap);
        var previous=(GameObject)serialized.FindProperty("character").objectReferenceValue;
        var character=(GameObject)PrefabUtility.InstantiatePrefab(candidate);
        character.transform.SetPositionAndRotation(previous.transform.position,previous.transform.rotation);
        character.transform.localScale=previous.transform.localScale;
        character.SetActive(previous.activeSelf);
        serialized.FindProperty("character").objectReferenceValue=character;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Object.DestroyImmediate(previous);
        bootstrap.gameObject.AddComponent<VisualExpressionRegistration>()
            .Configure(bootstrap,character.GetComponent<VisualFaceControl>());
        Directory.CreateDirectory(LengyueVisualBuilder.Root+"/Scenes");
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("VISUAL_GAME_BUILT "+ScenePath);
    }

    [MenuItem("MateSoul/冷月外观 v003/打开游戏场景")]
    public static void OpenGame() => EditorSceneManager.OpenScene(ScenePath);
}
