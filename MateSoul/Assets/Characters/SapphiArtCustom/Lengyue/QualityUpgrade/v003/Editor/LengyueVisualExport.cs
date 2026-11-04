using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MateSoul.Hair;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LengyueVisualExport
{
    [Serializable] public class Number { public string name; public float value; }
    [Serializable] public class ColorValue { public string name; public Color value; }
    [Serializable] public class TextureValue { public string name, file; }
    [Serializable] public class MaterialValue
    {
        public string name, path, shader;
        public Number[] floats;
        public ColorValue[] colors;
        public TextureValue[] textures;
    }
    [Serializable] public class Spring
    {
        public string path;
        public int parent;
        public bool full, balanced;
        public Vector3 tailLocal;
        public float stiffness, drag, gravity, maxLateralDegrees, maxDepthDegrees;
    }
    [Serializable] public class Sphere { public string path; public Vector3 offset; public float radius; }
    [Serializable] public class ExportSettings
    {
        public MaterialValue[] materials;
        public Spring[] springs;
        public Sphere[] colliders;
        public float particleRadius, maxBendDegrees, maxChainBendDegrees;
    }

    public static void Run()
    {
        LengyueVisualReviewBuilder.Build();
        string prefab=LengyueVisualBuilder.Root+"/Prefabs/Lengyue_VisualUpgrade_v003.prefab";
        string output=LengyueVisualBuilder.Work+"/production_snapshot";
        LengyueVisualSnapshot.Export(prefab,output);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
        var hair=go.GetComponentsInChildren<HairSpringPhysics>(true).Single(h=>h.enabled);
        foreach(var b in go.GetComponentsInChildren<Behaviour>(true))b.enabled=false;
        string PathOf(Transform t)=>AnimationUtility.CalculateTransformPath(t,go.transform);
        var materials=go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r=>r.enabled&&r.gameObject.activeInHierarchy)
            .SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
        Directory.CreateDirectory(output+"/textures");
        var settings=new ExportSettings {
            materials=materials.Select(m=> {
                var textures=new List<TextureValue>();
                foreach(string property in m.GetTexturePropertyNames())
                {
                    var texture=m.GetTexture(property);
                    if(!texture)continue;
                    string source=AssetDatabase.GetAssetPath(texture);
                    string filename=AssetDatabase.AssetPathToGUID(source)+Path.GetExtension(source);
                    File.Copy(source,output+"/textures/"+filename,true);
                    textures.Add(new TextureValue {name=property,file=filename});
                }
                var numbers=new List<Number>();
                var colors=new List<ColorValue>();
                for(int i=0;i<ShaderUtil.GetPropertyCount(m.shader);i++)
                {
                    string property=ShaderUtil.GetPropertyName(m.shader,i);
                    var type=ShaderUtil.GetPropertyType(m.shader,i);
                    if(type==ShaderUtil.ShaderPropertyType.Float||type==ShaderUtil.ShaderPropertyType.Range)
                        numbers.Add(new Number {name=property,value=m.GetFloat(property)});
                    if(type==ShaderUtil.ShaderPropertyType.Color)
                        colors.Add(new ColorValue {name=property,value=m.GetColor(property)});
                }
                return new MaterialValue {name=m.name,path=AssetDatabase.GetAssetPath(m),shader=m.shader.name,
                    floats=numbers.ToArray(),colors=colors.ToArray(),textures=textures.ToArray()};
            }).ToArray(),
            springs=hair.bones.Select(b=>new Spring {path=PathOf(b.transform),parent=b.parent,full=b.full,balanced=b.balanced,
                tailLocal=b.tailLocal,stiffness=b.stiffness,drag=b.drag,gravity=b.gravity,
                maxLateralDegrees=b.maxLateralDegrees,maxDepthDegrees=b.maxDepthDegrees}).ToArray(),
            colliders=hair.colliders.Select(c=>new Sphere {path=PathOf(c.transform),offset=c.offset,radius=c.radius}).ToArray(),
            particleRadius=hair.particleRadius,maxBendDegrees=hair.maxBendDegrees,maxChainBendDegrees=hair.maxChainBendDegrees
        };
        File.WriteAllText(output+"/export_settings.json",JsonUtility.ToJson(settings,true));
        Object.DestroyImmediate(go);
        Debug.Log("VISUAL_EXPORT_SNAPSHOT_COMPLETE");
    }
}
