using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(GameBootstrap))]
    public sealed class ItemPlacementPreviewController : MonoBehaviour
    {
        private static readonly Color AvailableColor = new Color(.12f, .9f, .3f, .52f);
        private static readonly Color BlockedColor = new Color(.95f, .12f, .16f, .52f);
        private GameBootstrap bootstrap;
        private ItemDefinition definition;
        private GameObject projection;
        private Material material;
        public bool CanPlace { get; private set; }
        public Pose Placement { get; private set; }
        public bool IsActive => definition != null;

        private void Awake() => bootstrap = GetComponent<GameBootstrap>();

        public bool IsPreviewing(ItemDefinition item) => definition == item;

        public void Begin(ItemDefinition item)
        {
            if (item == null || !item.PlacementPreview)
                throw new ArgumentException("This item does not use placement preview.");
            definition = item;
            EnsureProjection();
            projection.transform.localScale = item.PlacementSurface == ItemPlacementSurface.Wall
                ? new Vector3(item.Footprint.x, item.PlacementHeight, .006f)
                : new Vector3(item.Footprint.x, .006f, item.Footprint.y);
            projection.SetActive(true);
            Refresh();
        }

        public void Cancel()
        {
            definition = null;
            CanPlace = false;
            if (projection != null) projection.SetActive(false);
        }

        private void Update()
        {
            if (definition == null) return;
            Refresh();
        }

        private void Refresh()
        {
            var session = bootstrap.Session;
            var cameraTransform = Camera.main == null ? null : Camera.main.transform;
            if (session == null || session.Paused || cameraTransform == null)
            {
                CanPlace = false;
                if (projection != null) projection.SetActive(false);
                return;
            }
            projection.SetActive(true);
            if (definition.PlacementSurface == ItemPlacementSurface.Wall)
            {
                CanPlace = session.TryResolveWallPlacement(definition,
                    new Ray(cameraTransform.position, cameraTransform.forward),
                    null, out var wallPlacement);
                Placement = CanPlace
                    ? wallPlacement
                    : new Pose(cameraTransform.position + cameraTransform.forward * 1.8f,
                        Quaternion.LookRotation(-cameraTransform.forward, Vector3.up));
                var center = Placement.position +
                             Vector3.up * (definition.PlacementHeight * .5f);
                projection.transform.SetPositionAndRotation(
                    center, Placement.rotation);
                SetProjectionColor();
                return;
            }
            if (session.TryResolveSurfacePlacement(definition,
                    new Ray(cameraTransform.position, cameraTransform.forward),
                    null, out var surfacePlacement))
            {
                CanPlace = true;
                Placement = surfacePlacement;
                projection.transform.SetPositionAndRotation(
                    Placement.position + Vector3.up * .004f, Placement.rotation);
                SetProjectionColor();
                return;
            }
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            var desired = cameraTransform.position + forward * 1.8f;
            desired.y = session.Navigation.FloorHeight;
            var desiredPose = new Pose(desired,
                definition.ResolvePlacementRotation(-forward));
            CanPlace = session.TryResolveExactPlacement(
                definition, desiredPose, null, out var placement);
            Placement = CanPlace ? placement : desiredPose;
            projection.transform.SetPositionAndRotation(
                Placement.position + Vector3.up * .004f, Placement.rotation);
            SetProjectionColor();
        }

        private void SetProjectionColor()
        {
            material.SetColor("_BaseColor", CanPlace ? AvailableColor : BlockedColor);
            material.color = CanPlace ? AvailableColor : BlockedColor;
        }

        private void EnsureProjection()
        {
            if (projection != null) return;
            projection = GameObject.CreatePrimitive(PrimitiveType.Cube);
            projection.name = "Item Placement Projection";
            projection.transform.SetParent(transform, false);
            Destroy(projection.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("A URP shader is required for placement preview.");
            material = new Material(shader) { name = "Runtime Placement Projection" };
            material.SetFloat("_Surface", 1);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            projection.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
