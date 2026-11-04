using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.CharacterVisual;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LengyueVisualExpressions
{
    static string Root => LengyueVisualBuilder.Root;
    static readonly string[] Expressions = { "Neutral", "Smile", "Laugh", "Shy", "Angry", "Sad", "Surprised" };
    static readonly string[] Visemes = { "None", "A", "I", "U", "E", "O" };

    public static void Build()
    {
        var path = Root + "/Prefabs/Lengyue_VisualUpgrade_v003.prefab";
        var go = PrefabUtility.LoadPrefabContents(path);
        var animator = go.GetComponent<Animator>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            LengyueVisualBuilder.Source + "/Prefabs/Lengyue_GameCharacter.prefab").GetComponent<Animator>();
        animator.runtimeAnimatorController = Create(source.runtimeAnimatorController, go, "Character");
        if (!go.GetComponent<VisualFaceControl>()) go.AddComponent<VisualFaceControl>();
        PrefabUtility.SaveAsPrefabAsset(go, path);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.SaveAssets();
        Debug.Log("VISUAL_EXPRESSIONS_BUILT");
    }

    public static AnimatorController Create(RuntimeAnimatorController source, GameObject go, string label)
    {
        Directory.CreateDirectory(Root + "/Animations");
        string path = Root + "/Animations/" + label + ".controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path)) AssetDatabase.DeleteAsset(path);
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
            throw new InvalidOperationException("Could not copy source Animator.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        controller.AddParameter("VisualExpression", AnimatorControllerParameterType.Int);
        controller.AddParameter("VisualViseme", AnimatorControllerParameterType.Int);
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var face = skins.Single(s => s.name == "face");
        var brow = skins.Single(s => s.name == "brow");
        var paths = new Dictionary<SkinnedMeshRenderer,string> {
            [face] = AnimationUtility.CalculateTransformPath(face.transform, go.transform),
            [brow] = AnimationUtility.CalculateTransformPath(brow.transform, go.transform)
        };
        var layer = Layer(controller, "Visual Expression");
        for (int index=0; index<Expressions.Length; index++)
        {
            var values = ExpressionValues(Expressions[index]);
            var clip = Clip(label + "_Expression_" + Expressions[index], paths, (skin, shape) =>
                values.TryGetValue(shape.Split('.').Last(), out float weight) ? weight : 0,
                (skin, shape) => !shape.StartsWith("Visual.Viseme_"));
            AddState(layer, clip, Expressions[index], index, "VisualExpression");
        }
        layer = Layer(controller, "Visual Mouth");
        for (int index=0; index<Visemes.Length; index++)
        {
            string selected = "Visual.Viseme_" + Visemes[index];
            var clip = Clip(label + "_Viseme_" + Visemes[index], paths,
                (skin, shape) => shape == selected ? 100 : 0,
                (skin, shape) => skin == face &&
                    !(shape.Contains("eye") || shape.Contains("extra")));
            AddState(layer, clip, Visemes[index], index, "VisualViseme");
        }
        foreach (string side in new[] {"L","R"})
        {
            layer = Layer(controller, "Visual Blink " + side);
            var clip = Clip(label + "_Blink_" + side, paths,
                (skin, shape) => shape.EndsWith("eye_close_" + side + "_BS") ? 100 : 0,
                (skin, shape) => skin == face && shape.Contains("eye") &&
                    (shape.EndsWith("_" + side) || shape.EndsWith("_" + side + "_BS")));
            var state = layer.stateMachine.AddState("Blink");
            state.motion = clip;
            state.writeDefaultValues = false;
        }
        AddGaze(controller, go, label);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void AddGaze(AnimatorController controller, GameObject go, string label)
    {
        controller.AddParameter("VisualGazeX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VisualGazeY", AnimatorControllerParameterType.Float);
        var layer = Layer(controller, "Visual Gaze");
        var tree = new BlendTree { name="Eye Direction", blendType=BlendTreeType.FreeformCartesian2D,
            blendParameter="VisualGazeX", blendParameterY="VisualGazeY" };
        AssetDatabase.AddObjectToAsset(tree, controller);
        var eyes=go.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Eye_L"||t.name=="Eye_R").ToArray();
        if(eyes.Length!=2)throw new InvalidOperationException("Expected two production eye bones.");
        for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
        {
            string name=label+"_Gaze_"+x+"_"+y;
            var clip=new AnimationClip {name=name,frameRate=60};
            foreach(var eye in eyes)
            {
                var delta=go.transform.rotation*Quaternion.Euler(-VisualFaceControl.GazePitchLimit*y,
                    VisualFaceControl.GazeYawLimit*x,0)*Quaternion.Inverse(go.transform.rotation);
                var local=Quaternion.Inverse(eye.parent.rotation)*delta*eye.rotation;
                string path=AnimationUtility.CalculateTransformPath(eye,go.transform);
                foreach(var component in new[]{"x","y","z","w"})
                {
                    float value=component=="x"?local.x:component=="y"?local.y:component=="z"?local.z:local.w;
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(
                        path,typeof(Transform),"m_LocalRotation."+component),AnimationCurve.Constant(0,1,value));
                }
            }
            string assetPath=Root+"/Animations/"+name+".anim";
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if(existing) { EditorUtility.CopySerialized(clip,existing);Object.DestroyImmediate(clip);clip=existing; }
            else AssetDatabase.CreateAsset(clip,assetPath);
            tree.AddChild(clip,new Vector2(x,y));
        }
        var state=layer.stateMachine.AddState("Gaze");
        state.writeDefaultValues=false;
        state.motion=tree;
    }

    static AnimatorControllerLayer Layer(AnimatorController controller, string name)
    {
        var machine = new AnimatorStateMachine {name = name};
        AssetDatabase.AddObjectToAsset(machine, controller);
        var layer = new AnimatorControllerLayer {
            name=name, stateMachine=machine, defaultWeight=0,
            blendingMode=AnimatorLayerBlendingMode.Override };
        controller.AddLayer(layer);
        return layer;
    }

    static void AddState(AnimatorControllerLayer layer, AnimationClip clip, string name, int index, string parameter)
    {
        var state = layer.stateMachine.AddState(name);
        state.motion = clip;
        state.writeDefaultValues = false;
        var transition = layer.stateMachine.AddAnyStateTransition(state);
        transition.hasExitTime = false;
        transition.duration = .12f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.Equals, index, parameter);
    }

    static AnimationClip Clip(string name, Dictionary<SkinnedMeshRenderer,string> paths,
        Func<SkinnedMeshRenderer,string,float> value, Func<SkinnedMeshRenderer,string,bool> include)
    {
        string path = Root + "/Animations/" + name + ".anim";
        var clip = new AnimationClip { name=name, frameRate=60 };
        foreach (var pair in paths)
            for (int i=0; i<pair.Key.sharedMesh.blendShapeCount; i++)
            {
                string shape = pair.Key.sharedMesh.GetBlendShapeName(i);
                if (!include(pair.Key, shape)) continue;
                float weight=value(pair.Key, shape);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(pair.Value,
                    typeof(SkinnedMeshRenderer), "blendShape."+shape), AnimationCurve.Constant(0, 1, weight));
            }
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing) { EditorUtility.CopySerialized(clip,existing); Object.DestroyImmediate(clip); return existing; }
        AssetDatabase.CreateAsset(clip,path);
        return clip;
    }

    static Dictionary<string,float> ExpressionValues(string name)
    {
        var v = new Dictionary<string,float>();
        switch(name)
        {
            case "Smile": v["face_smile_BS"]=70; v["face_eyeHappy_L"]=20; v["face_eyeHappy_R"]=20; break;
            case "Laugh": v["face_smile_BS"]=75; v["face_jaw_open_BS"]=55;
                v["face_eyeHappy_L"]=50; v["face_eyeHappy_R"]=50; v["brow_Up_L"]=20; v["brow_Up_R"]=20; break;
            case "Shy": v["face_smile_BS"]=35; v["face_eyeHappy_L"]=18; v["face_eyeHappy_R"]=18;
                v["brow_sad_L"]=15; v["brow_sad_R"]=15; break;
            case "Angry": v["brow_anger_L"]=70; v["brow_anger_R"]=70; v["face_sad_BS"]=15; break;
            case "Sad": v["face_sad_BS"]=65; v["brow_sad_L"]=65; v["brow_sad_R"]=65; break;
            case "Surprised": v["face_jaw_open_BS"]=65; v["face_eye_wide_L_BS"]=40;
                v["face_eye_wide_R_BS"]=40; v["brow_Up_L"]=60; v["brow_Up_R"]=60; break;
        }
        return v;
    }
}
