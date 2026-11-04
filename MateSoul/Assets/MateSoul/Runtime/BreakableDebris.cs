using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class BreakableDebris : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private PhysicsBody[] bodies = Array.Empty<PhysicsBody>();
        private GameSession session;
        private float remainingLifetime;
        private bool paused;

        public int FragmentCount => bodies.Length;
        public IReadOnlyList<PhysicsBody> Bodies => bodies;
        public AudioSource BreakSoundSource { get; private set; }
        public double BreakSoundRequestedAt { get; private set; } = -1;
        public double FragmentsReadyAt { get; private set; } = -1;

        internal static BreakableDebris Create(GameSession owner, Transform parent, string itemName,
            Pose pose, Vector2 footprint, float height, BodySettings physics,
            BreakableSettings settings, BreakSeverity severity, Material[] materials,
            Vector3 impactPoint, Vector3 inheritedVelocity, Vector3 inheritedAngularVelocity)
        {
            if (owner == null || parent == null || materials == null || materials.Length == 0)
                throw new ArgumentException("Breakable debris requires an owner, parent, and material.");
            settings.Validate();
            physics.Validate();

            var count = severity == BreakSeverity.Heavy
                ? settings.heavyFragmentCount
                : settings.lightFragmentCount;
            var root = new GameObject(itemName + " " +
                                      (severity == BreakSeverity.Heavy ? "重度碎片" : "轻度碎片"));
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var debris = root.AddComponent<BreakableDebris>();
            debris.session = owner;
            debris.remainingLifetime = settings.debrisLifetime;
            debris.PlayBreakSound(settings.breakSound, severity);
            try
            {
                var template = severity == BreakSeverity.Heavy
                    ? settings.heavyDebrisPrefab
                    : settings.lightDebrisPrefab;
                debris.bodies = template != null
                    ? CreateAuthoredFragments(debris, template, count, pose, physics,
                        severity, impactPoint, inheritedVelocity, inheritedAngularVelocity)
                    : CreateProceduralFragments(debris, count, pose, footprint, height,
                        physics, severity, materials, impactPoint, inheritedVelocity,
                        inheritedAngularVelocity);
                debris.FragmentsReadyAt = Time.realtimeSinceStartupAsDouble;
                Debug.Log($"BREAK_SOUND_TIMING item={itemName} severity={severity} " +
                          $"leadMs={(debris.FragmentsReadyAt - debris.BreakSoundRequestedAt) * 1000:F1}");
                owner.RegisterDebris(debris);
#region debug-point C:item-break
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                var payload =
                    $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                    $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"BreakableDebris.Create\",\"msg\":\"[DEBUG] item debris created\"," +
                    $"\"data\":{{\"item\":\"{itemName}\",\"severity\":\"{severity}\"," +
                    $"\"fragments\":{debris.bodies.Length},\"authored\":" +
                    $"{(template != null).ToString().ToLowerInvariant()}}}}}";
                var request = UnityWebRequest.Post(
                    "http://127.0.0.1:7791/event", payload, "application/json");
                var operation = request.SendWebRequest();
                operation.completed += _ => request.Dispose();
#endif
#endregion
                return debris;
            }
            catch
            {
                Destroy(root);
                throw;
            }
        }

        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            foreach (var body in bodies)
                if (body != null) body.SetPaused(value);
            if (BreakSoundSource == null) return;
            if (value) BreakSoundSource.Pause();
            else BreakSoundSource.UnPause();
        }

        internal void PlayBreakSound(AudioClip clip, BreakSeverity severity)
        {
            if (clip == null || BreakSoundSource != null) return;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = .2f;
            source.spread = 25f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = .35f;
            source.maxDistance = 7f;
            source.volume = severity == BreakSeverity.Heavy ? .95f : .72f;
            source.pitch = severity == BreakSeverity.Heavy ? .94f : 1.04f;
            BreakSoundSource = source;
            BreakSoundRequestedAt = Time.realtimeSinceStartupAsDouble;
            source.Play();
        }

        private void Update()
        {
            if (paused) return;
            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            session?.UnregisterDebris(this);
            foreach (var mesh in meshes)
                if (mesh != null) Destroy(mesh);
        }

        private static PhysicsBody[] CreateAuthoredFragments(BreakableDebris debris,
            GameObject template, int expectedCount, Pose itemPose, BodySettings physics,
            BreakSeverity severity, Vector3 impactPoint, Vector3 inheritedVelocity,
            Vector3 inheritedAngularVelocity)
        {
            var instance = Instantiate(template, debris.transform, false);
            instance.name = "Authored Fragments";
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            Array.Sort(filters, (left, right) =>
                string.CompareOrdinal(left.transform.name, right.transform.name));
            if (filters.Length != expectedCount)
                throw new InvalidOperationException(
                    $"{template.name} contains {filters.Length} fragments; expected {expectedCount}.");
            var totalWeight = 0f;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
                    throw new InvalidOperationException(
                        $"{template.name} contains a fragment without a mesh or renderer.");
                totalWeight += Mathf.Max(1, filter.sharedMesh.vertexCount);
            }

            var bodies = new PhysicsBody[filters.Length];
            for (var index = 0; index < filters.Length; index++)
            {
                var filter = filters[index];
                var shard = filter.gameObject;
                var collider = shard.GetComponent<MeshCollider>();
                if (collider == null) collider = shard.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
                var weight = Mathf.Max(1, filter.sharedMesh.vertexCount) / totalWeight;
                var body = shard.GetComponent<PhysicsBody>();
                if (body == null) body = shard.AddComponent<PhysicsBody>();
                body.Configure(new BodySettings(Mathf.Max(.001f, physics.mass * weight),
                    physics.material, physics.linearDrag, physics.angularDrag));
                body.Place(new Pose(shard.transform.position, shard.transform.rotation));
                Launch(body, shard.transform.TransformPoint(filter.sharedMesh.bounds.center),
                    itemPose.rotation, severity, index, filters.Length, impactPoint,
                    inheritedVelocity, inheritedAngularVelocity);
                bodies[index] = body;
            }
            return bodies;
        }

        private static PhysicsBody[] CreateProceduralFragments(BreakableDebris debris,
            int count, Pose pose, Vector2 footprint, float height, BodySettings physics,
            BreakSeverity severity, Material[] materials, Vector3 impactPoint,
            Vector3 inheritedVelocity, Vector3 inheritedAngularVelocity)
        {
            var bodies = new PhysicsBody[count];
            var radiusX = Mathf.Max(.006f, footprint.x * .5f);
            var radiusZ = Mathf.Max(.006f, footprint.y * .5f);
            var fragmentHeight = Mathf.Max(.006f, height);
            var fragmentMass = Mathf.Max(.001f, physics.mass / count);
            for (var index = 0; index < count; index++)
            {
                var mesh = CreateWedge(index, count, radiusX, radiusZ, fragmentHeight);
                debris.meshes.Add(mesh);
                var shard = new GameObject($"Fragment {index + 1:00}",
                    typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                shard.transform.SetParent(debris.transform, false);
                shard.GetComponent<MeshFilter>().sharedMesh = mesh;
                shard.GetComponent<MeshRenderer>().sharedMaterial =
                    materials[index % materials.Length];
                var collider = shard.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;

                var body = shard.AddComponent<PhysicsBody>();
                body.Configure(new BodySettings(fragmentMass, physics.material,
                    physics.linearDrag, physics.angularDrag));
                body.Place(pose);
                Launch(body, pose.position + pose.rotation * mesh.bounds.center,
                    pose.rotation, severity, index, count, impactPoint,
                    inheritedVelocity, inheritedAngularVelocity);
                bodies[index] = body;
            }
            return bodies;
        }

        private static void Launch(PhysicsBody body, Vector3 worldCenter,
            Quaternion itemRotation, BreakSeverity severity, int index, int count,
            Vector3 impactPoint, Vector3 inheritedVelocity, Vector3 inheritedAngularVelocity)
        {
            var direction = worldCenter - impactPoint;
            if (direction.sqrMagnitude < .000001f)
            {
                var angle = (index + .5f) * Mathf.PI * 2f / count;
                direction = itemRotation *
                            new Vector3(Mathf.Cos(angle), .3f, Mathf.Sin(angle));
            }
            direction = (direction.normalized + Vector3.up * .25f).normalized;
            var spreadSpeed = severity == BreakSeverity.Heavy ? .55f : .3f;
            body.AddForce(inheritedVelocity + direction * spreadSpeed,
                ForceMode.VelocityChange);
            body.AddTorque(inheritedAngularVelocity +
                           itemRotation * new Vector3(
                               (index % 3 - 1) * 1.2f,
                               (index % 2 == 0 ? 1 : -1) * 1.8f,
                               ((index + 1) % 3 - 1) * 1.2f),
                ForceMode.VelocityChange);
        }

        private static Mesh CreateWedge(int index, int count, float radiusX, float radiusZ,
            float height)
        {
            var span = Mathf.PI * 2f / count;
            var margin = span * .025f;
            var first = index * span + margin;
            var second = (index + 1) * span - margin;
            var p0 = new Vector3(Mathf.Cos(first) * radiusX * .98f, 0,
                Mathf.Sin(first) * radiusZ * .98f);
            var p1 = new Vector3(Mathf.Cos(second) * radiusX * .98f, 0,
                Mathf.Sin(second) * radiusZ * .98f);
            var vertices = new[]
            {
                Vector3.zero, p0, p1,
                Vector3.up * height, p0 + Vector3.up * height, p1 + Vector3.up * height
            };
            var mesh = new Mesh
            {
                name = $"Breakable Wedge {index + 1:00}",
                vertices = vertices,
                triangles = new[]
                {
                    0, 2, 1,
                    3, 4, 5,
                    0, 1, 4, 0, 4, 3,
                    1, 2, 5, 1, 5, 4,
                    2, 0, 3, 2, 3, 5
                },
                uv = new[]
                {
                    new Vector2(.5f, .5f),
                    new Vector2(p0.x / (radiusX * 2f) + .5f, p0.z / (radiusZ * 2f) + .5f),
                    new Vector2(p1.x / (radiusX * 2f) + .5f, p1.z / (radiusZ * 2f) + .5f),
                    new Vector2(.5f, .5f),
                    new Vector2(p0.x / (radiusX * 2f) + .5f, p0.z / (radiusZ * 2f) + .5f),
                    new Vector2(p1.x / (radiusX * 2f) + .5f, p1.z / (radiusZ * 2f) + .5f)
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
