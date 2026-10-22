using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    public sealed class GameItem : MonoBehaviour
    {
        private ItemRegistry registry;
        private GameSession session;
        private ItemDefinition definition;
        private ItemInteractionFocusTarget[] focusTargets =
            Array.Empty<ItemInteractionFocusTarget>();
        private readonly Dictionary<Renderer, Material[]> recycleBaseMaterials =
            new Dictionary<Renderer, Material[]>();
        private Material recycleFocusMaterial;
        private PhysicsGrabInteractable grab;
        private RuntimeCuttable cuttable;
        private NavMeshObstacle obstacle;
        private Pose stablePose;
        private Guid interactionReleaseOwner;
        private bool fixedAfterPlacement, recycleFocused;
        private float nextUnavailableHint;
        public PhysicsBody Body { get; private set; }
        public ItemInstance Instance { get; private set; }
        public Vector3 ApproachPosition => transform.TransformPoint(definition.ApproachOffset);
        public ItemDefinition Definition => definition;
        public string InventoryDefinitionId { get; private set; }
        public string InventoryUnitId { get; private set; }
        public bool IsFixedAfterPlacement => fixedAfterPlacement;
        public bool IsImmovableAfterPlacement =>
            definition != null && definition.ImmovableAfterPlacement;
        public bool IsRecycleFocused => recycleFocused;
        public Material RecycleFocusMaterial => recycleFocusMaterial;
        public RuntimeCuttable Cuttable => cuttable;
        public bool IsHeld => Instance != null &&
            (Instance.ControlState == ItemControlState.PlayerHeld ||
             Instance.ControlState == ItemControlState.HeldByCharacter);
        public bool IsReserved => Instance != null && Instance.IsCharacterOwned;
        public float LastInteractionReleasePositionDelta { get; private set; }
        public float LastInteractionReleaseRotationDelta { get; private set; }
        public bool CanPlayerUse => Instance != null && !Instance.IsOccupied &&
            Body != null && !Body.IsPaused;
        public ItemRegistry Registry => registry;
        internal GameSession Session => session;

        private void Awake() => ResolveFocusTargets();

        private void ResolveFocusTargets()
            => focusTargets = GetComponentsInChildren<ItemInteractionFocusTarget>(true);

        public void ReportUnavailable()
        {
            if (!IsReserved || Time.unscaledTime < nextUnavailableHint) return;
            nextUnavailableHint = Time.unscaledTime + 1;
            session?.Notifications.ShowHint($"冷月正在使用{definition.DisplayName}。");
        }

        public void Stabilize(ItemReservation lease)
        {
            if (lease == null || !lease.IsValid || lease.InteractionId != Instance.InteractionId ||
                !IsReserved) throw new InvalidOperationException("物品预约已失效。");
            var id = lease.InteractionId;
            if (interactionReleaseOwner != id)
            {
                interactionReleaseOwner = id;
                lease.OnRelease(reason =>
                {
                    if (this == null || Body == null) return;
                    Body.EndInteraction(id);
                    interactionReleaseOwner = Guid.Empty;
                    if (obstacle != null)
                        obstacle.enabled = fixedAfterPlacement;
                });
            }
            LastInteractionReleasePositionDelta = 0;
            LastInteractionReleaseRotationDelta = 0;
            Body.BeginInteraction(id);
            if (obstacle != null && !fixedAfterPlacement) obstacle.enabled = false;
        }

        public void CheckpointInteraction()
        {
            if (!IsReserved || !Body.IsInteractionControlled)
                throw new InvalidOperationException("物品工作位置缺少有效操作所有权。");
            stablePose = Body.Pose;
        }

        public void ReleaseInteractionToPhysics(ItemReservation lease)
        {
            if (lease == null || !lease.IsValid ||
                lease.InteractionId != Instance.InteractionId ||
                !IsReserved || !Body.IsInteractionControlled)
                throw new InvalidOperationException(
                    "物品释放时缺少有效操作所有权。");
            stablePose = Body.Pose;
            var before = Body.Pose;
            Body.EndInteraction(lease.InteractionId);
            LastInteractionReleasePositionDelta = Vector3.Distance(
                before.position, Body.Pose.position);
            LastInteractionReleaseRotationDelta = Quaternion.Angle(
                before.rotation, Body.Pose.rotation);
        }

        public void Bind(ItemDefinition content, ItemRegistry owner, GameSession gameSession,
            string instanceId, string stateId = "default",
            string inventoryDefinitionId = null, string inventoryUnitId = null)
        {
            definition = content;
            registry = owner;
            session = gameSession;
            InventoryDefinitionId = string.IsNullOrEmpty(inventoryDefinitionId)
                ? content.Id : inventoryDefinitionId;
            InventoryUnitId = string.IsNullOrEmpty(inventoryUnitId)
                ? instanceId : inventoryUnitId;
            fixedAfterPlacement = content.FixedAfterPlacement;
            Instance = new ItemInstance(instanceId, content.Id, content.DisplayName, content.Capabilities, stateId);
            registry.Register(Instance);
            if (content.Interaction != null)
                registry.AddCapability(instanceId, content.Interaction.capability);
            cuttable = GetComponent<RuntimeCuttable>();
            if (content.Cuttable.enabled)
            {
                if (cuttable == null)
                    throw new InvalidOperationException(
                        $"{content.DisplayName}已标记可切割，但预制体缺少 RuntimeCuttable。");
                cuttable.Bind(content.Cuttable);
            }
            stablePose = new Pose(transform.position, transform.rotation);
            obstacle = GetComponent<NavMeshObstacle>();

            Body = GetComponent<PhysicsBody>() ?? gameObject.AddComponent<PhysicsBody>();
            Body.Configure(content.Physics);
            if (cuttable != null)
                Body.Rigidbody.mass = Mathf.Max(.001f,
                    content.Physics.mass * cuttable.VolumeRatio);
            Body.Rigidbody.useGravity =
                content.PlacementSurface != ItemPlacementSurface.Wall;
            Body.Place(new Pose(transform.position, transform.rotation));
            grab = GetComponent<PhysicsGrabInteractable>() ?? gameObject.AddComponent<PhysicsGrabInteractable>();
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;
            grab.colliders.Clear();
            foreach (var itemCollider in Body.Shapes) grab.colliders.Add(itemCollider);
            grab.hoverEntered.AddListener(OnHoverEntered);
            grab.hoverExited.AddListener(OnHoverExited);
            grab.selectEntered.AddListener(OnSelected);
            grab.selectExited.AddListener(OnReleased);
            grab.PlayerGrabbable = !fixedAfterPlacement;
            if (content.ImmovableAfterPlacement)
                Body.SetContact(true);
            if (fixedAfterPlacement && obstacle != null) obstacle.enabled = true;
            if (content.Breakable.enabled)
            {
                var breakable = GetComponent<BreakableItem>() ??
                                gameObject.AddComponent<BreakableItem>();
                breakable.Configure(this, content.Breakable);
            }
            if (content.ImpactSound.enabled)
            {
                var impact = GetComponent<ImpactSoundEmitter>() ??
                             gameObject.AddComponent<ImpactSoundEmitter>();
                impact.Configure(this, content.ImpactSound);
            }
            if (content.Cooking.enabled)
            {
                var food = GetComponent<CookableFood>() ??
                           gameObject.AddComponent<CookableFood>();
                food.Configure(content.Cooking, this);
            }
            if (content.Interaction != null &&
                content.Interaction.capability == "cutting-tool")
            {
                var tool = GetComponent<RuntimeCuttingTool>() ??
                           gameObject.AddComponent<RuntimeCuttingTool>();
                tool.Configure(this, session, content.Interaction);
            }

            ResolveFocusTargets();
        }

        public void SetFocused(bool value)
        {
            if (IsHeld || IsReserved) value = false;
            foreach (var target in focusTargets)
                if (target != null) target.SetFocused(value);
        }

        public void SetRecycleFocused(bool value)
        {
            if (definition == null || definition.FocusMaterial == null ||
                IsHeld || IsReserved)
                value = false;
            SetWholeItemFocused(value);
        }

        private void SetWholeItemFocused(bool value)
        {
            if (definition == null || definition.FocusMaterial == null)
                value = false;
            if (value == recycleFocused) return;
            if (value)
            {
                if (recycleFocusMaterial == null)
                {
                    recycleFocusMaterial = new Material(definition.FocusMaterial)
                    {
                        name = definition.FocusMaterial.name + " (Recycle)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    recycleFocusMaterial.SetColor(
                        "_Color", new Color(1f, .08f, .34f, .96f));
                    recycleFocusMaterial.SetFloat("_OutlineWidth", .007f);
                    recycleFocusMaterial.SetFloat("_Intensity", 1.25f);
                }
                recycleBaseMaterials.Clear();
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    var materials = renderer.sharedMaterials
                        .Where(material => material != recycleFocusMaterial).ToArray();
                    recycleBaseMaterials.Add(renderer, materials);
                    renderer.sharedMaterials =
                        materials.Concat(new[] { recycleFocusMaterial }).ToArray();
                }
            }
            else
            {
                foreach (var entry in recycleBaseMaterials)
                    if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
                recycleBaseMaterials.Clear();
            }
            recycleFocused = value;
        }

        public ItemSaveData Capture(Transform anchor)
        {
            // Holding uses the last settled checkpoint. Free bodies retain their actual
            // pose; interrupted falls resume under gravity after a validated restore.
            var pose = IsHeld || IsReserved ? stablePose : Body.Pose;
            var position = anchor.InverseTransformPoint(pose.position);
            var rotation = Quaternion.Inverse(anchor.rotation) * pose.rotation;
            return new ItemSaveData
            {
                instanceId = Instance.Id, definitionId = Instance.DefinitionId,
                inventoryDefinitionId = InventoryDefinitionId, inventoryUnitId = InventoryUnitId,
                stateId = Instance.StateId,
                cutState = cuttable?.CaptureState(),
                needsGravitySettling =
                    definition.PlacementSurface != ItemPlacementSurface.Wall &&
                    !Body.HasSupport(),
                x = position.x, y = position.y, z = position.z,
                qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w
            };
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            SetFocused(false);
            if (obstacle != null) obstacle.enabled = false;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
        }

        private void Update()
        {
            if (Body == null || Body.IsPaused) return;
            if (IsReserved) return;
            if (IsImmovableAfterPlacement)
            {
                if (obstacle != null) obstacle.enabled = true;
                return;
            }
            var settled = Body.IsSettled && !IsHeld;
            if (obstacle != null) obstacle.enabled = settled;
            if (!settled) return;
            var pose = Body.Pose;
            if (Vector3.Distance(pose.position, stablePose.position) < .005f &&
                Quaternion.Angle(pose.rotation, stablePose.rotation) < 1f) return;
            if (!Body.HasSupport()) return;
            stablePose = pose;
            session?.RequestPhysicsSave();
        }

        public void SetPaused(bool paused)
        {
            if (paused) grab.CancelGrab();
            Body.SetPaused(paused);
            grab.enabled = !paused;
        }

        public void Retire()
        {
            grab.CancelGrab();
            gameObject.SetActive(false);
        }

        internal bool TryBreak(BreakSeverity severity)
            => session != null && Instance != null && session.BreakItem(Instance.Id, severity);

        private void OnDisable()
        {
            SetFocused(false);
            SetRecycleFocused(false);
            if (Instance != null && registry != null && registry.IsCurrent(Instance))
                registry.Remove(Instance.Id);
        }

        private void OnDestroy()
        {
            if (recycleFocusMaterial != null) Destroy(recycleFocusMaterial);
            if (grab != null)
            {
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
                grab.selectEntered.RemoveListener(OnSelected);
                grab.selectExited.RemoveListener(OnReleased);
            }
            if (Instance != null && registry != null && registry.IsCurrent(Instance))
                registry.Remove(Instance.Id);
        }
    }
}
