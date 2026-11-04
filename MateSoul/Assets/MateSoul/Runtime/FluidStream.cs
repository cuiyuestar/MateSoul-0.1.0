using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Runtime
{
    // Fixed-capacity ballistic liquid parcels. No per-drop GameObjects, rigidbodies,
    // particle neighbour solver or camera-sized fluid render targets.
    [DisallowMultipleComponent]
    public sealed class FluidStream : MonoBehaviour
    {
        public const int Capacity = 128;
        private struct Parcel
        {
            public Vector3 Position, Velocity, Normal;
            public float Liters, Temperature, Age, Radius;
            public Color Color;
            public bool Active, Settled;
        }
        private readonly Parcel[] parcels = new Parcel[Capacity];
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Vector3[] vertices = new Vector3[Capacity * 12];
        private readonly Color[] colors = new Color[Capacity * 12];
        private readonly int[] indices = new int[Capacity * 36];
        private Mesh mesh;
        private Transform visual;
        private Renderer fluidRenderer;
        private PhysicsBody owner;
        private WaterReceiver source;
        private Material material;
        private double simulationTime;
        public float EmittedLiters { get; private set; }
        public float ReceivedLiters { get; private set; }
        public float SpilledLiters { get; private set; }
        public int ActiveCount { get; private set; }
        public int SettledCount { get; private set; }

        public void Configure(Material reference)
        {
            if (mesh != null) return;
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            owner = GetComponent<PhysicsBody>();
            source = GetComponent<WaterReceiver>();
            var shader = Resources.Load<Shader>("MateSoulWater/Fluid");
            if (shader == null) throw new InvalidOperationException("Fluid shader is missing.");
            material = new Material(shader) { name = "Physical water parcels" };
            var root = new GameObject("Physical Water Parcels");
            root.transform.SetParent(transform, false);
            visual = root.transform;
            fluidRenderer = root.AddComponent<MeshRenderer>();
            fluidRenderer.sharedMaterial = material;
            fluidRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fluidRenderer.receiveShadows = false;
            mesh = new Mesh { name = "Pooled fluid mesh" };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            for (var parcel = 0; parcel < Capacity; parcel++)
                for (var side = 0; side < 6; side++)
                {
                    var start = parcel * 12;
                    var at = parcel * 36 + side * 6;
                    var next = (side + 1) % 6;
                    indices[at] = start + side; indices[at + 1] = start + next;
                    indices[at + 2] = start + side + 6;
                    indices[at + 3] = start + next; indices[at + 4] = start + next + 6;
                    indices[at + 5] = start + side + 6;
                }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = indices;
            fluidRenderer.enabled = false;
        }

        public float Emit(Vector3 position, Vector3 velocity, float liters,
            float temperature, Color color)
        {
            if (mesh == null || !PhysicsBody.IsFinite(position) ||
                !PhysicsBody.IsFinite(velocity) || !PhysicsBody.IsFinite(liters) || liters <= 0 ||
                !PhysicsBody.IsFinite(temperature)) return 0;
            for (var i = 0; i < Capacity; i++)
            {
                if (parcels[i].Active) continue;
                parcels[i] = new Parcel { Position = position, Velocity = velocity,
                    Liters = liters, Temperature = temperature, Color = color,
                    Radius = .006f, Active = true };
                EmittedLiters += liters;
                ActiveCount++;
                return liters;
            }
            return 0;
        }

        private void Update()
        {
            if (owner != null && owner.IsPaused) return;
            Advance(Time.deltaTime);
        }

        public void Advance(float delta)
        {
            if (!PhysicsBody.IsFinite(delta) || delta <= 0 || mesh == null || ActiveCount == 0) return;
            simulationTime += Mathf.Min(delta, 2f);
            if (simulationTime + .000001 < .02) return;
            while (simulationTime + .000001 >= .02)
            {
                const float step = .02f;
                for (var i = 0; i < Capacity; i++)
                {
                    var parcel = parcels[i];
                    if (!parcel.Active) continue;
                    parcel.Age += step;
                    if (parcel.Settled)
                    {
                        if (parcel.Age >= .9f) parcel.Active = false;
                        parcels[i] = parcel;
                        continue;
                    }
                    var from = parcel.Position;
                    parcel.Velocity += Physics.gravity * step;
                    var to = from + parcel.Velocity * step;
                    var displacement = to - from;
                    var distance = displacement.magnitude;
                    var nearest = distance + .001f;
                    RaycastHit impact = default;
                    var hitCount = distance > .00001f ? Physics.SphereCastNonAlloc(from,
                        parcel.Radius, displacement / distance, hits, distance,
                        InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore) : 0;
                    for (var h = 0; h < hitCount; h++)
                    {
                        var hit = hits[h];
                        if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                        var vessel = hit.collider.GetComponentInParent<WaterReceiver>();
                        // Convex vessel colliders seal the visual cavity. Permit
                        // descent through the authored opening, then receive only
                        // when the trajectory actually crosses its mouth plane.
                        if (vessel != null && vessel != source &&
                            vessel.CanReceiveAt(hit.point) &&
                            Vector3.Dot(displacement, vessel.transform.up) < 0 &&
                            Vector3.Dot(from - vessel.transform.TransformPoint(vessel.OpeningCenter),
                                vessel.transform.up) >= -parcel.Radius)
                            continue;
                        nearest = hit.distance;
                        impact = hit;
                    }
                    var receiver = WaterReceiver.FindCrossedOpening(from, to,
                        source, out var entry, out var fraction);
                    if (receiver != null && fraction * distance <= nearest + parcel.Radius)
                    {
                        var accepted = receiver.Receive(parcel.Liters, parcel.Temperature, entry);
                        ReceivedLiters += accepted;
                        parcel.Liters -= accepted;
                        if (parcel.Liters <= .000001f) parcel.Active = false;
                        else Settle(ref parcel, entry, Vector3.up);
                    }
                    else if (impact.collider != null)
                    {
                        // A hit on a vessel wall does not deliver liquid through it.
                        // Only crossing its open mouth can add contained volume.
                        Settle(ref parcel, impact.point, impact.normal);
                    }
                    else
                    {
                        parcel.Position = to;
                        if (parcel.Age + .00001f >= 2f) { SpilledLiters += parcel.Liters; parcel.Active = false; }
                    }
                    parcels[i] = parcel;
                }
                simulationTime = Math.Max(0, simulationTime - .02);
            }
            Draw();
            if (ActiveCount == 0) simulationTime = 0;
        }

        private void Settle(ref Parcel parcel, Vector3 point, Vector3 normal)
        {
            SpilledLiters += parcel.Liters;
            parcel.Position = point + normal * .002f;
            parcel.Normal = normal.sqrMagnitude > .1f ? normal.normalized : Vector3.up;
            parcel.Velocity = Vector3.zero;
            parcel.Settled = true;
            parcel.Age = 0;
        }

        private void Draw()
        {
            ActiveCount = 0;
            SettledCount = 0;
            for (var i = 0; i < Capacity; i++)
            {
                var parcel = parcels[i];
                if (parcel.Active) ActiveCount++;
                if (parcel.Active && parcel.Settled) SettledCount++;
                var axis = parcel.Settled ? parcel.Normal : parcel.Velocity.normalized;
                if (axis.sqrMagnitude < .1f) axis = Vector3.down;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
                var other = Vector3.Cross(axis, side);
                var radius = parcel.Settled ? .018f + parcel.Age * .018f : parcel.Radius;
                var length = parcel.Settled ? .0007f : Mathf.Clamp(parcel.Velocity.magnitude * .022f, .008f, .06f);
                for (var v = 0; v < 12; v++)
                {
                    var angle = (v % 6) * Mathf.PI / 3f;
                    var point = parcel.Position + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius +
                                axis * ((v < 6 ? -.5f : .5f) * length);
                    vertices[i * 12 + v] = visual.InverseTransformPoint(parcel.Active ? point : parcel.Position);
                    colors[i * 12 + v] = parcel.Active ? parcel.Color : Color.clear;
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            fluidRenderer.enabled = ActiveCount > 0;
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
