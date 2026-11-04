from pathlib import Path
R=Path(__file__).resolve().parent;U=R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal'
s=(U/'Fix54/Editor/Direct54Build.cs').read_text().replace('Direct54','Direct55').replace('Fix54','Fix55');s=s.replace('Fix53/Onigawara_Lengyue_Direct53_SoftFace.prefab','Fix54/Onigawara_Lengyue_Direct54.prefab').replace('new[]{"Face","Hair","Body"}','new[]{"Hair"}').replace('new[]{"Face","Hair","Body","HairStrandLines_Direct51"}','new[]{"Hair","HairStrandLines_Direct51"}')
a=s.index(' static void Render(');e=s.index(' public static void Export()',a)
s=s[:a]+''' static void Render(GameObject prefab,string version){foreach(float angle in new[]{0f,45f,90f,270f}){Clean();var go=Object.Instantiate(prefab);Zero(go);Lights();var cam=EyeDirectInspect.Camera(angle);EyeDirectInspect.Shot(cam,W+"/renders/"+version+"/head_"+angle+".png");}}
'''+s[e:];s=s.replace('冷月Direct55_体态刘海耳廓.unitypackage','冷月Direct55_刘海间隙精修.unitypackage');(U/'Fix55/Editor/Direct55Build.cs').write_text(s)
s=(U/'Fix54/Editor/Direct54Lines.cs').read_text().replace('Direct54','Direct55').replace('Fix54','Fix55');(U/'Fix55/Editor/Direct55Lines.cs').write_text(s)
