using System;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private PlayerInteractionModeController input;
        private bool gestureReady, gesturePressed;
        private InputAction confirmAction, cancelAction;
        private bool controllerReady;
        private float cancelGestureStarted = -1;
        public event Action<ItemDefinition> ConfirmRequested;
        public event Action<bool> ActiveChanged;
        public bool CanPlace { get; private set; }
        public Pose Placement { get; private set; }
        public bool IsActive => definition != null;

        private void Awake()
        {
            confirmAction = new InputAction("确认放置", InputActionType.Button,
                "<XRController>{RightHand}/triggerPressed");
            cancelAction = new InputAction("取消放置", InputActionType.Button,
                "<XRController>{RightHand}/secondaryButton");
        }

        private void OnEnable()
        {
            confirmAction.Enable();
            cancelAction.Enable();
        }

        public bool IsPreviewing(ItemDefinition item) => definition == item;

        public void Begin(ItemDefinition item)
        {
            if (item == null || !item.PlacementPreview)
                throw new ArgumentException("This item does not use placement preview.");
            bootstrap = GetComponent<GameBootstrap>();
            input = FindObjectOfType<PlayerInteractionModeController>();
            if (definition == null && input != null && input.PlacementActive)
                throw new InvalidOperationException("请先完成当前放置。");
            definition = item;
            if (input != null) input.PlacementActive = true;
            gestureReady = gesturePressed = false;
            controllerReady = false;
            cancelGestureStarted = -1;
            EnsureProjection();
            projection.transform.localScale = item.PlacementSurface == ItemPlacementSurface.Wall
                ? new Vector3(item.Footprint.x, item.PlacementHeight, .006f)
                : new Vector3(item.Footprint.x, .006f, item.Footprint.y);
            projection.SetActive(true);
            Refresh();
            ActiveChanged?.Invoke(true);
        }

        public void Cancel()
        {
            if (definition == null) return;
            definition = null;
            CanPlace = false;
            gestureReady = gesturePressed = false;
            cancelGestureStarted = -1;
            if (input != null) input.PlacementActive = false;
            if (projection != null) projection.SetActive(false);
            ActiveChanged?.Invoke(false);
        }

        private void Update()
        {
            if (definition == null) return;
            if (cancelAction.WasPressedThisFrame())
            {
                Cancel();
                return;
            }
            Refresh();
            if (input == null || !input.DirectHandsEnabled)
            {
                if (!confirmAction.IsPressed()) controllerReady = true;
                if (controllerReady && CanPlace && confirmAction.WasPressedThisFrame())
                {
                    controllerReady = false;
                    ConfirmRequested?.Invoke(definition);
                }
                return;
            }
            var hands = input.Hands;
            if (hands != null && hands.Length == 2 &&
                hands[0].Tracked && hands[1].Tracked && hands[0].Closed && hands[1].Closed)
            {
                if (cancelGestureStarted < 0) cancelGestureStarted = Time.unscaledTime;
                if (Time.unscaledTime - cancelGestureStarted >= 1f) Cancel();
                return;
            }
            cancelGestureStarted = -1;
            if (!CanPlace || !input.TryGetPlacementHand(out _, out var pressed))
            {
                gestureReady = gesturePressed = false;
                return;
            }
            if (!pressed && !gesturePressed) gestureReady = true;
            var confirm = gestureReady && gesturePressed && !pressed;
            gesturePressed = pressed;
            if (confirm)
            {
                gestureReady = false;
                ConfirmRequested?.Invoke(definition);
            }
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
            if (input != null && input.DirectHandsEnabled)
            {
                RefreshDirectHand(cameraTransform.position);
                return;
            }
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
                    null, out var surfacePlacement, cameraTransform.position))
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

        private void RefreshDirectHand(Vector3 viewerPosition)
        {
            if (!input.TryGetPlacementHand(out var hand, out _))
            {
                CanPlace = false;
                projection.SetActive(false);
                return;
            }
            var session = bootstrap.Session;
            if (definition.PlacementSurface == ItemPlacementSurface.Wall)
            {
                var direction = hand.rotation * Vector3.forward;
                // Wall controls are placed within arm's reach, never across the room.
                CanPlace = session.TryResolveWallPlacement(definition,
                    new Ray(hand.position, direction), null, out var wall);
                var center = wall.position + Vector3.up * definition.PlacementHeight * .5f;
                CanPlace &= Vector3.Distance(center, hand.position) <= .3f + definition.Footprint.y * .5f;
                Placement = wall;
                projection.transform.SetPositionAndRotation(
                    CanPlace ? center : hand.position + direction * .15f,
                    CanPlace ? wall.rotation : Quaternion.LookRotation(-direction));
            }
            else
            {
                // Vertical projection under the actual palm; no visible aiming ray.
                CanPlace = session.TryResolveSurfacePlacement(definition,
                    new Ray(hand.position, Vector3.down), null, out var surface,
                    viewerPosition);
                CanPlace &= hand.position.y - surface.position.y >= -.01f &&
                            hand.position.y - surface.position.y <= 2f;
                Placement = surface;
                projection.transform.SetPositionAndRotation(
                    CanPlace ? surface.position + Vector3.up * .004f : hand.position,
                    CanPlace ? surface.rotation : Quaternion.identity);
            }
            SetProjectionColor();
        }

        private void OnDisable()
        {
            Cancel();
            confirmAction.Disable();
            cancelAction.Disable();
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
            confirmAction.Dispose();
            cancelAction.Dispose();
            if (material != null) Destroy(material);
        }
    }
}
