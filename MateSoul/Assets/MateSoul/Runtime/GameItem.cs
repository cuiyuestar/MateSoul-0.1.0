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
        private IRuntimeWorldFactSource[] worldFactSources =
            Array.Empty<IRuntimeWorldFactSource>();
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
        // Being carried by the player is not a lock: the radio's own buttons and
        // knobs have to stay usable in the player's other hand.
        public bool CanPlayerUse => Instance != null &&
            (!Instance.IsOccupied || Instance.ControlState == ItemControlState.PlayerHeld) &&
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
#region debug-point C-D-E:stabilize-before
#if UNITY_EDITOR
            var debugBeforePayload =
                $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"C-D-E\",\"location\":\"GameItem.Stabilize.Before\"," +
                $"\"msg\":\"[DEBUG] item before stabilize\",\"data\":{{" +
                $"\"item\":\"{definition.Id}\",\"pose\":{JsonUtility.ToJson(Body.Pose.position)}," +
                $"\"stablePose\":{JsonUtility.ToJson(stablePose.position)}," +
                $"\"kinematic\":{Body.Rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                $"\"gravity\":{Body.Rigidbody.useGravity.ToString().ToLowerInvariant()}," +
                $"\"detectCollisions\":{Body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                $"\"support\":{Body.HasSupport().ToString().ToLowerInvariant()}," +
                $"\"control\":\"{Instance.ControlState}\"}}}}";
            var debugBeforeRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7777/event", debugBeforePayload, "application/json");
            debugBeforeRequest.SendWebRequest().completed += _ => debugBeforeRequest.Dispose();
#endif
#endregion
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
#region debug-point C-E:stabilize-after
#if UNITY_EDITOR
            var debugAfterPayload =
                $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"C-E\",\"location\":\"GameItem.Stabilize.After\"," +
                $"\"msg\":\"[DEBUG] item after stabilize\",\"data\":{{" +
                $"\"item\":\"{definition.Id}\",\"pose\":{JsonUtility.ToJson(Body.Pose.position)}," +
                $"\"kinematic\":{Body.Rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                $"\"gravity\":{Body.Rigidbody.useGravity.ToString().ToLowerInvariant()}," +
                $"\"detectCollisions\":{Body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                $"\"support\":{Body.HasSupport().ToString().ToLowerInvariant()}," +
                $"\"controlled\":{Body.IsInteractionControlled.ToString().ToLowerInvariant()}}}}}";
            var debugAfterRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7777/event", debugAfterPayload, "application/json");
            debugAfterRequest.SendWebRequest().completed += _ => debugAfterRequest.Dispose();
#endif
#endregion
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
            if (content.IsEdible)
                registry.AddCapability(instanceId, "edible");
            if (content.UseAnimation != null)
            {
                registry.AddCapability(instanceId, "use");
                registry.SetUseLabel(instanceId, content.UseAnimation.actionLabel);
            }
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

            RuntimeColliderRefiner.Ensure(gameObject);
            Body = GetComponent<PhysicsBody>() ?? gameObject.AddComponent<PhysicsBody>();
            Body.Configure(content.Physics);
            // A carried pan still has to catch the food dropped into it.
            Body.RetainCollisionsWhileHeld =
                GetComponent<CookwareHeatReceiver>() != null;
            if (Body.RetainCollisionsWhileHeld)
                Debug.Log($"MATESOUL_COOK_BIND item={content.Id} cookware=1 " +
                          $"retainCollisions=1");
            if (cuttable != null)
                Body.Rigidbody.mass = Mathf.Max(.001f,
                    content.Physics.mass * cuttable.VolumeRatio);
            Body.Rigidbody.useGravity =
                content.PlacementSurface != ItemPlacementSurface.Wall;
            Body.Place(new Pose(transform.position, transform.rotation));
            if (!fixedAfterPlacement && ItemGripAnchor.Supports(content.Interaction))
            {
                var grip = GetComponent<ItemGripAnchor>() ?? gameObject.AddComponent<ItemGripAnchor>();
                grip.Configure(content.Interaction);
            }
            grab = GetComponent<PhysicsGrabInteractable>() ?? gameObject.AddComponent<PhysicsGrabInteractable>();
            var enableGrab = grab.enabled;
            grab.enabled = false;
            grab.useDynamicAttach = true;
            grab.selectMode = InteractableSelectMode.Multiple;
            grab.throwOnDetach = false;
            grab.colliders.Clear();
            foreach (var itemCollider in Body.Shapes) grab.colliders.Add(itemCollider);
            grab.hoverEntered.AddListener(OnHoverEntered);
            grab.hoverExited.AddListener(OnHoverExited);
            grab.selectEntered.AddListener(OnSelected);
            grab.selectExited.AddListener(OnReleased);
            grab.PlayerGrabbable = !fixedAfterPlacement;
            grab.enabled = enableGrab;
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
            worldFactSources = GetComponentsInChildren<MonoBehaviour>(true)
                .OfType<IRuntimeWorldFactSource>().ToArray();
