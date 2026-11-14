using System;
using System.IO;
using System.Linq;
using MateSoul.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    // Editor-only, isolated captures of the canonical prefab under fixed lights.
    public static class LengyueAppearanceReview
    {
        static string Repository => Path.GetFullPath(Application.dataPath + "/../..");
        static string Output => Repository + "/ArtSource/Lengyue/Appearance/ReferenceMatch";
        static double nextPoll;

        [Serializable] sealed class Surface
        {
            public string name;
            public Vector3[] positions, normals;
            public Vector4[] tangents;
            public Vector2[] uv;
            public int[] triangles;
        }

        [InitializeOnLoadMethod] static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayState;
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Repository + "/.dbg/appearance-capture";
            if (!File.Exists(request)) return;
            string label = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (label == "prepare") LengyueReferenceHair.Prepare();
                else if (label == "play") RunMotion();
                else Capture(label);
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(Output + "/reports");
                File.WriteAllText(Output + "/reports/capture_error.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        [MenuItem("MateSoul/Lengyue/Capture Current Appearance")]
        public static void CaptureCurrent() => Capture("after");

        public static void StartBatchReview()
        {
            Capture("after");
        }

        static void RunMotion()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Scene active = SceneManager.GetActiveScene();
            bool empty = SceneManager.sceneCount == 1 && !active.isDirty &&
                active.GetRootGameObjects().Length == 0;
            if (!empty && string.IsNullOrEmpty(active.path))
                throw new InvalidOperationException("Save the working scene before motion validation");
            var scene = EditorSceneManager.OpenScene(
                LengyueLayeredDressBuilder.Root + "/Validation/lengyue_validation_garment.unity",
                empty ? OpenSceneMode.Single : OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var probe = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<LengyueLayeredDressProbe>()).Single();
            Object.DestroyImmediate(probe.character);
            probe.character = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(LengyueLayeredDressBuilder.Prefab), scene);
            probe.appearanceCloseup = true;
            probe.reportDirectory = Output + "/reports";
            probe.outputDirectory = Output + "/evidence";
            SessionState.SetBool("LengyueAppearanceEmptyScene", empty);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode ||
                !SessionState.GetBool("LengyueAppearanceEmptyScene", false)) return;
            SessionState.SetBool("LengyueAppearanceEmptyScene", false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Capture(string label)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(label, @"^[a-zA-Z0-9_-]+$"))
                throw new ArgumentException("Expected an artifact label");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Output + "/evidence/" + label);
            Directory.CreateDirectory(Output + "/reports");
            File.Delete(Output + $"/reports/{label}_capture.json");
            File.Delete(Output + "/reports/capture_error.txt");
            var scene = EditorSceneManager.NewPreviewScene();
            var previousAmbient = RenderSettings.ambientLight;
            var previousMode = RenderSettings.ambientMode;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LengyueLayeredDressBuilder.Prefab);
                var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                character.GetComponent<Animator>().enabled = false;
                var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.enabled && s.gameObject.activeInHierarchy).ToArray();
                var hair = skins.Single(s => s.name.Contains("Bob_Hair"));
                var face = skins.Single(s => s.name == "lengyue_face_root");
                if (!hair.sharedMaterial.HasProperty("_HairMode") ||
                    hair.sharedMaterial.GetFloat("_HairMode") < .5f)
                    throw new InvalidOperationException("Updated hair material has not been imported");
                if (hair.sharedMaterial.HasProperty("_HairHighlightMap"))
                {
                    if (!hair.sharedMaterial.GetTexture("_HairHighlightMap") ||
                        hair.sharedMesh.uv2.Length != hair.sharedMesh.vertexCount)
                        throw new InvalidOperationException("Reference hair map/UV2 missing");
                    VerifyRibbons(hair.sharedMaterial);
                }
                if (label == "before")
                {
                    Export(hair);
                    Export(face);
                }
                var headBounds = hair.bounds;
                headBounds.Encapsulate(face.bounds);
                Vector3 headCenter = headBounds.center;
                // Lower the center slightly to leave breathing room below the chin.
                headCenter.y -= .018f;
                var cameraObject = new GameObject("Appearance Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.34f, .35f, .38f);
                camera.orthographic = true;
                camera.nearClipPlane = .02f;
                camera.farClipPlane = 20;
                camera.allowHDR = false;
                var lightObject = new GameObject("Appearance Key", typeof(Light));
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1, .94f, .91f);
                light.intensity = 1.05f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(38, 148, 0);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.27f, .28f, .34f);
                // Bake only for static closeups; live motion is checked separately.
                var proxies = skins.Select(s => {
                    var mesh = new Mesh();
                    s.BakeMesh(mesh);
                    var proxy = new GameObject(s.name, typeof(MeshFilter), typeof(MeshRenderer));
                    SceneManager.MoveGameObjectToScene(proxy, scene);
                    proxy.transform.SetPositionAndRotation(s.transform.position, s.transform.rotation);
                    proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                    proxy.GetComponent<MeshRenderer>().sharedMaterials = s.sharedMaterials;
                    s.enabled = false;
                    return mesh;
                }).ToArray();
                try
                {
                    foreach (var angle in new[] { 0f, 35f, 90f, 180f, -35f })
                    {
                        Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                        camera.transform.position = headCenter + direction * 3;
                        camera.transform.LookAt(headCenter);
                        camera.orthographicSize = .23f;
                        Save(camera, Output + $"/evidence/{label}/head_{angle:0}.png");
                    }
                    camera.transform.position = headCenter + Vector3.forward * 3;
                    camera.transform.LookAt(headCenter);
                    foreach (var rotation in new[] { 90f, 210f })
                    {
                        light.transform.rotation = Quaternion.Euler(38, rotation, 0);
                        Save(camera, Output + $"/evidence/{label}/light_{rotation:0}.png");
                    }
                    light.transform.rotation = Quaternion.Euler(38, 148, 0);
                    if (label != "before")
                    {
                        int faceIndex = Array.IndexOf(skins, face);
                        foreach (string expression in new[] { "face_smile_BS", "face_jaw_open_BS" })
                        {
                            int index = face.sharedMesh.GetBlendShapeIndex("Face_Blendshape." + expression);
                            if (index < 0) throw new InvalidOperationException("Missing " + expression);
                            float previousWeight = face.GetBlendShapeWeight(index);
                            face.SetBlendShapeWeight(index, 75);
                            face.BakeMesh(proxies[faceIndex]);
                            Save(camera, Output + $"/evidence/{label}/{expression}.png");
                            face.SetBlendShapeWeight(index, previousWeight);
                        }
                        face.BakeMesh(proxies[faceIndex]);
                    }
                    camera.transform.position = new Vector3(0, .91f, 3);
                    camera.transform.LookAt(new Vector3(0, .91f, 0));
                    camera.orthographicSize = .97f;
                    Save(camera, Output + $"/evidence/{label}/full_front.png");
                    var shader = hair.sharedMaterial.shader;
                    var errors = ShaderUtil.GetShaderMessages(shader)
                        .Where(m => m.severity.ToString() == "Error").ToArray();
                    if (errors.Length > 0)
                        throw new InvalidOperationException(string.Join("\n", errors.Select(e => e.message)));
                    if (!shader.isSupported) throw new InvalidOperationException("Shader unsupported");
                    File.WriteAllText(Output + $"/reports/{label}_capture.json",
                        "{\"passed\":true,\"unity\":\"" + Application.unityVersion +
                        "\",\"views\":" + (label != "before" ? 10 : 8) +
                        ",\"shaderErrors\":0,\"hairTangents\":" +
                        hair.sharedMesh.tangents.Length + ",\"hairVertices\":" +
                        hair.sharedMesh.vertexCount + ",\"headCenter\":" +
                        JsonUtility.ToJson(headCenter) + "}");
                }
                finally { foreach (var mesh in proxies) Object.DestroyImmediate(mesh); }
            }
            finally
            {
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientMode = previousMode;
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("LENGYUE_APPEARANCE_CAPTURE_PASS " + label);
        }

        static void Export(SkinnedMeshRenderer skin)
        {
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            var surface = new Surface {
                name = skin.name,
                positions = mesh.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray(),
                normals = mesh.normals.Select(v => matrix.MultiplyVector(v)).ToArray(),
                tangents = mesh.tangents,
                uv = mesh.uv, triangles = mesh.triangles
            };
            File.WriteAllText(Output + "/reports/" + skin.name + "_surface.json",
                JsonUtility.ToJson(surface));
            Object.DestroyImmediate(mesh);
        }

        static void VerifyRibbons(Material material)
        {
            var map = material.GetTexture("_HairHighlightMap");
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map));
            if (importer.sRGBTexture || !importer.mipmapEnabled)
                throw new InvalidOperationException("Ribbon atlas must use linear RGB with mipmaps");
            var target = RenderTexture.GetTemporary(256, 256, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(256, 256, TextureFormat.RGB24, false, true);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(map, target);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                pixels.Apply();
                var colors = pixels.GetPixels();
                if (!colors.Any(c => c.r > .3f) || !colors.Any(c => c.r < .01f))
                    throw new InvalidOperationException("Hair light-mass map missing after import");
                File.WriteAllText(Output + "/reports/ribbon_import.json",
                    "{\"linear\":true,\"mipmaps\":true,\"macroLightMass\":true}");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(pixels);
            }
        }

        static void Save(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1200, 1200, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
