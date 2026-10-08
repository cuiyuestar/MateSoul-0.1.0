using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using MateSoul.Wardrobe;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public sealed class GameSession : IDisposable
    {
        private readonly Dictionary<string, ItemDefinition> definitions;
        private readonly Dictionary<string, GameItem> views = new Dictionary<string, GameItem>();
        private readonly HashSet<BreakableDebris> debris = new HashSet<BreakableDebris>();
        private readonly IGameSaveStore store;
        private readonly ISpatialAnchorService anchors;
        private readonly Transform itemRoot;
        private readonly LengyueWardrobeController wardrobe;
        private readonly CharacterRuntimeContextProvider characterContext;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource itemOperation;
        private GameSaveData saved;
        private bool itemsRestored, itemRestoreFailed, disposed;
        private float physicsSaveDelay = -1;
        private readonly Collider[] placementOverlaps = new Collider[64];
        private readonly RaycastHit[] placementHits = new RaycastHit[32];
        public ActionCatalog Catalog { get; } = new ActionCatalog();
        public AvatarActionCoordinator Avatar { get; }
        public ItemRegistry Items { get; } = new ItemRegistry();
        public RealityItemDirectory RealityItems { get; }
        public MoodSystem Mood { get; } = new MoodSystem();
        public CharacterPersonaState Persona { get; }
        public ICharacterContextProvider CharacterContext => characterContext;
        public ConversationOrchestrator Conversation { get; }
        public AutonomousBehaviour Autonomy { get; }
        public INavigationService Navigation { get; }
        public IAvatarMotor Motor { get; }
        public bool Paused { get; private set; }
        public bool Subtitles { get; set; }
        private string notice;
        public SystemNotifications Notifications { get; }
        public string Notice
        {
            get => notice;
            private set { notice = value; if (!string.IsNullOrEmpty(value)) Notifications.ShowHint(value); }
        }
        public bool ItemsReady => itemsRestored;
        public bool ItemRestoreFailed => itemRestoreFailed;
        public bool IsRestoringItems => !itemsRestored && !itemRestoreFailed;
        public IEnumerable<GameItem> ItemViews => views.Values;
        public IEnumerable<BreakableDebris> Debris => debris;
        public IEnumerable<ItemDefinition> ItemDefinitions => definitions.Values;
        public IReadOnlyList<ItemDefinition> CatalogItems { get; }
        public LengyueWardrobeController Wardrobe => wardrobe;
        public RealityMeshSource RealityMesh { get; private set; }
        public RealChairScanner Chairs { get; private set; }
        public RealBedScanner Beds { get; private set; }

        public void ConfigureHandHold(HandIKRig rig, Transform playerHead)
        {
            if (rig == null) throw new InvalidOperationException("角色缺少牵手 IK 配置。");
            rig.Bind(this, playerHead);
            Catalog.Register(new ActionDefinition(HandHoldAction.LeftId, "Hold the left hand",
                ControlChannels.Locomotion | ControlChannels.LeftArm,
                intent => new HandHoldAction((AvatarMotor)Motor, Navigation, rig,
                    intent.Payload as HandHoldRequest, playerHead),
                timeoutSeconds: 3600, exposeToAI: false, exitTimeoutSeconds: 1));
            Catalog.Register(new ActionDefinition(HandHoldAction.RightId, "Hold the right hand",
                ControlChannels.Locomotion | ControlChannels.RightArm,
                intent => new HandHoldAction((AvatarMotor)Motor, Navigation, rig,
                    intent.Payload as HandHoldRequest, playerHead),
                timeoutSeconds: 3600, exposeToAI: false, exitTimeoutSeconds: 1));
        }

        public void ConfigureRealityItems(RoomMapSnapshot room, AvatarSeatingRig seatingRig,
            AvatarLyingRig lyingRig)
        {
            RealityItems.Load(room.RealityItems);
            RealityMesh = new RealityMeshSource(room);
            Chairs = new RealChairScanner(RealityItems, room, RealityMesh);
            Beds = new RealBedScanner(RealityItems, room, RealityMesh);
            var sit = RealityInteractionCatalog.Get(RealityInteractionIds.Sit);
            Catalog.Register(new ActionDefinition(sit.Id, sit.Description,
                ControlChannels.Locomotion | ControlChannels.FullBody | ControlChannels.Gaze,
                intent => new SitOnChairAction(Chairs, Items, intent.TargetId,
                    (AvatarMotor)Motor, Navigation, seatingRig),
                timeoutSeconds: 300f, targetCapability: sit.Id, exitTimeoutSeconds: 4f));
            var lie = RealityInteractionCatalog.Get(RealityInteractionIds.Lie);
            Catalog.Register(new ActionDefinition(lie.Id, lie.Description,
                ControlChannels.Locomotion | ControlChannels.FullBody | ControlChannels.Gaze,
                intent => new LieOnBedAction(Beds, Items, intent.TargetId,
                    (AvatarMotor)Motor, Navigation, lyingRig),
                timeoutSeconds: 300f, targetCapability: lie.Id, exitTimeoutSeconds: 5f));
            RealityMesh.Start();
        }

        public GameSession(IAvatarMotor motor, INavigationService navigation, LengyueWardrobeController wardrobe,
            IEnumerable<ItemDefinition> definitions, Transform itemRoot, IGameSaveStore store,
            ISpatialAnchorService anchors, ILLMProvider llm, CharacterPersonaDefinition personaDefinition,
            SystemNotifications notifications = null)
        {
            Notifications = notifications ?? new SystemNotifications();
            Motor = motor; Navigation = navigation; this.wardrobe = wardrobe;
            RealityItems = new RealityItemDirectory(Items);
            this.definitions = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
            CatalogItems = this.definitions.Values.Where(d => d.CatalogVisible).ToArray();
            this.itemRoot = itemRoot; this.store = store; this.anchors = anchors;
            saved = store.Load();
            Mood.Restore(saved.mood);
            Persona = new CharacterPersonaState(personaDefinition, saved.personality);
            Subtitles = saved.subtitles;
            RegisterActions();
            Avatar = new AvatarActionCoordinator(Catalog);
            Autonomy = new AutonomousBehaviour(Avatar, navigation, motor, Items);
            characterContext = new CharacterRuntimeContextProvider(
                Persona, wardrobe, Mood, Avatar, Catalog, Items, Motor as AvatarMotor);
            // Pay one-time snapshot/generic setup during session assembly,
            // not on the player's first conversation turn.
            characterContext.Capture();
            Conversation = new ConversationOrchestrator(llm, new SessionConversationContext(),
                Catalog, Avatar, Items, Mood, characterContext);
            Conversation.Changed += OnConversationChanged;
            if (!string.IsNullOrEmpty(saved.outfitId))
            {
                var restore = Avatar.Submit(new AvatarIntent("outfit", IntentSource.System, payload: saved.outfitId));
                Avatar.Tick(0);
                if (restore.State != ActionState.Succeeded)
                    Notice = $"服装恢复失败：{restore.Error}";
            }
            UpdateAvatarAvailability();
        }

        private void RegisterActions()
        {
            var body = ControlChannels.Locomotion | ControlChannels.FullBody | ControlChannels.Gaze;
            Catalog.Register(new ActionDefinition("move", "Walk to a navigation point", body,
                intent => new MoveAction(Motor, Navigation, () => (Vector3)intent.Payload), exposeToAI: false));
            Catalog.Register(new ActionDefinition("stay", "Stay here briefly", body,
                _ => new StayAction(Motor)));
            Catalog.Register(new ActionDefinition("visit", "Walk to an item and look at it", body,
                intent => new VisitItemAction(Items, intent.TargetId,
                    () => views.TryGetValue(intent.TargetId, out var view) ? view : null, Motor, Navigation),
                targetCapability: "visit"));
            Catalog.Register(new ActionDefinition("outfit", "Change outfit", ControlChannels.Appearance,
                intent => new ChangeOutfitAction(wardrobe, (string)intent.Payload), exposeToAI: false));
        }

        public ActionHandle Submit(AvatarIntent intent)
        {
            if (Paused || disposed) throw new InvalidOperationException("会话未运行。");
            var action = Avatar.Submit(intent);
            if (action.State == ActionState.Rejected) Notice = action.Error;
            return action;
        }

        public void SelectNextPersonality(string traitId)
        {
            if (Paused || disposed) return;
            Persona.SelectNext(traitId);
            Save();
        }

        public void Tick(float seconds)
        {
            if (Paused || disposed) return;
            Notifications.Tick(seconds);
            if (!itemsRestored && !itemRestoreFailed)
            {
                // Retry a lifecycle-cancelled restore once its prior operation has unwound.
                // No automatic retry after a genuine validation/anchor failure.
                _ = RestoreItemsAsync();
                return;
            }
            Chairs?.Tick(Time.unscaledTime);
            Beds?.Tick(Time.unscaledTime);
            Avatar.Tick(seconds);
            Mood.Tick(seconds, Avatar.Leases.IsOccupied(ControlChannels.Body));
            Autonomy.Tick(seconds);
            if (physicsSaveDelay >= 0)
            {
                physicsSaveDelay -= seconds;
                if (physicsSaveDelay <= 0) { physicsSaveDelay = -1; Save(); }
            }
        }

        private void OnConversationChanged()
        {
            if (Conversation.State == ConversationState.Failed || Conversation.State == ConversationState.Cancelled)
                Notifications.ShowHint(Conversation.Text);
            if (!string.IsNullOrEmpty(Conversation.ActionError))
                Notifications.ShowHint(Conversation.ActionError);
        }

        public void RequestPhysicsSave()
        {
            if (!disposed && physicsSaveDelay < 0) physicsSaveDelay = .5f;
        }

        public async Task RestoreItemsAsync()
        {
            if (itemsRestored || itemOperation != null || Paused || disposed) return;
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                itemOperation = operation;
                var previousRoot = new Pose(itemRoot.position, itemRoot.rotation);
                var spawned = new List<GameItem>();
                try
                {
                    itemRestoreFailed = false;
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in saved.items)
                    {
                        if (!definitions.ContainsKey(item.definitionId))
                            throw new InvalidOperationException($"物品定义不存在：{item.definitionId}");
                        if (string.IsNullOrWhiteSpace(item.instanceId) || !ids.Add(item.instanceId))
                            throw new InvalidOperationException("存档包含重复或空物品 ID。");
                        PhysicsBody.ValidatePose(new Pose(new Vector3(item.x, item.y, item.z),
                            new Quaternion(item.qx, item.qy, item.qz, item.qw)));
                    }

                    var usedFallbackPose = false;
                    var roomPose = previousRoot;
                    if (!string.IsNullOrEmpty(saved.roomAnchorId))
                    {
                        try
                        {
                            roomPose = await anchors.LoadAsync(saved.roomAnchorId, operation.Token);
                            operation.Token.ThrowIfCancellationRequested();
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception anchorError)
                        {
                            if (!TryGetSavedRoomPose(out roomPose))
                                throw new InvalidOperationException(
                                    $"旧物品定位失败：{anchorError.Message}", anchorError);
                            usedFallbackPose = true;
                        }
                    }
                    PhysicsBody.ValidatePose(roomPose);
                    itemRoot.SetPositionAndRotation(roomPose.position, roomPose.rotation.normalized);
                    foreach (var item in saved.items)
                    {
                        var view = Spawn(definitions[item.definitionId], item.instanceId,
                            new Vector3(item.x, item.y, item.z),
                            new Quaternion(item.qx, item.qy, item.qz, item.qw), item.stateId);
                        view.SetPaused(true);
                        spawned.Add(view);
                    }
                    Physics.SyncTransforms();
                    var accepted = new HashSet<PhysicsBody>();
                    var avatarMotor = Motor as AvatarMotor;
                    foreach (var view in spawned)
                        if (!view.Body.CanOccupy(view.Body.Pose, ignoredRoot: avatarMotor?.transform))
                            throw new InvalidOperationException("旧物品位置与当前房间或其他物品相交。");
                    // Support graph must reach the static room, independent of save ordering.
                    for (var pass = 0; pass < spawned.Count; pass++)
                        for (var index = 0; index < spawned.Count; index++)
                        {
                            var body = spawned[index].Body;
                            if (accepted.Contains(body)) continue;
                            // Interrupted falls may resume above known support; legacy
                            // unsupported poses still fail closed.
                            var distance = saved.items[index].needsGravitySettling
                                ? Mathf.Clamp(body.Pose.position.y - Navigation.FloorHeight + .2f, .13f, 4f)
                                : .13f;
                            if (body.HasSupport(accepted, distance)) accepted.Add(body);
                        }
                    if (accepted.Count != spawned.Count)
                        throw new InvalidOperationException("旧物品缺少可靠支撑，已保留原存档。");
                    if (spawned.Count > 0 && avatarMotor != null && !avatarMotor.TryPlaceAfterRestore(Navigation))
                        throw new InvalidOperationException("恢复布局后没有安全的角色出生点，已保留原存档。");
                    foreach (var view in spawned) view.SetPaused(false);
                    itemsRestored = true;
                    StoreRoomPose(roomPose);
                    Notice = usedFallbackPose ? "空间锚点暂不可用，已按当前房间恢复物品。" : null;
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    itemsRestored = false;
                    itemRestoreFailed = true;
                    Notice = error.Message;
                    Debug.LogWarning($"[GameSession] {Notice}");
                }
                finally
                {
                    if (!itemsRestored)
                    {
                        foreach (var view in spawned)
                        {
                            views.Remove(view.Instance.Id);
                            Items.Remove(view.Instance.Id);
                            view.Retire();
                            UnityEngine.Object.Destroy(view.gameObject);
                        }
                        itemRoot.SetPositionAndRotation(previousRoot.position, previousRoot.rotation);
                    }
                    itemOperation = null;
                    if (!disposed) UpdateAvatarAvailability();
                }
            }
        }

        public async Task PlaceItemAsync(string definitionId, Pose pose)
        {
            if (Paused || disposed) return;
            if (itemOperation != null) { Notice = "物品定位处理中，请稍候。"; return; }
            if (!itemsRestored && !itemRestoreFailed)
            {
                Notice = "房间物品仍在恢复，请稍候。";
                return;
            }
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                itemOperation = operation;
                var previousSave = Clone(saved);
                var previouslyRestored = itemsRestored;
                var previouslyFailed = itemRestoreFailed;
                var committed = false;
                var spawned = new List<GameItem>();
                var previousRoot = new Pose(itemRoot.position, itemRoot.rotation);
                var previousAnchor = saved.roomAnchorId;
                try
                {
                    var definition = definitions[definitionId];
                    if (!TryResolvePlacement(definition, pose, null, out var placement))
                    {
                        Notice = $"附近没有可放置{definition.DisplayName}的空位。";
                        return;
                    }
                    var startsNewLayout = itemRestoreFailed;
                    if (startsNewLayout)
                    {
                        saved.roomAnchorId = null;
                        saved.items = Array.Empty<ItemSaveData>();
                        saved.hasRoomAnchorPose = false;
                        itemsRestored = true;
                        itemRestoreFailed = false;
                    }
                    if (string.IsNullOrEmpty(saved.roomAnchorId))
                    {
                        var anchorPose = views.Count == 0 ? placement : previousRoot;
                        saved.roomAnchorId = await anchors.CreateAsync(anchorPose, operation.Token);
                        operation.Token.ThrowIfCancellationRequested();
                        itemRoot.SetPositionAndRotation(anchorPose.position, anchorPose.rotation);
                        StoreRoomPose(anchorPose);
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    // Anchor creation can span frames; recheck before committing into a changed room.
                    Physics.SyncTransforms();
                    if (!IsPlacementFree(definition, placement.position, placement.rotation, null))
                        throw new InvalidOperationException("放置区域已被占用，请重试。");
                    if (definition.IsPlacementGroup)
                    {
                        foreach (var part in definition.PlacementParts)
                        {
                            if (!definitions.ContainsKey(part.Definition.Id))
                                throw new InvalidOperationException(
                                    $"组合物品成员未注册：{part.Definition.Id}");
                            var partPosition = placement.position +
                                               placement.rotation * part.LocalPosition;
                            var partRotation = placement.rotation * part.LocalRotation;
                            spawned.Add(Spawn(part.Definition, Guid.NewGuid().ToString(),
                                itemRoot.InverseTransformPoint(partPosition),
                                Quaternion.Inverse(itemRoot.rotation) * partRotation, "default"));
                        }
                    }
                    else
                    {
                        spawned.Add(Spawn(definition, Guid.NewGuid().ToString(),
                            itemRoot.InverseTransformPoint(placement.position),
                            Quaternion.Inverse(itemRoot.rotation) * placement.rotation, "default"));
                    }
                    Save();
                    committed = true;
                    Notice = startsNewLayout
                        ? $"旧物品定位已失效，已创建新布局并放置{definition.DisplayName}。"
                        : $"{definition.DisplayName}已放置。";
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { Notice = error.Message; }
                finally
                {
                    if (!committed)
                    {
                        foreach (var view in spawned)
                        {
                            views.Remove(view.Instance.Id);
                            Items.Remove(view.Instance.Id);
                            view.Retire();
                            UnityEngine.Object.Destroy(view.gameObject);
                        }
                        saved.roomAnchorId = previousAnchor;
                        itemRoot.SetPositionAndRotation(previousRoot.position, previousRoot.rotation);
                    }
                    if (!committed)
                    {
                        saved = previousSave;
                        itemsRestored = previouslyRestored;
                        itemRestoreFailed = previouslyFailed;
                    }
                    itemOperation = null;
                }
            }
        }

        public bool TryResolvePlacement(ItemDefinition definition, Pose desired, GameItem ignoredItem,
            out Pose placement)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            PhysicsBody.ValidatePose(desired);
            Physics.SyncTransforms();
            var rotation = Quaternion.Euler(0, desired.rotation.eulerAngles.y, 0);
            const float step = .25f;
            const int directions = 16;
            for (var ring = 0; ring <= 8; ring++)
            {
                var count = ring == 0 ? 1 : directions;
                for (var index = 0; index < count; index++)
                {
                    var angle = index * Mathf.PI * 2f / count;
                    var offset = ring == 0 ? Vector3.zero :
                        new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * step);
                    var candidate = desired.position + offset;
                    if (definition.AllowSurfacePlacement &&
                        TrySurface(candidate, ignoredItem, out var surface) &&
                        IsPlacementFree(definition, surface, rotation, ignoredItem))
                    {
                        placement = new Pose(surface, rotation);
                        return true;
                    }
                    candidate.y = Navigation.FloorHeight;
                    if (!Navigation.TrySample(candidate, .3f, out var floor)) continue;
                    if (Mathf.Abs(floor.y - Navigation.FloorHeight) > .1f ||
                        Vector3.Distance(candidate, floor) > .3f) continue;
                    var mainFloor = new Vector3(floor.x, Navigation.FloorHeight, floor.z);
                    if (!IsPlacementFree(definition, mainFloor, rotation, ignoredItem)) continue;
                    placement = new Pose(mainFloor, rotation);
                    return true;
                }
            }
            placement = default;
            return false;
        }

        private bool TrySurface(Vector3 candidate, GameItem ignored, out Vector3 surface)
        {
            var origin = candidate + Vector3.up * .25f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, placementHits,
                Mathf.Max(.3f, origin.y - Navigation.FloorHeight + .1f),
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            var closest = float.MaxValue;
            surface = default;
            if (count == placementHits.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var hit = placementHits[index];
                if (hit.normal.y < .95f || hit.distance >= closest ||
                    hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                    ignored != null && hit.collider.transform.IsChildOf(ignored.transform)) continue;
                closest = hit.distance;
                surface = hit.point;
            }
            return closest < float.MaxValue;
        }

        private bool IsPlacementFree(ItemDefinition definition, Vector3 floor,
            Quaternion rotation, GameItem ignoredItem, Transform ignoredRoot = null)
        {
            const float clearance = .03f;
            var halfHeight = Mathf.Max(.05f, definition.PlacementHeight * .5f - clearance);
            var center = floor + Vector3.up * (halfHeight + clearance * 2f);
            var halfExtents = new Vector3(definition.Footprint.x * .5f + clearance, halfHeight,
                definition.Footprint.y * .5f + clearance);
            var count = Physics.OverlapBoxNonAlloc(center, halfExtents, placementOverlaps, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == placementOverlaps.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var collider = placementOverlaps[index];
                if (ignoredItem != null && collider.transform.IsChildOf(ignoredItem.transform)) continue;
                if (ignoredRoot != null && collider.transform.IsChildOf(ignoredRoot)) continue;
                return false;
            }
            return true;
        }

        private bool TryGetSavedRoomPose(out Pose pose)
        {
            pose = default;
            if (!saved.hasRoomAnchorPose) return false;
            pose = new Pose(new Vector3(saved.roomX, saved.roomY, saved.roomZ),
                new Quaternion(saved.roomQx, saved.roomQy, saved.roomQz, saved.roomQw));
            return true;
        }

        private void StoreRoomPose(Pose pose)
        {
            saved.hasRoomAnchorPose = true;
            saved.roomX = pose.position.x;
            saved.roomY = pose.position.y;
            saved.roomZ = pose.position.z;
            saved.roomQx = pose.rotation.x;
            saved.roomQy = pose.rotation.y;
            saved.roomQz = pose.rotation.z;
            saved.roomQw = pose.rotation.w;
        }

        private static GameSaveData Clone(GameSaveData data)
            => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));

        private GameItem Spawn(ItemDefinition definition, string id, Vector3 localPosition,
            Quaternion localRotation, string stateId)
        {
            var root = UnityEngine.Object.Instantiate(definition.Prefab, itemRoot);
            try
            {
                root.transform.SetLocalPositionAndRotation(localPosition, localRotation.normalized);
                var view = root.AddComponent<GameItem>();
                view.Bind(definition, Items, this, id, stateId);
                views.Add(id, view);
                return view;
            }
            catch
            {
                Items.Remove(id);
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        public void RemoveItem(string id)
        {
            if (!TryRemoveItem(id, out var view)) return;
            Notice = $"{view.Instance.DisplayName}已删除。";
#region debug-point E:delete-persist
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var deletePayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                $"\"hypothesisId\":\"E\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                $"\"location\":\"GameSession.RemoveItem\",\"msg\":\"[DEBUG] item removed and saved\"," +
                                $"\"data\":{{\"id\":\"{id}\",\"registryCount\":{Items.Count}," +
                                $"\"viewCount\":{views.Count},\"savedCount\":{saved.items.Length}" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", deletePayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        internal bool BreakItem(string id, BreakSeverity severity)
        {
            if (!TryRemoveItem(id, out var view)) return false;
            Notice = severity == BreakSeverity.Heavy
                ? $"{view.Instance.DisplayName}受到猛烈撞击，碎成了许多小碎片。"
                : $"{view.Instance.DisplayName}受到撞击，裂成了几块。";
            return true;
        }

        private bool TryRemoveItem(string id, out GameItem view)
        {
            view = null;
            if (Paused || disposed || !views.TryGetValue(id, out view)) return false;
            Items.Remove(id); // Invalidates a running action's reservation in this frame.
            views.Remove(id);
            view.Retire();
            UnityEngine.Object.Destroy(view.gameObject);
            Save();
            return true;
        }

        internal void RegisterDebris(BreakableDebris value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (disposed) throw new ObjectDisposedException(nameof(GameSession));
            debris.Add(value);
            if (Paused) value.SetPaused(true);
        }

        internal void UnregisterDebris(BreakableDebris value)
        {
            if (value != null) debris.Remove(value);
        }

        public void SetPaused(bool paused)
        {
            if (Paused == paused || disposed) return;
            Paused = paused;
            if (paused)
            {
                itemOperation?.Cancel();
                Conversation.Cancel();
                Avatar.StopAll(ActionStopReason.Paused);
                Chairs?.Invalidate();
                Beds?.Invalidate();
                foreach (var view in views.Values) view.SetPaused(true);
                foreach (var value in debris) value.SetPaused(true);
                UpdateAvatarAvailability();
                Save();
            }
            else
            {
                foreach (var view in views.Values) view.SetPaused(false);
                foreach (var value in debris) value.SetPaused(false);
                UpdateAvatarAvailability();
                RealityMesh?.RefreshCached();
            }
        }

        private void UpdateAvatarAvailability()
        {
            var unavailable = Paused || !itemsRestored && !itemRestoreFailed;
            Avatar.Suspended = unavailable;
            if (Motor is AvatarMotor motor) motor.Body.SetPaused(unavailable);
        }

        public void Save()
        {
            physicsSaveDelay = -1;
            Physics.SyncTransforms();
            saved.outfitId = wardrobe.CurrentOutfitId;
            saved.subtitles = Subtitles;
            saved.personality = Persona.CaptureSelection();
            saved.mood = Mood.Capture();
            // A missing anchor must not overwrite recoverable items with an empty list.
            if (itemsRestored)
            {
                saved.items = views.Values.Where(v => v != null).Select(v => v.Capture(itemRoot)).ToArray();
                if (!string.IsNullOrEmpty(saved.roomAnchorId))
                    StoreRoomPose(new Pose(itemRoot.position, itemRoot.rotation));
            }
            store.Save(saved);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            Conversation.Changed -= OnConversationChanged;
            Conversation.Dispose();
            characterContext.Dispose();
            Avatar.Dispose();
            Chairs?.Dispose();
            Beds?.Dispose();
            RealityMesh?.Dispose();
            RealityItems.Dispose();
            anchors.Dispose();
            foreach (var view in views.Values)
            {
                if (view == null) continue;
                view.Retire();
                UnityEngine.Object.Destroy(view.gameObject);
            }
            views.Clear();
            foreach (var value in debris)
                if (value != null) UnityEngine.Object.Destroy(value.gameObject);
            debris.Clear();
            Items.Dispose();
            lifetime.Dispose();
        }
    }
}
