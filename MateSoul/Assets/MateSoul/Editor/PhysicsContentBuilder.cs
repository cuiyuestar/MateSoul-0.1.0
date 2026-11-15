using System.IO;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    public static class PhysicsContentBuilder
    {
        private const string DirectoryPath = "Assets/MateSoul/Content/Physics";
        public static PhysicMaterial Wood => Surface("Wood", .55f, .4f, .05f);
        public static PhysicMaterial Metal => Surface("Metal", .5f, .35f, .08f);
        public static PhysicMaterial Ceramic => Surface("Ceramic", .48f, .32f, .04f);
        public static PhysicMaterial Glass => Surface("Glass", .34f, .22f, .03f);
        public static PhysicMaterial Plastic => Surface("Plastic", .42f, .3f, .06f);
        public static PhysicMaterial Paper => Surface("Paper", .62f, .48f, .02f);
        public static PhysicMaterial Food => Surface("Food", .58f, .44f, .03f);
        public static PhysicMaterial Fabric => Surface("Fabric", .72f, .58f, .01f);
        public static PhysicMaterial Rubber => Surface("Rubber", .8f, .65f, .1f);
        public static PhysicMaterial Room => Surface("Room", .6f, .45f, .02f);
        public static PhysicMaterial BowlingBall => Surface("BowlingBall", .18f, .08f, .08f);
        public static PhysicMaterial BowlingPin => Surface("BowlingPin", .35f, .25f, .12f);

        private static PhysicMaterial Surface(string name, float friction, float sliding, float bounce)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = DirectoryPath + "/" + name + ".physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
            if (material == null)
            {
                material = new PhysicMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.staticFriction = friction;
            material.dynamicFriction = sliding;
            material.bounciness = bounce;
            material.frictionCombine = PhysicMaterialCombine.Average;
            material.bounceCombine = PhysicMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void ConfigureWorld()
        {
            var dynamics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/DynamicsManager.asset")[0]);
            dynamics.FindProperty("m_DefaultMaterial").objectReferenceValue = Room;
            dynamics.ApplyModifiedPropertiesWithoutUndo();
            var time = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TimeManager.asset")[0]);
            time.FindProperty("Fixed Timestep").floatValue = .02f;
            time.FindProperty("Maximum Allowed Timestep").floatValue = .06f;
            time.ApplyModifiedPropertiesWithoutUndo();
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(
                new[] { time.targetObject }, "ProjectSettings/TimeManager.asset", true);
        }

        public static void ConfigureBody(GameObject root, BodySettings settings)
        {
            var body = root.GetComponent<PhysicsBody>() ?? root.AddComponent<PhysicsBody>();
            body.Configure(settings);
            EditorUtility.SetDirty(body);
        }

        public static void ConfigureCharacter(GameObject root)
        {
            var old = root.GetComponent<CharacterController>();
            if (old != null) Object.DestroyImmediate(old);
            var shape = root.GetComponent<CapsuleCollider>() ?? root.AddComponent<CapsuleCollider>();
            shape.radius = RoomNavigation.RuntimeRoomNavigation.CharacterRadius;
            shape.height = RoomNavigation.RuntimeRoomNavigation.CharacterHeight -
                AvatarMotor.StandingSoleLift;
            shape.center = Vector3.up *
                (shape.height * .5f + AvatarMotor.StandingSoleLift);
            Debug.Log("MATESOUL_CHARACTER_COLLIDER height=" +
                shape.height.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " centerY=" + shape.center.y.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " radius=" + shape.radius.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " soleLift=" + AvatarMotor.StandingSoleLift.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root=" + root.name);
            ConfigureBody(root, new BodySettings(45f, Rubber, 0, .05f));
            root.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeRotation;
        }

        public static void Box(GameObject root, string name, Vector3 center, Vector3 size)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = center;
            child.AddComponent<BoxCollider>().size = size;
        }

        public static void FurnitureShapes(GameObject root, bool chair, Vector3 size)
        {
            var top = chair ? .4f : size.y;
            Box(root, chair ? "Seat Collider" : "Top Collider",
                new Vector3(0, top - .025f, 0), new Vector3(size.x, .05f, size.z));
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2)
                    Box(root, $"Leg Collider {x} {z}",
                        new Vector3(x * (size.x * .5f - .035f), (top - .05f) * .5f,
                            z * (size.z * .5f - .035f)),
                        new Vector3(.05f, top - .05f, .05f));
            if (chair)
                Box(root, "Back Collider", new Vector3(0, (top + size.y) * .5f, size.z * .5f - .025f),
                    new Vector3(size.x, size.y - top, .05f));
        }
    }
}
