using System;
using System.Collections.Generic;
using System.IO;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Editor
{
    public static class CarrotBuilder
    {
        public const string DefinitionPath =
            "Assets/MateSoul/Content/Props/Carrot.asset";
        public const string PrefabPath =
            "Assets/MateSoul/Content/Props/Carrot.prefab";
        public const string VisualMeshPath =
            "Assets/MateSoul/Content/PropsSource/Carrot/CarrotRootVisual.asset";
        public const string CollisionMeshPath =
            "Assets/MateSoul/Content/PropsSource/Carrot/CarrotRootCollision.asset";
        public const string CrownMeshPath =
            "Assets/MateSoul/Content/PropsSource/Carrot/CarrotCrown.asset";

        private const string SourceRoot =
            "Assets/MateSoul/Content/PropsSource/Carrot";
        private const string TextureRoot = SourceRoot + "/Textures";
        private const string MaterialRoot =
            "Assets/MateSoul/Content/Props/Materials/Carrot";
        private const float BodyHeight = .23f;
        private const float MaximumRadius = .036f;

        [MenuItem("MateSoul/Content/Build Carrot")]
        public static ItemDefinition BuildFromMenu()
        {
            var focus = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat");
            return Build(focus);
        }

        [MenuItem("MateSoul/Content/Render Carrot Validation")]
        public static void RenderValidationPreview()
        {
            var definition = BuildFromMenu();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Validation Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .08f, .09f, 1f);
            camera.fieldOfView = 32f;
            camera.transform.position = new Vector3(.28f, .31f, -.82f);
            camera.transform.LookAt(new Vector3(0, .12f, 0));
            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, .92f, .82f);
            light.intensity = 1.35f;
            light.transform.rotation = Quaternion.Euler(38f, -28f, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.28f, .31f, .36f);

            var full = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab);
            full.name = "Full Carrot";
            full.transform.position = new Vector3(-.17f, 0, 0);
            var horizontal = CreateValidationPiece(definition,
                new Plane(Vector3.up, new Vector3(0, .125f, 0)), false);
            horizontal.name = "Horizontal Cut";
            horizontal.transform.position = new Vector3(.02f, 0, 0);
            var obliqueNormal =
                Quaternion.AngleAxis(48f, Vector3.forward) * Vector3.up;
            var oblique = CreateValidationPiece(definition,
                new Plane(obliqueNormal, new Vector3(0, .13f, 0)), false);
            oblique.name = "Oblique Cut";
            oblique.transform.position = new Vector3(.21f, 0, 0);

            var texture = new RenderTexture(1200, 720, 24,
                RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height,
                TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            var path = "/tmp/matesoul-carrot-validation.png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("CARROT_VALIDATION_RENDER path=" + path);
        }

        private static GameObject CreateValidationPiece(ItemDefinition definition,
            Plane plane, bool positive)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab);
            var cuttable = root.GetComponent<RuntimeCuttable>();
            cuttable.Bind(definition.Cuttable);
            using (var pair = cuttable.CreateCut(plane))
            {
                if (pair == null)
                    throw new InvalidOperationException(
                        "Carrot validation cut failed.");
                cuttable.ApplyPiece(positive ? pair.Positive : pair.Negative);
            }
            return root;
        }

        public static ItemDefinition Build(Material focusMaterial)
        {
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(TextureRoot);
            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory("Assets/MateSoul/Content/Props");
            AssetDatabase.Refresh();
            ConfigureTextures();

            var visual = SaveMesh(CreateRootMesh(32, 12, true), VisualMeshPath);
            var collision = SaveMesh(CreateRootMesh(12, 6, false), CollisionMeshPath);
            var crownMesh = SaveMesh(CreateCrownMesh(), CrownMeshPath);
            var exterior = BuildExteriorMaterial();
            var crownMaterial = BuildLitMaterial("CarrotLeaves",
                new Color(.12f, .39f, .075f, 1f), .18f, true);
            var interior = BuildInteriorMaterial();
            var settings = new CuttableSettings(interior, collision, Vector3.zero,
                Vector3.up, BodyHeight, MaximumRadius, RadiusCurve(), 3, .03f);

            var root = new GameObject("胡萝卜");
            try
            {
                var bodyView = new GameObject("Carrot Root");
                bodyView.transform.SetParent(root.transform, false);
                var bodyFilter = bodyView.AddComponent<MeshFilter>();
                bodyFilter.sharedMesh = visual;
                var bodyRenderer = bodyView.AddComponent<MeshRenderer>();
                bodyRenderer.sharedMaterials = new[] { exterior, interior };

                var crown = new GameObject("Carrot Crown");
                crown.transform.SetParent(root.transform, false);
                crown.transform.localPosition = Vector3.up * BodyHeight;
                var crownFilter = crown.AddComponent<MeshFilter>();
                crownFilter.sharedMesh = crownMesh;
                var crownRenderer = crown.AddComponent<MeshRenderer>();
                crownRenderer.sharedMaterial = crownMaterial;

                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = collision;
                collider.convex = true;
                var physics = new BodySettings(.12f, PhysicsContentBuilder.Food,
                    .035f, .07f);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var cuttable = root.AddComponent<RuntimeCuttable>();
                cuttable.Configure(bodyFilter, bodyRenderer, collider,
                    crown.transform, settings);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var definition =
                    AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                definition.Configure("carrot", "胡萝卜", prefab,
                    new[] { "visit", "runtime-cuttable" }, true,
                    new Vector2(MaximumRadius * 2f, MaximumRadius * 2f),
                    BodyHeight + .1f, focusMaterial, ItemCategory.Food);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureCuttable(settings);
                definition.ConfigureInventory(5, true);
                definition.ConfigurePlacement(false);
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                return definition;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static AnimationCurve RadiusCurve()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, .05f),
                new Keyframe(.1f, .28f),
                new Keyframe(.3f, .62f),
                new Keyframe(.55f, .88f),
                new Keyframe(.72f, 1f),
                new Keyframe(.88f, .97f),
                new Keyframe(.96f, .72f),
                new Keyframe(1f, .28f));
            for (var index = 0; index < curve.length; index++)
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, index, AnimationUtility.TangentMode.ClampedAuto);
            return curve;
        }

        private static float RadiusScale(float normalized)
        {
            return RadiusCurve().Evaluate(Mathf.Clamp01(normalized));
        }

        private static Mesh CreateRootMesh(int segments, int rings, bool detailed)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (var ring = 0; ring <= rings; ring++)
            {
                var normalized = ring / (float)rings;
                var y = normalized * BodyHeight;
                var baseRadius = MaximumRadius * RadiusScale(normalized);
                for (var segment = 0; segment < segments; segment++)
                {
                    var angle = segment * Mathf.PI * 2f / segments;
                    var variation = detailed
                        ? 1f + Mathf.Sin(angle * 3f + ring * .71f) * .012f +
                          Mathf.Sin(angle * 7f - ring * .33f) * .006f
                        : 1f;
                    var radius = baseRadius * variation;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, y,
                        Mathf.Sin(angle) * radius));
                    uv.Add(new Vector2(segment / (float)segments, normalized));
                }
            }

            for (var ring = 0; ring < rings; ring++)
            {
                for (var segment = 0; segment < segments; segment++)
                {
                    var next = (segment + 1) % segments;
                    var lower = ring * segments + segment;
                    var lowerNext = ring * segments + next;
                    var upper = (ring + 1) * segments + segment;
                    var upperNext = (ring + 1) * segments + next;
                    triangles.Add(lower);
                    triangles.Add(upper);
                    triangles.Add(lowerNext);
                    triangles.Add(lowerNext);
                    triangles.Add(upper);
                    triangles.Add(upperNext);
                }
            }

            var bottomCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            uv.Add(new Vector2(.5f, .5f));
            var topCenter = vertices.Count;
            vertices.Add(Vector3.up * BodyHeight);
            uv.Add(new Vector2(.5f, .5f));
            var topStart = rings * segments;
            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                triangles.Add(bottomCenter);
                triangles.Add(segment);
                triangles.Add(next);
                triangles.Add(topCenter);
                triangles.Add(topStart + next);
                triangles.Add(topStart + segment);
            }

            var mesh = new Mesh
            {
                name = detailed ? "Carrot Root Visual" : "Carrot Root Collision",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = detailed ? 2 : 1;
            mesh.SetTriangles(triangles, 0, false);
            if (detailed) mesh.SetTriangles(Array.Empty<int>(), 1, false);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCrownMesh()
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            const int leafCount = 7;
            for (var leaf = 0; leaf < leafCount; leaf++)
            {
                var angle = leaf * Mathf.PI * 2f / leafCount;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var side = new Vector3(-radial.z, 0, radial.x);
                var lean = radial * (.012f + leaf % 3 * .003f);
                var centers = new[]
                {
                    Vector3.zero,
                    Vector3.up * .032f + lean * .25f,
                    Vector3.up * (.066f + leaf % 2 * .008f) + lean * .7f,
                    Vector3.up * (.098f + leaf % 3 * .006f) + lean
                };
                var widths = new[] { .003f, .0065f, .005f, .0012f };
                for (var point = 0; point < centers.Length - 1; point++)
                {
                    var lower = centers[point];
                    var upper = centers[point + 1];
                    var lowerRadius = widths[point];
                    var upperRadius = widths[point + 1];
                    const int sides = 5;
                    var start = vertices.Count;
                    for (var ring = 0; ring < 2; ring++)
                    {
                        var center = ring == 0 ? lower : upper;
                        var radius = ring == 0 ? lowerRadius : upperRadius;
                        for (var segment = 0; segment < sides; segment++)
                        {
                            var phase = segment * Mathf.PI * 2f / sides;
                            var offset = side * (Mathf.Cos(phase) * radius) +
                                         radial * (Mathf.Sin(phase) * radius);
                            vertices.Add(center + offset);
                            uv.Add(new Vector2(segment / (float)sides,
                                (point + ring) / 3f));
                        }
                    }
                    for (var segment = 0; segment < sides; segment++)
                    {
                        var next = (segment + 1) % sides;
                        triangles.Add(start + segment);
                        triangles.Add(start + sides + segment);
                        triangles.Add(start + next);
                        triangles.Add(start + next);
                        triangles.Add(start + sides + segment);
                        triangles.Add(start + sides + next);
                    }
                }
            }
            var mesh = new Mesh { name = "Carrot Crown" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material BuildLitMaterial(string name, Color color,
            float smoothness, bool doubleSided)
        {
            var path = MaterialRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cull", doubleSided ? 0 : 2);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildExteriorMaterial()
        {
            var path = MaterialRoot + "/CarrotExterior.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("MateSoul/Carrot Exterior");
            if (shader == null)
                throw new InvalidOperationException("Carrot exterior shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "CarrotExterior" };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", new Color(.94f, .24f, .025f, 1f));
            material.SetColor("_DarkColor", new Color(.56f, .11f, .012f, 1f));
            material.SetFloat("_Smoothness", .22f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildInteriorMaterial()
        {
            var path = MaterialRoot + "/CarrotInterior.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("MateSoul/Carrot Interior");
            if (shader == null)
                throw new InvalidOperationException("Carrot interior shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "CarrotInterior" };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureRoot + "/CarrotInterior_BaseColor.png"));
            material.SetTexture("_CoreMask", AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureRoot + "/CarrotInterior_CoreMask.png"));
            material.SetTexture("_CrossHeight", AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureRoot + "/CarrotInterior_CrossHeight.png"));
            material.SetTexture("_LongHeight", AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureRoot + "/CarrotInterior_LongHeight.png"));
            material.SetColor("_CoreTint", new Color(1.04f, .96f, .78f, 1f));
            material.SetFloat("_DetailStrength", .16f);
            material.SetFloat("_Smoothness", .38f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTextures()
        {
            ConfigureTexture("CarrotInterior_BaseColor.png", true,
                TextureWrapMode.Clamp);
            ConfigureTexture("CarrotInterior_CoreMask.png", false,
                TextureWrapMode.Clamp);
            ConfigureTexture("CarrotInterior_CrossHeight.png", false,
                TextureWrapMode.Clamp);
            ConfigureTexture("CarrotInterior_LongHeight.png", false,
                TextureWrapMode.Repeat);
        }

        private static void ConfigureTexture(string file, bool color,
            TextureWrapMode wrap)
        {
            var path = TextureRoot + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Carrot texture failed to import: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = color;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 512;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }
    }
}
