using MateSoul.CharacterVisual;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class LengyueVisualReviewBuilder
{
    public static string ScenePath => LengyueVisualBuilder.Root+"/Scenes/Lengyue_Visual_AB_v003.unity";

    [MenuItem("MateSoul/冷月外观 v003/生成 A-B 对照入口")]
    public static void Build()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.24f,.25f,.29f);
        var viewer=new GameObject("Original and Visual v003").AddComponent<VisualComparison>();
        viewer.before=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            LengyueVisualBuilder.Source+"/Prefabs/Lengyue_GameCharacter.prefab"));
        viewer.after=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            LengyueVisualBuilder.Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab"));
        viewer.before.name="Original Lengyue";
        viewer.after.name="Visual v003 Lengyue";
        viewer.before.transform.position=new Vector3(-.58f,0,0);
        viewer.after.transform.position=new Vector3(.58f,0,0);
        viewer.reviewCamera=new GameObject("Review Camera",typeof(Camera)).GetComponent<Camera>();
        viewer.reviewCamera.tag="MainCamera";viewer.reviewCamera.orthographic=true;
        viewer.reviewCamera.orthographicSize=.96f;
        viewer.reviewCamera.transform.position=new Vector3(0,.86f,4);
        viewer.reviewCamera.transform.LookAt(new Vector3(0,.86f,0));
        viewer.reviewCamera.clearFlags=CameraClearFlags.SolidColor;
        viewer.reviewCamera.backgroundColor=new Color(.30f,.31f,.34f);
        viewer.key=new GameObject("Matched directional light",typeof(Light)).GetComponent<Light>();
        viewer.key.type=LightType.Directional;viewer.key.intensity=1.05f;viewer.key.shadows=LightShadows.Soft;
        viewer.key.transform.rotation=Quaternion.Euler(35,155,0);
        EditorSceneManager.SaveScene(scene,ScenePath);
        Debug.Log("VISUAL_AB_BUILT "+ScenePath);
    }

    [MenuItem("MateSoul/冷月外观 v003/打开 A-B 对照场景")]
    public static void Open() => EditorSceneManager.OpenScene(ScenePath);
}
