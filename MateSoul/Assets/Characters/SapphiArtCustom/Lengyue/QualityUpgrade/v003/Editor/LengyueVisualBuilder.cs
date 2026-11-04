using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MateSoul.Hair;
using Object=UnityEngine.Object;

/// <summary>Version-isolated visual asset importer. All input data is in authoring coordinates.</summary>
public static class LengyueVisualBuilder
{
    public const string Source="Assets/Characters/SapphiArtCustom/Lengyue/Runtime";
    public const string Root="Assets/Characters/SapphiArtCustom/Lengyue/QualityUpgrade/v003";
    public const string HeadPath="BindJoints/PelvisRoot/Hips/Spine/Chest/Neck/Head 1";
    public static string Work {
        get {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-qualitySource");
            return Path.GetFullPath(i>=0?args[i+1]:Application.dataPath+"/../../ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003");
        }
    }
    [Serializable] public class Weight {public int[] indices;public float[] weights;}
    [Serializable] public class Bind {public float[] values;}
    [Serializable] public class Morph {public string name;public float weight;public Vector3[] positions,normals,tangents;}
    [Serializable] public class Submesh {public int[] triangles;}
    [Serializable] public class Surface {
        public string name,path;public string[] bonePaths;
        public Vector3[] positions,normals;public Vector4[] tangents;public Vector2[] uv0,uv1;
        public int[] triangles;public Weight[] weights;public Bind[] bindposes;public Morph[] morphs;public Submesh[] submeshes;
    }
    [Serializable] public class HairBone {public string name,region;public int parent;public float[] worldPosition,tail;}
    [Serializable] public class HairRig {public HairBone[] bones;}

    public static Mesh Import(string name)
    {
        var data=JsonUtility.FromJson<Surface>(File.ReadAllText(Work+"/generated/"+name+".json"));
        string path=Root+"/Meshes/"+name+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool created=!mesh;
        if(created)mesh=new Mesh();
        mesh.Clear(false);mesh.ClearBlendShapes();mesh.indexFormat=IndexFormat.UInt32;mesh.name=data.name;
        mesh.vertices=data.positions;mesh.normals=data.normals;mesh.uv=data.uv0;mesh.uv2=data.uv1;
        mesh.triangles=data.triangles;
        mesh.bindposes=data.bindposes.Select(b=>{
            Matrix4x4 m=new Matrix4x4();for(int i=0;i<16;i++)m[i/4,i%4]=b.values[i];return m;
        }).ToArray();
        mesh.boneWeights=data.weights.Select(w=>new BoneWeight {
            boneIndex0=w.indices[0],boneIndex1=w.indices[1],boneIndex2=w.indices[2],boneIndex3=w.indices[3],
            weight0=w.weights[0],weight1=w.weights[1],weight2=w.weights[2],weight3=w.weights[3]}).ToArray();
        foreach(var m in data.morphs)mesh.AddBlendShapeFrame(m.name,m.weight,m.positions,m.normals,m.tangents);
        if(data.tangents!=null && data.tangents.Length==mesh.vertexCount)mesh.tangents=data.tangents;
        else mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        if(created)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    public static Texture2D Texture(string name)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+name+".png");

