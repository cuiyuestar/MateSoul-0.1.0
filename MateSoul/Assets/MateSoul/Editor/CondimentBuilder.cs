using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class CondimentBuilder
    {
        public const string SourceRoot = "Assets/MateSoul/Content/PropsSource/Condiments";
        public const string PropsRoot = "Assets/MateSoul/Content/Props";
        private const string MaterialsRoot = PropsRoot + "/Materials/Condiments";

        [MenuItem("MateSoul/Content/Build Salt and Sauce Bottles")]
        public static void BuildAndRegister()
        {
            var items = Build(AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat"));
            var scene = EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            var bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true)).Single();
            var serialized = new SerializedObject(bootstrap);
            var catalogue = serialized.FindProperty("items");
            foreach (var item in items)
            {
                if (Enumerable.Range(0, catalogue.arraySize).Any(index =>
                        catalogue.GetArrayElementAtIndex(index).objectReferenceValue == item))
                    continue;
                catalogue.InsertArrayElementAtIndex(catalogue.arraySize);
                catalogue.GetArrayElementAtIndex(catalogue.arraySize - 1).objectReferenceValue = item;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("MATESOUL_CONDIMENT_BUILD_PASS items=3 scene=" + GameSceneBuilder.ScenePath);
        }

        public static ItemDefinition[] Build(Material focus)
        {
            if (focus == null) throw new InvalidOperationException("Item focus material is missing.");
            Directory.CreateDirectory(MaterialsRoot);
            ConfigureTextures();
            return new[]
            {
                BuildItem("SaltShaker", "salt-shaker", "盐罐", true, focus),
                BuildItem("BlackPepperSauce", "black-pepper-sauce", "黑椒酱", false, focus),
                BuildItem("SaladDressing", "salad-dressing", "沙拉酱", false, focus)
            };
        }

        private static ItemDefinition BuildItem(
            string name, string id, string title, bool salt, Material focus)
        {
            var path = SourceRoot + "/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Model not imported: " + path);
            importer.importAnimation = false;
            importer.importCameras = importer.importLights = false;
            importer.importBlendShapes = !salt;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = new GameObject(title);
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.transform.SetParent(root.transform, false);
                PrefabUtility.UnpackPrefabInstance(view, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();
                    renderer.shadowCastingMode = renderer.name.Contains("BottleBody") ||
                                                 renderer.name.Contains("Glass")
                        ? ShadowCastingMode.Off : ShadowCastingMode.On;
                }
                var outlet = view.GetComponentsInChildren<Transform>(true)
                    .Single(t => t.name == "Outlet" || t.name.StartsWith("Outlet.", StringComparison.Ordinal));
                // FBX empties retain Blender axes; authored outlet direction is root +Y.
                outlet.localRotation = Quaternion.Inverse(outlet.parent.rotation) * root.transform.rotation;
                var deforming = view.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh.GetBlendShapeIndex("Squeeze") >= 0).ToArray();
                foreach (var renderer in deforming)
                {
                    renderer.updateWhenOffscreen = false;
                    renderer.localBounds = renderer.sharedMesh.bounds;
                }
                ValidateBounds(view, salt);
                var shape = root.AddComponent<BoxCollider>();
                shape.center = Vector3.up * (salt ? .064f : .107f);
                shape.size = salt
                    ? new Vector3(.055f, .128f, .055f)
                    : new Vector3(.067f, .214f, .036f);
                if (!salt)
                {
                    var capShape = root.AddComponent<BoxCollider>();
                    capShape.center = new Vector3(0, .218f, 0);
                    capShape.size = new Vector3(.033f, .01f, .033f);
                    var nozzleShape = root.AddComponent<CapsuleCollider>();
                    nozzleShape.center = new Vector3(0, .230f, 0);
                    nozzleShape.radius = .003f;
                    nozzleShape.height = .013f;
                }
                var physics = new BodySettings(salt ? .18f : .28f,
                    salt ? PhysicsContentBuilder.Glass : PhysicsContentBuilder.Plastic, .035f, .08f);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var particles = CreateEffect(outlet, salt, name == "BlackPepperSauce");
                root.AddComponent<SeasoningDispenser>().Configure(salt
                        ? SeasoningDispenseMode.InvertedSalt : SeasoningDispenseMode.SqueezedSauce,
                    outlet, particles, deforming);
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PropsRoot + "/" + name + ".prefab");
                var definitionPath = PropsRoot + "/" + name + ".asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure(id, title, prefab, new[] { "visit", "seasoning-dispenser" },
                    true, salt ? Vector2.one * .058f : new Vector2(.07f, .04f),
                    salt ? .13f : .251f, focus, ItemCategory.Kitchenware);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(1);
                definition.ConfigurePlacement(false);
                definition.ConfigurePlacementParts();
                var gripPath = "Assets/MateSoul/Content/ItemActions/" + name + ".asset";
                var grip = AssetDatabase.LoadAssetAtPath<ItemInteractionProfile>(gripPath);
                if (grip == null)
                {
                    grip = ScriptableObject.CreateInstance<ItemInteractionProfile>();
                    AssetDatabase.CreateAsset(grip, gripPath);
                }
                grip.capability = "character-grip";
                grip.ConfigureGrip(ItemGripKind.Generic, CharacterHandPose.Power,
                    new Pose(new Vector3(0, salt ? .062f : .105f, salt ? -.027f : -.018f),
                        Quaternion.Euler(0, 0, 90)),
                    salt ? new Vector3(.055f, .055f, .064f) : new Vector3(.036f, .067f, .07f),
                    Vector3.zero, .002f, .65f);
                definition.ConfigureInteraction(grip);
                EditorUtility.SetDirty(grip);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                return definition;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void ValidateBounds(GameObject view, bool salt)
        {
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (Mathf.Abs(bounds.size.x - (salt ? .058f : .07f)) > .004f ||
                Mathf.Abs(bounds.size.y - (salt ? .130f : .250f)) > .014f ||
                Mathf.Abs(bounds.size.z - (salt ? .058f : .050f)) > .016f ||
                Mathf.Abs(bounds.min.y) > .002f)
                throw new InvalidOperationException("Condiment model has incorrect metre scale: " + bounds);
        }

        private static ParticleSystem CreateEffect(Transform outlet, bool salt, bool pepper)
        {
            var effect = new GameObject(salt ? "Fine Salt Stream" : "Sauce Jet");
            effect.transform.SetParent(outlet, false);
            // Particle shapes emit along local +Z, the bottle's outward +Y.
            effect.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var particles = effect.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            main.maxParticles = salt ? SeasoningDispenser.SaltParticleLimit :
                SeasoningDispenser.SauceParticleLimit;
            main.startLifetime = salt
                ? new ParticleSystem.MinMaxCurve(.38f, .65f)
                : new ParticleSystem.MinMaxCurve(.35f, .55f);
            main.startSpeed = salt ? .12f : .8f;
            main.startSize = salt
                ? new ParticleSystem.MinMaxCurve(.0007f, .0013f)
                : new ParticleSystem.MinMaxCurve(.0045f, .006f);
            main.startColor = salt
                ? new ParticleSystem.MinMaxGradient(new Color(.82f, .82f, .80f),
                    new Color(1f, 1f, .98f))
                : new ParticleSystem.MinMaxGradient(pepper
                    ? new Color(.27f, .12f, .036f) : new Color(.93f, .84f, .59f));
            main.gravityModifier = salt ? 1f : .75f;
            var emission = particles.emission;
            emission.rateOverTime = 0;
            var shape = particles.shape;
            shape.shapeType = salt ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
            shape.radius = salt ? .018f : .0013f;
            shape.radiusThickness = salt ? .04f : 1f;
            shape.angle = salt ? 0f : 2.5f;
            shape.randomDirectionAmount = salt ? .05f : .012f;
            // Built-in collision cache, bounded shapes; no messages or per-grain rigidbodies.
            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.enableDynamicColliders = true;
            collision.maxCollisionShapes = 16;
            collision.voxelSize = .025f;
            collision.collidesWith = InteractionPhysicsLayers.DirectSelectionMask;
            collision.sendCollisionMessages = false;
            collision.bounce = salt ? .05f : 0;
            collision.dampen = 1f;
            collision.lifetimeLoss = 1f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = salt ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = salt ? 1f : 2.2f;
            renderer.velocityScale = salt ? 0f : .003f;
            renderer.sharedMaterial = EffectMaterial(salt);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.minParticleSize = 0;
            renderer.maxParticleSize = .025f;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static void ConfigureTextures()
        {
            foreach (var path in Directory.GetFiles(SourceRoot, "*.png"))
            {
                var texture = AssetImporter.GetAtPath(path) as TextureImporter;
                texture.maxTextureSize = 512;
                texture.mipmapEnabled = true;
                texture.isReadable = false;
                var android = texture.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 512;
                android.format = TextureImporterFormat.ASTC_6x6;
                texture.SetPlatformTextureSettings(android);
                texture.SaveAndReimport();
            }
        }

        private static Material ConvertMaterial(Material original)
        {
            var name = original.name;
            var glass = name == "ClearGlass" || name == "SoftBottle";
            var material = LoadMaterial(name, glass ? "MateSoul/Condiment Glass" :
                "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", Color.white);
            var textureName = name == "SaltFill" ? "SaltGrain" : name;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "/" + textureName + ".png");
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (glass)
            {
                material.SetColor("_BaseColor", name == "ClearGlass"
                    ? new Color(.88f, .96f, .98f, .065f)
                    : new Color(.985f, .965f, .91f, .035f));
                material.SetFloat("_EdgeOpacity", name == "ClearGlass" ? .48f : .22f);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                material.SetFloat("_Metallic", name == "BrushedSteel" ? .88f : 0f);
                material.SetFloat("_Smoothness", name == "BrushedSteel" ? .68f :
                    name == "SaltFill" ? .12f :
                    name == "PepperSauce" || name == "SaladSauce" ? .28f : .55f);
                if (name == "PepperSauce" || name == "SaladSauce")
                {
                    // Plastic shell supplies the highlight; keep the sauce's dark/butter colour.
                    material.SetFloat("_SpecularHighlights", 0f);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                if (name == "OutletInterior") material.SetColor("_BaseColor", new Color(.014f, .012f, .01f));
                if (name == "WhiteCap") material.SetColor("_BaseColor", new Color(.93f, .945f, .94f));
                material.enableInstancing = true;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EffectMaterial(bool salt)
        {
            var material = LoadMaterial(salt ? "SaltParticles" : "SauceParticles",
                salt ? "Universal Render Pipeline/Particles/Unlit" : "MateSoul/Sauce Particle");
            material.SetColor("_BaseColor", Color.white);
            if (!salt)
            {
                material.renderQueue = (int)RenderQueue.AlphaTest;
                EditorUtility.SetDirty(material);
                return material;
            }
            // Opaque quads stay small; no large blended fog or stereo depth ambiguity.
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadMaterial(string name, string shaderName)
        {
            var path = MaterialsRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Shader missing: " + shaderName);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }
    }
}
