using System;
using System.Linq;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RuntimeCutPiece : IDisposable
    {
        public Mesh VisualMesh { get; private set; }
        public Mesh CollisionMesh { get; private set; }
        public CutStateSaveData State { get; }
        public bool PositiveSide { get; }
        public float VolumeRatio { get; }

        internal RuntimeCutPiece(Mesh visualMesh, Mesh collisionMesh,
            CutStateSaveData state, bool positiveSide, float volumeRatio)
        {
            VisualMesh = visualMesh;
            CollisionMesh = collisionMesh;
            State = state;
            PositiveSide = positiveSide;
            VolumeRatio = volumeRatio;
        }

        internal void TakeMeshes(out Mesh visual, out Mesh collision)
        {
            visual = VisualMesh;
            collision = CollisionMesh;
            VisualMesh = null;
            CollisionMesh = null;
        }

        public void Dispose()
        {
            RuntimeCuttable.DestroyRuntimeMesh(VisualMesh);
            RuntimeCuttable.DestroyRuntimeMesh(CollisionMesh);
            VisualMesh = null;
            CollisionMesh = null;
        }
    }

    public sealed class RuntimeCutPair : IDisposable
    {
        public RuntimeCutPiece Positive { get; }
        public RuntimeCutPiece Negative { get; }

        internal RuntimeCutPair(RuntimeCutPiece positive, RuntimeCutPiece negative)
        {
            Positive = positive;
            Negative = negative;
        }

        public void Dispose()
        {
            Positive?.Dispose();
            Negative?.Dispose();
        }
    }

    [DisallowMultipleComponent]
    public sealed class RuntimeCuttable : MonoBehaviour
    {
        [SerializeField] private MeshFilter surfaceFilter;
        [SerializeField] private MeshRenderer surfaceRenderer;
        [SerializeField] private MeshCollider collisionShape;
        [SerializeField] private Transform crownRoot;
        [SerializeField] private Mesh originalSurface;
        [SerializeField] private CuttableSettings settings;

        private CutStateSaveData state = new CutStateSaveData();
        private Mesh ownedSurface;
        private Mesh ownedCollision;
        private float baseCollisionVolume;

        public int CutDepth => state?.planes?.Length ?? 0;
        public float VolumeRatio { get; private set; } = 1f;
        public Mesh SurfaceMesh => surfaceFilter != null ? surfaceFilter.sharedMesh : null;
        public Mesh CollisionMesh => collisionShape != null ? collisionShape.sharedMesh : null;
        public CuttableSettings Settings => settings;

        public void Configure(MeshFilter visual, MeshRenderer renderer,
            MeshCollider physicsShape, Transform crown, CuttableSettings value)
        {
            value.Validate();
            if (visual == null || renderer == null || physicsShape == null ||
                visual.sharedMesh == null || physicsShape.sharedMesh == null)
                throw new ArgumentException(
                    "A cuttable item requires visual and convex collision meshes.");
            surfaceFilter = visual;
            surfaceRenderer = renderer;
            collisionShape = physicsShape;
            crownRoot = crown;
            originalSurface = visual.sharedMesh;
            settings = value;
            baseCollisionVolume = MeshPlaneSlicer.CalculateVolume(value.collisionProxy);
            if (baseCollisionVolume <= .000000001f)
                throw new ArgumentException("Cut collision proxy must enclose a positive volume.");
            EnsureMaterials();
        }

        public void Bind(CuttableSettings value)
        {
            value.Validate();
            settings = value;
            if (surfaceFilter == null || surfaceRenderer == null || collisionShape == null ||
                originalSurface == null)
                throw new InvalidOperationException(
                    $"{name}: cuttable prefab references are incomplete.");
            if (!collisionShape.convex)
                throw new InvalidOperationException(
                    $"{name}: runtime cut collision mesh must be convex.");
            baseCollisionVolume = MeshPlaneSlicer.CalculateVolume(settings.collisionProxy);
            if (baseCollisionVolume <= .000000001f)
                throw new InvalidOperationException(
                    $"{name}: cut collision proxy has no enclosed volume.");
            EnsureMaterials();
            RefreshCrown();
        }

        private void EnsureMaterials()
        {
            if (surfaceRenderer == null || settings.interiorMaterial == null) return;
            var exterior = surfaceRenderer.sharedMaterials.FirstOrDefault();
            surfaceRenderer.sharedMaterials = new[] { exterior, settings.interiorMaterial };
        }

        public RuntimeCutPair CreateCut(Plane localPlane)
        {
            settings.Validate();
            if (CutDepth >= settings.maximumCutDepth) return null;
            var visualResult = MeshPlaneSlicer.Slice(
                SurfaceMesh, localPlane, settings, name + " Visual");
            if (visualResult == null) return null;
            var collisionResult = MeshPlaneSlicer.Slice(
                CollisionMesh, localPlane, settings, name + " Collision");
            if (collisionResult == null)
            {
                DestroyRuntimeMesh(visualResult.Positive.Mesh);
                DestroyRuntimeMesh(visualResult.Negative.Mesh);
                return null;
            }
            if (TriangleCount(collisionResult.Positive.Mesh) > 255 ||
                TriangleCount(collisionResult.Negative.Mesh) > 255)
            {
                DestroyRuntimeMesh(visualResult.Positive.Mesh);
                DestroyRuntimeMesh(visualResult.Negative.Mesh);
                DestroyRuntimeMesh(collisionResult.Positive.Mesh);
                DestroyRuntimeMesh(collisionResult.Negative.Mesh);
                return null;
            }

            var positiveRatio = collisionResult.Positive.Volume / baseCollisionVolume;
            var negativeRatio = collisionResult.Negative.Volume / baseCollisionVolume;
            if (positiveRatio < settings.minimumPieceVolumeRatio ||
                negativeRatio < settings.minimumPieceVolumeRatio)
            {
                DestroyRuntimeMesh(visualResult.Positive.Mesh);
                DestroyRuntimeMesh(visualResult.Negative.Mesh);
                DestroyRuntimeMesh(collisionResult.Positive.Mesh);
                DestroyRuntimeMesh(collisionResult.Negative.Mesh);
                return null;
            }

            var positiveState = AppendState(localPlane, true);
            var negativeState = AppendState(localPlane, false);
            return new RuntimeCutPair(
                new RuntimeCutPiece(visualResult.Positive.Mesh,
                    collisionResult.Positive.Mesh, positiveState, true, positiveRatio),
                new RuntimeCutPiece(visualResult.Negative.Mesh,
                    collisionResult.Negative.Mesh, negativeState, false, negativeRatio));
        }

        public void ApplyPiece(RuntimeCutPiece piece)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            ReleaseOwnedMeshes();
            piece.TakeMeshes(out ownedSurface, out ownedCollision);
            if (ownedSurface == null || ownedCollision == null)
                throw new InvalidOperationException("Cut piece meshes were already consumed.");
            surfaceFilter.sharedMesh = ownedSurface;
            Physics.BakeMesh(ownedCollision.GetInstanceID(), true);
            collisionShape.sharedMesh = null;
            collisionShape.sharedMesh = ownedCollision;
            collisionShape.convex = true;
            state = CloneState(piece.State);
            VolumeRatio = piece.VolumeRatio;
            EnsureMaterials();
            RefreshCrown();
        }

        public void Restore(CutStateSaveData savedState)
        {
            settings.Validate();
            baseCollisionVolume = MeshPlaneSlicer.CalculateVolume(settings.collisionProxy);
            if (baseCollisionVolume <= .000000001f)
                throw new InvalidOperationException(
                    $"{name}: cut collision proxy has no enclosed volume.");
            if (savedState == null || savedState.planes == null ||
                savedState.planes.Length == 0)
            {
                state = new CutStateSaveData();
                VolumeRatio = 1f;
                RefreshCrown();
                return;
            }
            if (savedState.version != 1 ||
                savedState.planes.Length > settings.maximumCutDepth)
                throw new InvalidOperationException("Saved cut state is unsupported.");

            Mesh visual = originalSurface;
            Mesh collision = settings.collisionProxy;
            var visualOwned = false;
            var collisionOwned = false;
            try
            {
                foreach (var operation in savedState.planes)
                {
                    ValidateOperation(operation);
                    var plane = new Plane(
                        new Vector3(operation.nx, operation.ny, operation.nz),
                        operation.distance);
                    var visualResult = MeshPlaneSlicer.Slice(
                        visual, plane, settings, name + " Restored Visual");
                    var collisionResult = MeshPlaneSlicer.Slice(
                        collision, plane, settings, name + " Restored Collision");
                    if (visualResult == null || collisionResult == null)
                    {
                        if (visualResult != null)
                        {
                            DestroyRuntimeMesh(visualResult.Positive.Mesh);
                            DestroyRuntimeMesh(visualResult.Negative.Mesh);
                        }
                        if (collisionResult != null)
                        {
                            DestroyRuntimeMesh(collisionResult.Positive.Mesh);
                            DestroyRuntimeMesh(collisionResult.Negative.Mesh);
                        }
                        throw new InvalidOperationException(
                            "Saved cut history no longer intersects its source mesh.");
                    }
                    var nextVisual = operation.positiveSide
                        ? visualResult.Positive.Mesh
                        : visualResult.Negative.Mesh;
                    var discardedVisual = operation.positiveSide
                        ? visualResult.Negative.Mesh
                        : visualResult.Positive.Mesh;
                    var nextCollision = operation.positiveSide
                        ? collisionResult.Positive.Mesh
                        : collisionResult.Negative.Mesh;
                    var discardedCollision = operation.positiveSide
                        ? collisionResult.Negative.Mesh
                        : collisionResult.Positive.Mesh;
                    DestroyRuntimeMesh(discardedVisual);
                    DestroyRuntimeMesh(discardedCollision);
                    if (visualOwned) DestroyRuntimeMesh(visual);
                    if (collisionOwned) DestroyRuntimeMesh(collision);
                    visual = nextVisual;
                    collision = nextCollision;
                    visualOwned = true;
                    collisionOwned = true;
                }

                ReleaseOwnedMeshes();
                ownedSurface = visual;
                ownedCollision = collision;
                visualOwned = false;
                collisionOwned = false;
                surfaceFilter.sharedMesh = ownedSurface;
                Physics.BakeMesh(ownedCollision.GetInstanceID(), true);
                collisionShape.sharedMesh = null;
                collisionShape.sharedMesh = ownedCollision;
                collisionShape.convex = true;
                state = CloneState(savedState);
                VolumeRatio = MeshPlaneSlicer.CalculateVolume(ownedCollision) /
                              baseCollisionVolume;
                EnsureMaterials();
                RefreshCrown();
            }
            finally
            {
                if (visualOwned) DestroyRuntimeMesh(visual);
                if (collisionOwned) DestroyRuntimeMesh(collision);
            }
        }

        public CutStateSaveData CaptureState()
            => CutDepth == 0 ? null : CloneState(state);

        private CutStateSaveData AppendState(Plane plane, bool positive)
        {
            var current = state?.planes ?? Array.Empty<CutPlaneSaveData>();
            var operations = new CutPlaneSaveData[current.Length + 1];
            for (var index = 0; index < current.Length; index++)
                operations[index] = CloneOperation(current[index]);
            operations[current.Length] = new CutPlaneSaveData
            {
                nx = plane.normal.x,
                ny = plane.normal.y,
                nz = plane.normal.z,
                distance = plane.distance,
                positiveSide = positive
            };
            return new CutStateSaveData { version = 1, planes = operations };
        }

        private void RefreshCrown()
        {
            if (crownRoot == null) return;
            var visible = true;
            var point = crownRoot.localPosition;
            foreach (var operation in state?.planes ?? Array.Empty<CutPlaneSaveData>())
            {
                var distance = Vector3.Dot(
                    new Vector3(operation.nx, operation.ny, operation.nz), point) +
                               operation.distance;
                if (operation.positiveSide ? distance < -.00001f : distance > .00001f)
                {
                    visible = false;
                    break;
                }
            }
            crownRoot.gameObject.SetActive(visible);
        }

        private static void ValidateOperation(CutPlaneSaveData operation)
        {
            if (operation == null ||
                !PhysicsBody.IsFinite(new Vector3(operation.nx, operation.ny, operation.nz)) ||
                !PhysicsBody.IsFinite(operation.distance))
                throw new InvalidOperationException("Saved cut plane is invalid.");
            var magnitude = new Vector3(operation.nx, operation.ny, operation.nz).magnitude;
            if (Mathf.Abs(magnitude - 1f) > .001f)
                throw new InvalidOperationException("Saved cut plane normal is not normalized.");
        }

        private static int TriangleCount(Mesh mesh)
        {
            var count = 0;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                count += mesh.GetTriangles(submesh).Length / 3;
            return count;
        }

        private static CutStateSaveData CloneState(CutStateSaveData source)
        {
            if (source == null) return new CutStateSaveData();
            return new CutStateSaveData
            {
                version = source.version,
                planes = (source.planes ?? Array.Empty<CutPlaneSaveData>())
                    .Select(CloneOperation).ToArray()
            };
        }

        private static CutPlaneSaveData CloneOperation(CutPlaneSaveData source)
        {
            return new CutPlaneSaveData
            {
                nx = source.nx,
                ny = source.ny,
                nz = source.nz,
                distance = source.distance,
                positiveSide = source.positiveSide
            };
        }

        private void ReleaseOwnedMeshes()
        {
            DestroyRuntimeMesh(ownedSurface);
            DestroyRuntimeMesh(ownedCollision);
            ownedSurface = null;
            ownedCollision = null;
        }

        private void OnDestroy() => ReleaseOwnedMeshes();

        internal static void DestroyRuntimeMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