    public static void Prepare()
    {
        foreach(string dir in new[]{"Meshes","Materials","Prefabs","Textures","Shaders","Animations","Scenes"})
            Directory.CreateDirectory(Root+"/"+dir);
        foreach(var file in Directory.GetFiles(Work+"/textures","*.png"))
            File.Copy(file,Root+"/Textures/"+Path.GetFileName(file),true);
        File.Copy(Work+"/scripts/MateSoulCharacterVisual.shader",Root+"/Shaders/MateSoulCharacterVisual.shader",true);
        AssetDatabase.Refresh();
        foreach(string path in Directory.GetFiles(Root+"/Textures","*.png"))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=!(path.Contains("Normal")||path.Contains("Mask")||path.Contains("SDF")||path.Contains("Control")||path.Contains("Detail"));
            importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=2;
            importer.wrapMode=path.Contains("Detail")?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name="Android",overridden=true,maxTextureSize=2048,format=TextureImporterFormat.ASTC_6x6});
            importer.SaveAndReimport();
        }
    }

    public static Material Material(string name,string original)
    {
        string path=Root+"/Materials/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material) {
            material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Source+"/Materials/"+original+".mat"));
            AssetDatabase.CreateAsset(material,path);
        }
        material.name="Lengyue_Visual"+name;
        material.shader=Shader.Find("MateSoul/Character Visual v003");
        material.SetTexture("_BaseMap",Texture(name+"_Base"));
        material.SetTexture("_ShadeMap",Texture(name+"_Shade"));
        material.SetFloat("_ShadeMapStrength",1);
        material.SetFloat("_QualityDebug",0);material.SetFloat("_DebugUV",0);
        material.SetColor("_BaseColor",Color.white);
        material.SetColor("_EmissionColor",Color.black);
        material.SetFloat("_RimIntensity",0);
        material.SetFloat("_SurfaceNormalStrength",0);
        EditorUtility.SetDirty(material);
        return material;
    }

    public static void ConfigureHair(GameObject character,SkinnedMeshRenderer renderer)
    {
        var data=JsonUtility.FromJson<HairRig>(File.ReadAllText(Work+"/generated/hair_rig.json"));
        foreach(var solver in character.GetComponentsInChildren<HairSpringPhysics>(true)) {
            solver.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(solver);
        }
        var previous=character.transform.Find(HeadPath+"/VisualHairRoot");
        if(previous)Object.DestroyImmediate(previous.gameObject);
        var transforms=new Transform[data.bones.Length];
        for(int i=0;i<data.bones.Length;i++)
        {
            var definition=data.bones[i];
            var bone=new GameObject(definition.name).transform;
            bone.SetParent(i==0?character.transform.Find(HeadPath):transforms[definition.parent],false);
            bone.SetPositionAndRotation(new Vector3(definition.worldPosition[0],definition.worldPosition[1],
                definition.worldPosition[2]),Quaternion.identity);
            transforms[i]=bone;
        }
        renderer.bones=transforms;renderer.rootBone=transforms[0];
        var spring=transforms[0].gameObject.AddComponent<HairSpringPhysics>();
        spring.directionReference=character.transform.Find(HeadPath);
        spring.bones=data.bones.Select((b,i)=>new HairSpringPhysics.Bone {
            transform=transforms[i],parent=b.parent,tailLocal=new Vector3(b.tail[0],b.tail[1],b.tail[2]),
            // The fitted scalp profile is fixed; only the final two joints sway.
            full=i>0&&(i-1)%5>=3,balanced=i>0&&(i-1)%5>=3,
            stiffness=.92f,drag=.48f,gravity=.008f,
            lateralInertiaResponse=.12f,maxLateralDegrees=0,maxDepthDegrees=0
        }).ToArray();
        spring.maxBendDegrees=1.2f;spring.maxChainBendDegrees=1.2f;spring.particleRadius=.001f;
        spring.colliders=new[] {
            new HairSpringPhysics.ColliderSphere {transform=transforms[0],offset=new Vector3(0,.11f,-.014f),radius=.082f},
            new HairSpringPhysics.ColliderSphere {transform=transforms[0],offset=new Vector3(0,.045f,-.020f),radius=.079f}
        };
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }

    public static void HairPreview()
    {
        Prepare();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var character=(GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Prefabs/Lengyue_GameCharacter.prefab"));
        var hair=character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.name=="Lengyue_Bob_Hair_v002");
        hair.sharedMesh=Import("Hair");
        var material=Material("Hair","Lengyue_Toon_Hair");
        material.SetFloat("_HairMode",1);
        material.SetTexture("_HairHighlightMap",Texture("Hair_HighlightMask"));
        material.SetTexture("_SurfaceNormalMap",Texture("Hair_Normal"));
        material.SetFloat("_SurfaceNormalStrength",.32f);
        material.SetFloat("_HairHighlightIntensity",.22f);
        material.SetColor("_HairHighlightColor",new Color(.60f,.48f,.84f));
        material.SetFloat("_HairRoughness",.34f);
        material.SetFloat("_HairMidThreshold",.65f);
        material.SetFloat("_ShadowThreshold",.25f);
        material.SetFloat("_ShadowSoftness",.04f);
        material.SetFloat("_ReceiveShadows",.45f);
        material.SetFloat("_IndirectStrength",.14f);
        hair.sharedMaterial=material;
        ConfigureHair(character,hair);
        PrefabUtility.SaveAsPrefabAsset(character,Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab");
        Object.DestroyImmediate(character);
        AssetDatabase.SaveAssets();
        Debug.Log("VISUAL_HAIR_BUILD_COMPLETE");
    }

    public static void Build()
    {
        HairPreview();
        var character=(GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab"));
        var face=Material("Face","Lengyue_Toon_Face");
        face.SetFloat("_FaceMode",1);
        face.SetTexture("_FaceSDF",Texture("Face_SDF"));
        face.SetTexture("_FaceControl",Texture("Face_Control"));
        face.SetFloat("_UnlitBlend",.30f);
        face.SetFloat("_IndirectStrength",.12f);
        face.SetFloat("_ReceiveShadows",.28f);
        face.SetFloat("_ShadowThreshold",.36f);
        face.SetFloat("_ShadowSoftness",.05f);
        var eyes=Material("Eyes","Lengyue_Toon_Eye");
        eyes.SetTexture("_EmissionMap",Texture("Eyes_Highlight"));
        eyes.SetColor("_EmissionColor",new Color(.88f,.88f,.88f));
        eyes.SetFloat("_UnlitBlend",.90f);
        eyes.SetFloat("_SpecularIntensity",.015f);
        var body=Material("Body","Lengyue_Toon_SkinCoverage");
        body.SetFloat("_SkinMode",1);
        body.SetFloat("_UnlitBlend",.32f);
        body.SetFloat("_DiffuseWrap",.22f);
        body.SetFloat("_ShadowSoftness",.085f);
        body.SetFloat("_ReceiveShadows",.35f);
        body.SetFloat("_IndirectStrength",.12f);
        var cloth=Material("Cloth","Lengyue_LayeredDress_Toon");
        cloth.SetTexture("_SurfaceNormalMap",Texture("Cloth_Normal"));
        cloth.SetFloat("_SurfaceNormalStrength",.35f);
        cloth.SetTexture("_FabricDetailMap",Texture("Cloth_Detail"));
        cloth.SetFloat("_FabricAlbedoStrength",.020f);
        cloth.SetFloat("_FabricNormalStrength",.05f);
        cloth.SetFloat("_FabricSheenIntensity",.025f);
        cloth.SetFloat("_SpecularIntensity",.018f);
        cloth.SetFloat("_UnlitBlend",.18f);
        cloth.SetFloat("_DiffuseWrap",.12f);
        cloth.SetFloat("_ShadowThreshold",.40f);
        cloth.SetFloat("_ShadowSoftness",.060f);
        cloth.SetFloat("_ReceiveShadows",.50f);
        cloth.SetFloat("_IndirectStrength",.14f);
        foreach(var skin in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(skin.name=="brow") {
                skin.sharedMaterial=face;
                PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            }
            string mesh=skin.name=="face"?"Face":skin.name=="eyes"?"Eyes":
                skin.name=="LavenderCasual_Garments"?"Garments":
                skin.name=="LavenderCasual_SkinCoverage"?"Body":
                skin.name=="LavenderCasual_NeckExtension"?"Neck":null;
            if(mesh==null)continue;
            var old=skin.sharedMesh;
            var upgraded=Import(mesh);
            if(skin.bones.Length!=upgraded.bindposes.Length ||
                !old.bindposes.SequenceEqual(upgraded.bindposes))
                throw new InvalidOperationException(mesh+" original bindpose contract changed.");
            skin.sharedMesh=upgraded;
            skin.sharedMaterial=mesh=="Face"||mesh=="Neck"?face:mesh=="Eyes"?eyes:
                mesh=="Body"?body:cloth;
            PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
        }
        foreach(var material in new[]{face,eyes,body,cloth})EditorUtility.SetDirty(material);
        // Save the overrides into the existing source variant, without nesting it into itself.
        PrefabUtility.UnpackPrefabInstance(character,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
        PrefabUtility.SaveAsPrefabAsset(character,Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab");
        Object.DestroyImmediate(character);
        AssetDatabase.SaveAssets();
        Debug.Log("VISUAL_FULL_BUILD_COMPLETE");
    }
}