#region debug-point C:grab-bind
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var bindPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                              $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                              $"\"location\":\"GameItem.Bind\",\"msg\":\"[DEBUG] grab interactable bound\"," +
                              $"\"data\":{{\"id\":\"{Instance.Id}\",\"definition\":\"{definition.Id}\"," +
                              $"\"grabEnabled\":{grab.isActiveAndEnabled.ToString().ToLowerInvariant()}," +
                              $"\"manager\":{(grab.interactionManager != null).ToString().ToLowerInvariant()}," +
                              $"\"colliders\":{GetComponentsInChildren<Collider>(true).Length}," +
                              $"\"focusTargets\":{focusTargets.Length}" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", bindPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        internal void CaptureRuntimeWorldFacts(
            List<RuntimeWorldFactSnapshot> facts, int limit)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            foreach (var source in worldFactSources)
            {
                if (facts.Count >= limit) return;
                if (source != null &&
                    source.TryCaptureRuntimeWorldFact(out var fact) &&
                    fact != null)
                    facts.Add(fact);
            }
        }

        public void SetFocused(bool value)
        {
            if (IsHeld || IsReserved) value = false;
#region debug-point B:focus-material
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var focusPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.SetFocused\",\"msg\":\"[DEBUG] interaction focus apply\"," +
                               $"\"data\":{{\"id\":\"{Instance?.Id ?? "none"}\"," +
                               $"\"requested\":{value.ToString().ToLowerInvariant()}," +
                               $"\"held\":{IsHeld.ToString().ToLowerInvariant()}," +
                               $"\"targets\":{focusTargets.Length}" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", focusPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
            foreach (var target in focusTargets)
                if (target != null) target.SetFocused(value);
#region debug-point B:item-focus
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload =
                $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"GameItem.SetFocused\",\"msg\":\"[DEBUG] item focus changed\"," +
                $"\"data\":{{\"definition\":\"{definition?.Id ?? "none"}\",\"focused\":" +
                $"{value.ToString().ToLowerInvariant()},\"targets\":{focusTargets.Length}}}}}";
            var request = UnityWebRequest.Post(
                "http://127.0.0.1:7791/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
#endif
#endregion
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
#region debug-point C:grab-hover-enter
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var hoverPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.OnHoverEntered\",\"msg\":\"[DEBUG] grab hover entered\"," +
                               $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                               $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", hoverPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
#region debug-point C:grab-hover-exit
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var hoverPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.OnHoverExited\",\"msg\":\"[DEBUG] grab hover exited\"," +
                               $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                               $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", hoverPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
#region debug-point C:grab-select
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var rayInteractor = args.interactorObject as XRRayInteractor;
            var cameraTransform = Camera.main == null ? null : Camera.main.transform;
            var selectPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                $"\"location\":\"GameItem.OnSelected\",\"msg\":\"[DEBUG] grab select entered\"," +
                                $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                                $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"," +
                                $"\"forceGrab\":{(rayInteractor != null && rayInteractor.useForceGrab).ToString().ToLowerInvariant()}," +
                                $"\"cameraDistance\":{(cameraTransform == null ? -1f : Vector3.Distance(transform.position, cameraTransform.position))}" +
                                "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", selectPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
            SetFocused(false);
            if (obstacle != null) obstacle.enabled = false;
#region debug-point B:item-grab
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload =
                $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"GameItem.OnSelected\",\"msg\":\"[DEBUG] item grabbed\"," +
                $"\"data\":{{\"definition\":\"{definition.Id}\"}}}}";
            var request = UnityWebRequest.Post(
                "http://127.0.0.1:7791/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
#endif
#endregion
        }

        private void OnReleased(SelectExitEventArgs args)
        {
#region debug-point C:grab-release
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var releasePayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                 $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                 $"\"location\":\"GameItem.OnReleased\",\"msg\":\"[DEBUG] grab select exited\"," +
                                 $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                                 $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", releasePayload, "application/json")
                .SendWebRequest();
#endif
#endregion
#region debug-point B:item-release
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload =
                $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"GameItem.OnReleased\",\"msg\":\"[DEBUG] item released\"," +
                $"\"data\":{{\"definition\":\"{definition.Id}\"}}}}";
            var request = UnityWebRequest.Post(
                "http://127.0.0.1:7791/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
#endif
#endregion
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
