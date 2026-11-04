using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MateSoul.CharacterVisual;
using MateSoul.Core;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LengyueVisualGameProbe
{
    [Serializable] sealed class Report
    {
        public string scene, status, unity;
        public int frames, readyWarmupFrames=30;
        public List<string> errors=new List<string>();
        public List<string> actions=new List<string>();
        public List<string> images=new List<string>();
    }
    static Report report;
    static GameBootstrap game;
    static VisualFaceControl face;
    static int firstFrame=-1,lastFrame=-1,readyFrame=-1,stage;
    static bool poseCaptured;
    static double started;
    static ActionHandle current;
    static string output;
    static Transform leftEye;
    static Quaternion eyeRest;

    public static void Run()
    {
        if(PlayerSettings.productName!="MateSoulVisualValidation")
            throw new InvalidOperationException("Set the isolated validation project's productName before launching Unity.");
        LengyueVisualGameBuilder.Run();
        SessionState.SetString("VisualGameOutput",LengyueVisualBuilder.Work);
        EditorApplication.playModeStateChanged+=Play;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Restore()
    {
        if(string.IsNullOrEmpty(SessionState.GetString("VisualGameOutput","")))return;
        if(EditorApplication.isPlaying)
        {
            EditorApplication.delayCall+=()=>Play(PlayModeStateChange.EnteredPlayMode);
            return;
        }
        EditorApplication.playModeStateChanged-=Play;
        EditorApplication.playModeStateChanged+=Play;
    }

    static void Play(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode)return;
        if(report!=null)return;
        EditorApplication.playModeStateChanged-=Play;
        output=SessionState.GetString("VisualGameOutput","");
        report=new Report {scene=LengyueVisualGameBuilder.ScenePath,unity=Application.unityVersion};
        started=EditorApplication.timeSinceStartup;
        Time.captureFramerate=60;
        Application.runInBackground=true;
        Application.logMessageReceived+=Log;
        EditorApplication.update+=Tick;
        Debug.Log("VISUAL_GAME_PROBE_STARTED");
    }

    static void Log(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)
            report.errors.Add(message);
    }

    static void Tick()
    {
        try
        {
            if(!EditorApplication.isPlaying)return;
            if(EditorApplication.timeSinceStartup-started>180)
            { report.errors.Add("Actual game validation timed out."); Finish(); return; }
            if(Time.frameCount==lastFrame)return;
            lastFrame=Time.frameCount;
            if(!game)game=Object.FindObjectOfType<GameBootstrap>();
            if(!game||game.Session==null)return;
            if(game.Session.Paused)game.SendMessage("OnApplicationFocus",true);
            if(!game.Session.ItemsReady)
            {
                if(game.Session.ItemRestoreFailed)
                    throw new InvalidOperationException("Validation room item restore failed: "+game.Status);
                _ = game.Session.RestoreItemsAsync();
                return;
            }
            if(!face)face=Object.FindObjectOfType<VisualFaceControl>();
            if(!face)throw new InvalidOperationException("Game candidate face control missing.");
            if(!game.Session.Catalog.TryGet("expression",out _))return;
            if(firstFrame<0)
            {
                // The first Editor-ready callback can precede renderer updates.
                // Record it separately; start the comparison after 30 live frames.
                if(readyFrame<0) { readyFrame=Time.frameCount; Capture("first_ready"); }
                if(Time.frameCount-readyFrame<report.readyWarmupFrames)return;
                firstFrame=Time.frameCount;
                leftEye=face.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Eye_L");
                eyeRest=leftEye.localRotation;
                game.SendMessage("OnApplicationFocus",true);
                Capture("ready");
                Debug.Log("VISUAL_ACTUAL_GAME_READY "+game.Status);
            }
            report.frames=Time.frameCount-firstFrame;
            if(current!=null)
            {
                if(!current.IsFinished)
                {
                    if(current.Intent.ActionId=="expression" && current.ElapsedSeconds>.25f && !poseCaptured)
                    {
                        var skins=face.GetComponentsInChildren<SkinnedMeshRenderer>();
                        float total=skins.Sum(s=>Enumerable.Range(0,s.sharedMesh.blendShapeCount)
                            .Sum(i=>Mathf.Abs(s.GetBlendShapeWeight(i))));
                        bool gaze=stage>=14&&stage<=17;
                        if(gaze ? Quaternion.Angle(eyeRest,leftEye.localRotation)<5 : total<1)
                            throw new InvalidOperationException("Facial action produced no deformation or eye rotation.");
                        Capture("pose_"+stage);
                        poseCaptured=true;
                    }
                    return;
                }
                report.actions.Add(current.Intent.ActionId+":"+current.State+":"+current.Error);
                if(current.State!=ActionState.Succeeded)
                    report.errors.Add("Action failed: "+current.Intent.ActionId+" "+current.Error);
                if(current.Intent.ActionId=="outfit")Capture("outfit_"+stage);
                current=null;
                poseCaptured=false;
                stage++;
            }
            if(stage==0)
            {
                Vector3 desired=face.transform.position+Vector3.forward*.8f;
                if(!game.Room.TrySamplePosition(desired,1,out var target))
                    throw new InvalidOperationException("No game navigation target.");
                current=game.Session.Submit(new AvatarIntent("move",IntentSource.UI,payload:target));
            }
            else if(stage<=6)
            {
                current=game.Session.Submit(new AvatarIntent("expression",IntentSource.UI,
                    payload:new FacialPoseRequest((VisualExpression)stage,.7f)));
            }
            else if(stage<=11)
            {
                current=game.Session.Submit(new AvatarIntent("expression",IntentSource.UI,
                    payload:new FacialPoseRequest(VisualExpression.Neutral,.5f,1,(VisualViseme)(stage-6))));
            }
            else if(stage==12 || stage==13)
            {
                current=game.Session.Submit(new AvatarIntent("outfit",IntentSource.UI,
                    payload:stage==12 ? "pink-pajamas" : game.Session.Wardrobe.InitialOutfitId));
            }
            else if(stage<=17)
            {
                var head=face.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Head 1");
                var direction=new Vector3(stage==14?-.364f:stage==15?.364f:0,
                    stage==16?.213f:stage==17?-.213f:0,1);
                current=game.Session.Submit(new AvatarIntent("expression",IntentSource.UI,
                    payload:new FacialPoseRequest(VisualExpression.Neutral,.5f,
                        gaze:head.position+face.transform.rotation*direction)));
            }
            else if(stage<=20)
            {
                current=game.Session.Submit(new AvatarIntent("expression",IntentSource.UI,
                    payload:new FacialPoseRequest(VisualExpression.Neutral,.5f,
                        blink:new Vector2(stage==19?0:1,stage==18?0:1))));
            }
            else
            {
                Capture("complete");
                report.status=game.Status;
                Finish();
            }
        }
        catch(Exception e)
        {
            report.errors.Add(e.ToString());
            Finish();
        }
    }

    static void Capture(string name)
    {
        var camera=Camera.main;
        if(!camera)throw new InvalidOperationException("Actual game camera missing.");
        var bounds=face.GetComponentsInChildren<SkinnedMeshRenderer>().First().bounds;
        foreach(var skin in face.GetComponentsInChildren<SkinnedMeshRenderer>())bounds.Encapsulate(skin.bounds);
        camera.transform.position=bounds.center+new Vector3(0,.1f,2.4f);
        camera.transform.LookAt(bounds.center);
        Directory.CreateDirectory(output+"/review/game");
        string path=output+"/review/game/"+name+".png";
        var target=RenderTexture.GetTemporary(1280,960,24);
        var previous=RenderTexture.active;
        var image=new Texture2D(1280,960,TextureFormat.RGB24,false);
        camera.targetTexture=target;
        camera.Render();
        RenderTexture.active=target;
        image.ReadPixels(new Rect(0,0,1280,960),0,0);
        image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=null;
        RenderTexture.active=previous;
        RenderTexture.ReleaseTemporary(target);
        Object.DestroyImmediate(image);
        report.images.Add(path);
    }

    static void Finish()
    {
        EditorApplication.update-=Tick;
        Application.logMessageReceived-=Log;
        Time.captureFramerate=0;
        File.WriteAllText(output+"/reports/actual_game_playmode.json",JsonUtility.ToJson(report,true));
        SessionState.EraseString("VisualGameOutput");
        Debug.Log("VISUAL_ACTUAL_GAME_COMPLETE actions="+report.actions.Count+" errors="+report.errors.Count);
        EditorApplication.Exit(report.errors.Count==0 ? 0 : 1);
    }
}
