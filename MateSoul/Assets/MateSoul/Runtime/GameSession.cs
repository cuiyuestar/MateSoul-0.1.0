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
    public sealed partial class GameSession : IDisposable
    {
        private readonly Dictionary<string, ItemDefinition> definitions;
        private readonly Dictionary<string, GameItem> views = new Dictionary<string, GameItem>();
        private readonly Dictionary<string, int> inventory = new Dictionary<string, int>();
        private readonly Dictionary<string, string> inventoryOwners =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<BreakableDebris> debris = new HashSet<BreakableDebris>();
        private readonly IGameSaveStore store;
        private readonly ISpatialAnchorService anchors;
        private readonly Transform itemRoot;
        private readonly LengyueWardrobeController wardrobe;
        private readonly CharacterRuntimeContextProvider characterContext;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Func<DateTime> localNow;
        private CancellationTokenSource itemOperation;
        private GameSaveData saved;
        private readonly GameSaveData loadedSave;
        private bool itemsRestored, itemRestoreFailed, disposed;
        private float physicsSaveDelay = -1;
        private readonly Collider[] placementOverlaps = new Collider[64];
        private readonly RaycastHit[] placementHits = new RaycastHit[32];
        private const float SocialAttentionEnterDistance = .8f;
        private const float SocialAttentionExitDistance = 1.05f;
        private const float SocialAttentionCandidateSeconds = .7f;
        private const float SocialAttentionSettledSeconds = .25f;
        private const float SocialAttentionExitDwellSeconds = .25f;
        private const float AmbientAttentionMaximumSeconds = 5.5f;
        private const float AmbientAttentionCooldownSeconds = 5f;
        private const float PlayerApproachEvidenceDistance = .15f;
        private const float ApproachEvidenceAdvantage = .08f;
        private const float ApproachEvidenceWindowSeconds = 1.2f;
        private Transform playerHead;
        private AvatarSocialAttentionRig socialAttentionRig;
        private ActionHandle socialAttentionAction;
        private bool dialogueAttentionRequested;
        private bool playerNearby;
        private bool socialAttentionCandidate;
        private bool proximityAttentionArmed = true;
        private bool hasApproachSample;
        private Vector3 previousPlayerPosition;
        private Vector3 previousAvatarPosition;
        private float playerApproachEvidence;
        private float avatarApproachEvidence;
        private float socialAttentionCandidateElapsed;
        private float socialAttentionSettledElapsed;
        private float socialAttentionExitElapsed;
        private float ambientAttentionElapsed;
        private float ambientAttentionCooldown;
        public ActionCatalog Catalog { get; } = new ActionCatalog();
        public AvatarActionCoordinator Avatar { get; }
        public ItemRegistry Items { get; } = new ItemRegistry();
        public RealityItemDirectory RealityItems { get; }
        public MoodSystem Mood { get; } = new MoodSystem();
        public CharacterPersonaState Persona { get; }
        public ICharacterContextProvider CharacterContext => characterContext;
        public ConversationOrchestrator Conversation { get; }
        public SpontaneousReplyCoordinator SpontaneousReplies { get; }
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
        public bool DialogueAttentionRequested => dialogueAttentionRequested;
        public bool PlayerNearby => playerNearby;
        public bool SocialAttentionCandidate => socialAttentionCandidate;
        public bool SocialAttentionActive =>
            socialAttentionAction != null && !socialAttentionAction.IsFinished;

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

        public void ConfigureSocialAttention(AvatarSocialAttentionRig rig, Transform head)
        {
            if (rig == null || !rig.IsConfigured)
                throw new InvalidOperationException("角色缺少社交注视配置。");
            socialAttentionRig = rig;
            playerHead = head;
            hasApproachSample = false;
            socialAttentionCandidate = false;
            proximityAttentionArmed = true;
            Catalog.Register(new ActionDefinition(SocialAttentionAction.Id,
                "Stop and attend to the nearby player",
                ControlChannels.All,
                intent => new SocialAttentionAction(
                    (AvatarMotor)Motor, socialAttentionRig, intent.Payload as Transform),
                timeoutSeconds: 3600, exposeToAI: false, exitTimeoutSeconds: 1));
        }

        internal void SetItemActionAttention(Transform target, bool enabled)
        {
            if (socialAttentionRig == null || !socialAttentionRig.IsConfigured)
                return;
            var motor = Motor as AvatarMotor;
            if (enabled && target != null)
            {
                socialAttentionRig.Begin(target);
                motor?.SetSocialAttention(true);
            }
            else
            {
                socialAttentionRig.End(false);
                motor?.SetSocialAttention(false);
            }
        }

        public void SetDialogueAttention(bool requested)
        {
            dialogueAttentionRequested = requested;
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
            SystemNotifications notifications = null, Func<DateTime> localNow = null,
            ISpontaneousReplyProvider spontaneousReplyProvider = null)
        {
            Notifications = notifications ?? new SystemNotifications();
            Items.CleanupFailed += Debug.LogException;
            Motor = motor; Navigation = navigation; this.wardrobe = wardrobe;
            this.localNow = localNow ?? (() => DateTime.Now);
            RealityItems = new RealityItemDirectory(Items);
            this.definitions = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
            CatalogItems = this.definitions.Values
                .Where(d => d.CatalogVisible &&
                            (d.InventoryLimit > 0 || d.UnlimitedInventory)).ToArray();
            ConfigureInventoryOwners();
            this.itemRoot = itemRoot; this.store = store; this.anchors = anchors;
            saved = store.Load();
            loadedSave = Clone(saved);
            InitializeInventory();
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
            SpontaneousReplies = new SpontaneousReplyCoordinator(
                spontaneousReplyProvider, Persona);
            if (!string.IsNullOrEmpty(saved.outfitId))
            {
                var restore = Avatar.Submit(new AvatarIntent("outfit", IntentSource.System, payload: saved.outfitId));
                Avatar.Tick(0);
                if (restore.State != ActionState.Succeeded)
                    Notice = $"服装恢复失败：{restore.Error}";
            }
            UpdateAvatarAvailability();
        }

        private void ConfigureInventoryOwners()
        {
            foreach (var definition in definitions.Values)
                if (definition.CatalogVisible &&
                    (definition.InventoryLimit > 0 || definition.UnlimitedInventory))
                    inventoryOwners.Add(definition.Id, definition.Id);
            foreach (var definition in definitions.Values)
            {
                if (!definition.IsPlacementGroup) continue;
                foreach (var part in definition.PlacementParts)
                {
                    if (inventoryOwners.TryGetValue(part.Definition.Id, out var owner) &&
                        owner != definition.Id)
                        throw new InvalidOperationException(
                            $"{part.Definition.DisplayName}属于多个库存物品。");
                    inventoryOwners[part.Definition.Id] = definition.Id;
                }
            }
        }

        private void InitializeInventory()
        {
            if (saved.inventory == null) saved.inventory = Array.Empty<ItemInventorySaveData>();
            NormalizeSavedInventoryOwnership();
            var persisted = saved.inventory.ToDictionary(
                entry => entry.definitionId, entry => entry.remaining, StringComparer.Ordinal);
            inventory.Clear();
            foreach (var definition in CatalogItems)
            {
                if (definition.UnlimitedInventory) continue;
                var placed = CountSavedInventoryUnits(definition.Id);
                var maximumRemaining = Mathf.Max(0, definition.InventoryLimit - placed);
                inventory[definition.Id] = persisted.TryGetValue(definition.Id, out var remaining)
                    ? Mathf.Clamp(remaining, 0, maximumRemaining)
                    : maximumRemaining;
            }
            RefreshDailyInventory();
            CaptureInventory();
        }

        private void NormalizeSavedInventoryOwnership()
        {
            foreach (var item in saved.items ?? Array.Empty<ItemSaveData>())
                if (string.IsNullOrEmpty(item.inventoryDefinitionId))
                    item.inventoryDefinitionId = ResolveInventoryOwner(item.definitionId);

            foreach (var group in CatalogItems.Where(item => item.IsPlacementGroup))
            {
                var pending = saved.items.Where(item =>
                    item.inventoryDefinitionId == group.Id &&
                    string.IsNullOrEmpty(item.inventoryUnitId)).ToList();
                var partCounts = group.PlacementParts
                    .GroupBy(part => part.Definition.Id, StringComparer.Ordinal)
                    .ToDictionary(values => values.Key, values => values.Count(),
                        StringComparer.Ordinal);
                while (pending.Count > 0)
                {
                    var seed = pending[0];
                    pending.RemoveAt(0);
                    var unitId = "legacy-" + seed.instanceId;
                    seed.inventoryUnitId = unitId;
                    var needed = new Dictionary<string, int>(partCounts, StringComparer.Ordinal);
                    if (needed.ContainsKey(seed.definitionId)) needed[seed.definitionId]--;
                    foreach (var entry in needed.Where(entry => entry.Value > 0).ToArray())
                        for (var count = 0; count < entry.Value; count++)
                        {
                            var match = pending.Where(item => item.definitionId == entry.Key)
                                .OrderBy(item => SavedDistanceSquared(seed, item))
                                .FirstOrDefault();
                            if (match == null) break;
                            match.inventoryUnitId = unitId;
                            pending.Remove(match);
                        }
                }
            }

            foreach (var item in saved.items ?? Array.Empty<ItemSaveData>())
                if (string.IsNullOrEmpty(item.inventoryUnitId))
                    item.inventoryUnitId = item.instanceId;
        }

        private static float SavedDistanceSquared(ItemSaveData first, ItemSaveData second)
        {
            var dx = first.x - second.x;
            var dy = first.y - second.y;
            var dz = first.z - second.z;
            return dx * dx + dy * dy + dz * dz;
        }

        private int CountSavedInventoryUnits(string inventoryDefinitionId)
        {
            var units = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in saved.items ?? Array.Empty<ItemSaveData>())
            {
                var owner = ResolveInventoryOwner(item.definitionId, item.inventoryDefinitionId);
                if (owner != inventoryDefinitionId) continue;
                units.Add(string.IsNullOrEmpty(item.inventoryUnitId)
                    ? item.instanceId : item.inventoryUnitId);
            }
            return units.Count;
        }

        private void ReturnSavedInventory(IEnumerable<ItemSaveData> items)
        {
            var returned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items ?? Array.Empty<ItemSaveData>())
            {
                var owner = ResolveInventoryOwner(item.definitionId, item.inventoryDefinitionId);
                if (!inventory.TryGetValue(owner, out var remaining)) continue;
                var unit = string.IsNullOrEmpty(item.inventoryUnitId)
                    ? item.instanceId : item.inventoryUnitId;
                if (!returned.Add(owner + "\n" + unit)) continue;
                inventory[owner] = Mathf.Min(definitions[owner].InventoryLimit, remaining + 1);
            }
        }

        private string ResolveInventoryOwner(string definitionId, string savedOwner = null)
        {
            if (!string.IsNullOrEmpty(savedOwner))
            {
                if (!definitions.TryGetValue(savedOwner, out var savedDefinition) ||
                    savedDefinition.InventoryLimit <= 0 && !savedDefinition.UnlimitedInventory &&
                    !savedDefinition.ConsumedProduct)
                    throw new InvalidOperationException($"物品库存定义不存在：{savedOwner}");
                if (inventoryOwners.TryGetValue(definitionId, out var expectedOwner) &&
                    expectedOwner != savedOwner)
                    throw new InvalidOperationException(
                        $"{definitionId}不能归入库存 {savedOwner}。");
                return savedOwner;
            }
            return inventoryOwners.TryGetValue(definitionId, out var owner) ? owner : definitionId;
        }

        private bool RefreshDailyInventory()
        {
            var today = localNow().Date.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
            if (saved.dailyInventoryDate == today) return false;
            foreach (var definition in CatalogItems.Where(item => item.RestockDaily))
                inventory[definition.Id] = definition.InventoryLimit;
            saved.dailyInventoryDate = today;
            return true;
        }

        private void CaptureInventory()
        {
            saved.inventory = CatalogItems.Where(definition => !definition.UnlimitedInventory)
                .Select(definition => new ItemInventorySaveData
            {
                definitionId = definition.Id,
                remaining = inventory.TryGetValue(definition.Id, out var remaining) ? remaining : 0
            }).ToArray();
        }

        public int GetRemainingQuantity(string definitionId)
        {
            RefreshDailyInventoryIfNeeded();
            if (definitions.TryGetValue(definitionId, out var definition) &&
                definition.UnlimitedInventory)
                return int.MaxValue;
            return inventory.TryGetValue(definitionId, out var remaining) ? remaining : 0;
        }

        private void RefreshDailyInventoryIfNeeded()
        {
            if (!RefreshDailyInventory()) return;
            CaptureInventory();
            store.Save(saved);
            Notice = "苹果库存已按今日额度刷新。";
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
            Catalog.Register(new ActionDefinition(CutFruitAction.Id,
                "Collect fruit, knife and plate, cut fruit at the dining table and serve it",
                body | ControlChannels.LeftArm | ControlChannels.RightArm,
                _ => new CutFruitAction(this), timeoutSeconds: 240, exitTimeoutSeconds: 1));
        }

        public ActionHandle Submit(AvatarIntent intent)
        {
            if (Paused || disposed) throw new InvalidOperationException("会话未运行。");
            var action = Avatar.Submit(intent);
            if (action.State == ActionState.Rejected) Notice = action.Error;
            if (intent.ActionId == CutFruitAction.Id) observedItemAction = action;
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
            SpontaneousReplies.Tick(seconds);
            RefreshDailyInventoryIfNeeded();
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
            UpdateSocialAttention(seconds);
            ReportItemAction();
            Items.Tick(Time.unscaledTimeAsDouble);
            Mood.Tick(seconds, Avatar.Leases.IsOccupied(ControlChannels.Body));
            if (!socialAttentionCandidate && !dialogueAttentionRequested)
                Autonomy.Tick(seconds);
            if (physicsSaveDelay >= 0)
            {
                physicsSaveDelay -= seconds;
                if (physicsSaveDelay <= 0) { physicsSaveDelay = -1; Save(); }
            }
        }

        private void UpdateSocialAttention(float seconds)
        {
            if (socialAttentionRig == null || playerHead == null) return;
            seconds = Mathf.Max(0, seconds);
            var separation = Vector3.ProjectOnPlane(
                playerHead.position - Motor.Position, Vector3.up).magnitude;
            SampleApproachEvidence(seconds);
            ambientAttentionCooldown = Mathf.Max(
                0, ambientAttentionCooldown - seconds);

            if (separation <= SocialAttentionEnterDistance)
            {
                playerNearby = true;
                socialAttentionExitElapsed = 0;
            }
            else if (playerNearby && separation > SocialAttentionExitDistance)
            {
                socialAttentionExitElapsed += seconds;
                if (socialAttentionExitElapsed >= SocialAttentionExitDwellSeconds)
                {
                    playerNearby = false;
                    socialAttentionExitElapsed = 0;
                    proximityAttentionArmed = true;
                    socialAttentionCandidate = false;
                    socialAttentionCandidateElapsed = 0;
                    playerApproachEvidence = avatarApproachEvidence = 0;
                }
            }
            else
            {
                socialAttentionExitElapsed = 0;
            }

            var motor = Motor as AvatarMotor;
            var settled = Avatar.IsIdle && motor != null && !motor.IsMoving &&
                          Vector3.ProjectOnPlane(
                              motor.Body.Rigidbody.velocity, Vector3.up).magnitude < .05f;
            socialAttentionSettledElapsed = settled
                ? socialAttentionSettledElapsed + seconds : 0;

            if (socialAttentionAction != null &&
                !socialAttentionAction.IsFinished)
            {
                if (dialogueAttentionRequested)
                {
                    ambientAttentionElapsed = 0;
                    return;
                }
                ambientAttentionElapsed += seconds;
                if (!playerNearby ||
                    ambientAttentionElapsed >= AmbientAttentionMaximumSeconds)
                {
                    Avatar.Cancel(socialAttentionAction);
                    if (ambientAttentionElapsed >= AmbientAttentionMaximumSeconds)
                    {
                        proximityAttentionArmed = false;
                        ambientAttentionCooldown = AmbientAttentionCooldownSeconds;
                    }
                }
                return;
            }

            ambientAttentionElapsed = 0;
            if (dialogueAttentionRequested)
            {
                socialAttentionCandidate = true;
                if (socialAttentionSettledElapsed < SocialAttentionSettledSeconds)
                    return;
                SubmitSocialAttention();
                return;
            }

            if (separation > SocialAttentionEnterDistance ||
                !proximityAttentionArmed || ambientAttentionCooldown > 0)
            {
                socialAttentionCandidate = false;
                socialAttentionCandidateElapsed = 0;
                return;
            }

            if (socialAttentionCandidate &&
                avatarApproachEvidence >=
                playerApproachEvidence + ApproachEvidenceAdvantage)
            {
                socialAttentionCandidate = false;
                socialAttentionCandidateElapsed = 0;
            }
            if (!socialAttentionCandidate &&
                playerApproachEvidence >= PlayerApproachEvidenceDistance &&
                playerApproachEvidence >=
                avatarApproachEvidence + ApproachEvidenceAdvantage)
                socialAttentionCandidate = true;
            if (!socialAttentionCandidate) return;

            socialAttentionCandidateElapsed += seconds;
            if (socialAttentionCandidateElapsed < SocialAttentionCandidateSeconds ||
                socialAttentionSettledElapsed < SocialAttentionSettledSeconds)
                return;
            SubmitSocialAttention();
        }

        private void SubmitSocialAttention()
        {
            if (!Avatar.IsIdle) return;
            socialAttentionAction = Avatar.Submit(new AvatarIntent(
                SocialAttentionAction.Id, IntentSource.Ambient, payload: playerHead));
            if (!socialAttentionAction.IsFinished)
            {
                socialAttentionCandidate = false;
                socialAttentionCandidateElapsed = 0;
                socialAttentionSettledElapsed = 0;
            }
        }

        private void SampleApproachEvidence(float seconds)
        {
            var playerPosition = Vector3.ProjectOnPlane(
                playerHead.position, Vector3.up);
            var avatarPosition = Vector3.ProjectOnPlane(
                Motor.Position, Vector3.up);
            if (!hasApproachSample)
            {
                previousPlayerPosition = playerPosition;
                previousAvatarPosition = avatarPosition;
                hasApproachSample = true;
                return;
            }

            var retention = Mathf.Exp(
                -seconds / ApproachEvidenceWindowSeconds);
            playerApproachEvidence *= retention;
            avatarApproachEvidence *= retention;
            var previousSeparation = previousAvatarPosition - previousPlayerPosition;
            if (previousSeparation.sqrMagnitude > .0001f)
            {
                var towardAvatar = previousSeparation.normalized;
                playerApproachEvidence += Mathf.Max(0,
                    Vector3.Dot(playerPosition - previousPlayerPosition, towardAvatar));
                avatarApproachEvidence += Mathf.Max(0,
                    Vector3.Dot(avatarPosition - previousAvatarPosition, -towardAvatar));
            }
            previousPlayerPosition = playerPosition;
            previousAvatarPosition = avatarPosition;
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
                var previousSave = Clone(loadedSave);
                var previousInventory =
                    new Dictionary<string, int>(inventory, StringComparer.Ordinal);
                var spawned = new List<GameItem>();
                var activeSavedItems = new List<ItemSaveData>();
                var recoveredItems = new List<ItemSaveData>();
                try
                {
                    itemRestoreFailed = false;
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in saved.items)
                    {
                        if (!definitions.ContainsKey(item.definitionId))
                            throw new InvalidOperationException($"物品定义不存在：{item.definitionId}");
                        ResolveInventoryOwner(item.definitionId, item.inventoryDefinitionId);
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
                            new Quaternion(item.qx, item.qy, item.qz, item.qw), item.stateId,
                            ResolveInventoryOwner(item.definitionId, item.inventoryDefinitionId),
                            string.IsNullOrEmpty(item.inventoryUnitId)
                                ? item.instanceId : item.inventoryUnitId,
                            item.cutState);
                        view.SetPaused(true);
                        spawned.Add(view);
                        activeSavedItems.Add(item);
                    }
                    Physics.SyncTransforms();
                    var accepted = new HashSet<PhysicsBody>();
                    var rejectedUnits = new HashSet<string>(StringComparer.Ordinal);
                    var avatarMotor = Motor as AvatarMotor;
                    for (var index = 0; index < spawned.Count; index++)
                    {
                        var view = spawned[index];
                        Collider placementSupport = null;
                        if (view.Definition.PlacementSurface == ItemPlacementSurface.Wall &&
                            !TryGetWallSupport(
                                view.Definition, view.Body.Pose, view, out placementSupport))
                            throw new InvalidOperationException(
                                $"旧{view.Definition.DisplayName}缺少可靠墙面支撑，已保留原存档。");
                        if (SupportsHorizontalPlacement(view.Definition))
                            TryGetHorizontalSupport(
                                view.Definition, view.Body.Pose, view,
                                out placementSupport, out _);
                        Collider blockingCollider = null;
                        var canOccupy = view.Body.CanOccupy(view.Body.Pose,
                            ignoredRoot: avatarMotor?.transform,
                            ignoredCollider: placementSupport,
                            ignoredColliderRoot: GetVirtualSupportRoot(placementSupport),
                            blocked: (collider, _) => blockingCollider = collider);
                        if (!canOccupy)
                        {
                            if (blockingCollider == null ||
                                blockingCollider.GetComponentInParent<GameItem>() != null)
                                throw new InvalidOperationException(
                                    "旧物品位置与其他虚拟物品相交，已保留原存档。");
                            rejectedUnits.Add(InventoryUnitKey(activeSavedItems[index]));
                        }
                    }
                    if (rejectedUnits.Count > 0)
                    {
                        for (var index = spawned.Count - 1; index >= 0; index--)
                        {
                            if (!rejectedUnits.Contains(
                                    InventoryUnitKey(activeSavedItems[index])))
                                continue;
                            var view = spawned[index];
                            recoveredItems.Add(activeSavedItems[index]);
                            views.Remove(view.Instance.Id);
                            Items.Remove(view.Instance.Id);
                            view.Retire();
                            view.gameObject.SetActive(false);
                            UnityEngine.Object.Destroy(view.gameObject);
                            spawned.RemoveAt(index);
                            activeSavedItems.RemoveAt(index);
                        }
                        ReturnSavedInventory(recoveredItems);
                        saved.items = activeSavedItems.ToArray();
                        Physics.SyncTransforms();
                    }
                    // Support graph must reach the static room, independent of save ordering.
                    for (var pass = 0; pass < spawned.Count; pass++)
                        for (var index = 0; index < spawned.Count; index++)
                        {
                            var body = spawned[index].Body;
                            if (accepted.Contains(body)) continue;
                            if (spawned[index].Definition.PlacementSurface ==
                                ItemPlacementSurface.Wall)
                            {
                                accepted.Add(body);
                                continue;
                            }
                            // Interrupted falls may resume above known support; legacy
                            // unsupported poses still fail closed.
                            var distance = activeSavedItems[index].needsGravitySettling
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
                    if (recoveredItems.Count > 0)
                    {
                        CaptureInventory();
                        store.Save(saved);
                    }
                    Notice = recoveredItems.Count > 0
                        ? $"有{rejectedUnits.Count}件旧物品与当前房间相交，已回收到背包；其余物品已恢复。"
                        : usedFallbackPose
                            ? "空间锚点暂不可用，已按当前房间恢复物品。"
                            : null;
                }
                catch (OperationCanceledException)
                {
                    saved = previousSave;
                    inventory.Clear();
                    foreach (var entry in previousInventory)
                        inventory.Add(entry.Key, entry.Value);
                }
                catch (Exception error)
                {
                    saved = previousSave;
                    inventory.Clear();
                    foreach (var entry in previousInventory)
                        inventory.Add(entry.Key, entry.Value);
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

        private string InventoryUnitKey(ItemSaveData item)
        {
            var owner = ResolveInventoryOwner(
                item.definitionId, item.inventoryDefinitionId);
            var unit = string.IsNullOrEmpty(item.inventoryUnitId)
                ? item.instanceId
                : item.inventoryUnitId;
            return owner + "\n" + unit;
        }

        public async Task PlaceItemAsync(string definitionId, Pose pose, bool searchNearby = true)
        {
            if (Paused || disposed) return;
            if (itemOperation != null) { Notice = "物品定位处理中，请稍候。"; return; }
            if (!itemsRestored && !itemRestoreFailed)
            {
                Notice = "房间物品仍在恢复，请稍候。";
                return;
            }
            RefreshDailyInventoryIfNeeded();
            var remaining = 0;
            if (!definitions.TryGetValue(definitionId, out var requestedDefinition) ||
                !requestedDefinition.CatalogVisible ||
                !requestedDefinition.UnlimitedInventory &&
                !inventory.TryGetValue(definitionId, out remaining))
                throw new ArgumentException($"物品不在虚拟物品栏中：{definitionId}");
            if (!requestedDefinition.UnlimitedInventory && remaining <= 0)
            {
                Notice = $"{requestedDefinition.DisplayName}已全部放置，请先回收已有物品。";
                return;
            }
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                itemOperation = operation;
                var previousSave = Clone(saved);
                var previousInventory = new Dictionary<string, int>(inventory, StringComparer.Ordinal);
                var previouslyRestored = itemsRestored;
                var previouslyFailed = itemRestoreFailed;
                var committed = false;
                var spawned = new List<GameItem>();
                var previousRoot = new Pose(itemRoot.position, itemRoot.rotation);
                var previousAnchor = saved.roomAnchorId;
                try
                {
                    var definition = requestedDefinition;
                    Pose placement;
                    var resolved = searchNearby
                        ? TryResolvePlacement(definition, pose, null, out placement)
                        : TryResolveExactPlacement(definition, pose, null, out placement);
                    if (!resolved)
                    {
                        Notice = searchNearby
                            ? $"附近没有可放置{definition.DisplayName}的空位。"
                            : $"{definition.DisplayName}投影范围被遮挡，无法放置。";
                        return;
                    }
                    var startsNewLayout = itemRestoreFailed;
                    if (startsNewLayout)
                    {
                        ReturnSavedInventory(saved.items);
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
                    var ignoredRoot = definition.PlacementPreview
                        ? (Motor as AvatarMotor)?.transform : null;
                    Collider placementSupport = null;
                    if (definition.PlacementSurface == ItemPlacementSurface.Wall &&
                        !TryGetWallSupport(definition, placement, null, out placementSupport))
                        throw new InvalidOperationException("墙面已不可用，请重新选择位置。");
                    if (SupportsHorizontalPlacement(definition))
                        TryGetHorizontalSupport(
                            definition, placement, null, out placementSupport, out _);
                    if (!IsPlacementFree(definition, placement.position,
                            placement.rotation, null, ignoredRoot, placementSupport))
                        throw new InvalidOperationException("放置区域已被占用，请重试。");
                    var inventoryUnitId = Guid.NewGuid().ToString();
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
                                Quaternion.Inverse(itemRoot.rotation) * partRotation, "default",
                                definition.Id, inventoryUnitId));
                        }
                    }
                    else
                    {
                        var instanceId = Guid.NewGuid().ToString();
                        spawned.Add(Spawn(definition, instanceId,
                            itemRoot.InverseTransformPoint(placement.position),
                            Quaternion.Inverse(itemRoot.rotation) * placement.rotation, "default",
                            definition.Id, inventoryUnitId));
                    }
                    if (!definition.UnlimitedInventory) inventory[definition.Id]--;
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
                        inventory.Clear();
                        foreach (var entry in previousInventory)
                            inventory.Add(entry.Key, entry.Value);
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
            if (definition.PlacementSurface == ItemPlacementSurface.Wall)
            {
                placement = default;
                return false;
            }
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

        public bool TryResolveExactPlacement(ItemDefinition definition, Pose desired,
            GameItem ignoredItem, out Pose placement)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            PhysicsBody.ValidatePose(desired);
            Physics.SyncTransforms();
            if (definition.PlacementSurface == ItemPlacementSurface.Wall)
            {
                if (!TryGetWallSupport(definition, desired, ignoredItem, out var wall) ||
                    !IsPlacementFree(definition, desired.position, desired.rotation,
                        ignoredItem, null, wall))
                {
                    placement = default;
                    return false;
                }
                placement = desired;
                return true;
            }
            if (SupportsHorizontalPlacement(definition) &&
                TryGetHorizontalSupport(
                    definition, desired, ignoredItem,
                    out var support, out var supportPoint))
            {
                var surfaceRotation = Quaternion.Euler(0, desired.rotation.eulerAngles.y, 0);
                var surfacePosition = new Vector3(
                    desired.position.x, supportPoint.y + .006f, desired.position.z);
                if (IsPlacementFree(definition, surfacePosition, surfaceRotation,
                        ignoredItem, null, support))
                {
                    placement = new Pose(surfacePosition, surfaceRotation);
                    return true;
                }
            }
            var candidate = new Vector3(
                desired.position.x, Navigation.FloorHeight, desired.position.z);
            if (!Navigation.TrySample(candidate, .15f, out var floor) ||
                Mathf.Abs(floor.y - Navigation.FloorHeight) > .1f ||
                Vector3.Distance(candidate, floor) > .15f)
            {
                placement = default;
                return false;
            }
            var rotation = Quaternion.Euler(0, desired.rotation.eulerAngles.y, 0);
            var mainFloor = new Vector3(floor.x, Navigation.FloorHeight, floor.z);
            var ignoredRoot = definition.PlacementPreview
                ? (Motor as AvatarMotor)?.transform : null;
            if (!IsPlacementFree(definition, mainFloor, rotation, ignoredItem, ignoredRoot))
            {
                placement = default;
                return false;
            }
            placement = new Pose(mainFloor, rotation);
            return true;
        }

        public bool TryResolveWallPlacement(ItemDefinition definition, Ray aim,
            GameItem ignoredItem, out Pose placement)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.PlacementSurface != ItemPlacementSurface.Wall)
                throw new ArgumentException("Only wall items can use wall placement.", nameof(definition));
            if (!PhysicsBody.IsFinite(aim.origin) || !PhysicsBody.IsFinite(aim.direction) ||
                aim.direction.sqrMagnitude < .001f)
                throw new ArgumentException("Placement aim must contain a finite direction.", nameof(aim));
            Physics.SyncTransforms();
            var count = Physics.RaycastNonAlloc(aim.origin, aim.direction.normalized,
                placementHits, 8f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == placementHits.Length)
            {
                placement = default;
                return false;
            }
            var closest = float.PositiveInfinity;
            RaycastHit wall = default;
            for (var index = 0; index < count; index++)
            {
                var hit = placementHits[index];
                if (hit.collider == null || hit.distance >= closest ||
                    Mathf.Abs(hit.normal.y) > .25f ||
                    hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                    hit.collider.GetComponentInParent<GameItem>() != null)
                    continue;
                closest = hit.distance;
                wall = hit;
            }
            if (wall.collider == null)
            {
                placement = default;
                return false;
            }
            const float clearance = .008f;
            var rotation = Quaternion.LookRotation(wall.normal, Vector3.up);
            var position = wall.point + wall.normal *
                (definition.Footprint.y * .5f + clearance) -
                Vector3.up * (definition.PlacementHeight * .5f);
            var candidate = new Pose(position, rotation);
            if (!IsPlacementFree(definition, position, rotation, ignoredItem,
                    null, wall.collider))
            {
                placement = default;
                return false;
            }
            placement = candidate;
            return true;
        }

        public bool TryResolveSurfacePlacement(ItemDefinition definition, Ray aim,
            GameItem ignoredItem, out Pose placement)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!SupportsHorizontalPlacement(definition))
                throw new ArgumentException(
                    "Only horizontal-surface items can use surface placement.",
                    nameof(definition));
            if (!PhysicsBody.IsFinite(aim.origin) || !PhysicsBody.IsFinite(aim.direction) ||
                aim.direction.sqrMagnitude < .001f)
                throw new ArgumentException("Placement aim must contain a finite direction.", nameof(aim));
            Physics.SyncTransforms();
            var count = Physics.RaycastNonAlloc(aim.origin, aim.direction.normalized,
                placementHits, 8f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == placementHits.Length)
            {
                placement = default;
                return false;
            }
            var closest = float.PositiveInfinity;
            RaycastHit surface = default;
            for (var index = 0; index < count; index++)
            {
                var hit = placementHits[index];
                if (hit.collider == null || hit.distance >= closest ||
                    hit.normal.y < .6f ||
                    hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                    ignoredItem != null &&
                    hit.collider.transform.IsChildOf(ignoredItem.transform))
                    continue;
                closest = hit.distance;
                surface = hit;
            }
            if (surface.collider == null)
            {
                placement = default;
                return false;
            }
            var facing = Vector3.ProjectOnPlane(-aim.direction, Vector3.up).normalized;
            if (facing.sqrMagnitude < .01f) facing = Vector3.forward;
            var rotation = definition.ResolvePlacementRotation(facing);
            var position = surface.point + Vector3.up * .006f;
            if (!HasRequiredSurfaceCoverage(definition, position, rotation,
                    ignoredItem, surface.collider, surface.point.y) ||
                !IsPlacementFree(definition, position, rotation, ignoredItem,
                    null, surface.collider))
            {
                placement = default;
                return false;
            }
            placement = new Pose(position, rotation);
            return true;
        }

        private static bool SupportsHorizontalPlacement(ItemDefinition definition)
            => definition.PlacementSurface != ItemPlacementSurface.Wall &&
               (definition.AllowSurfacePlacement || definition.ImmovableAfterPlacement);

        private bool TryGetHorizontalSupport(ItemDefinition definition, Pose pose,
            GameItem ignoredItem,
            out Collider support, out Vector3 point)
        {
            var count = Physics.RaycastNonAlloc(pose.position + Vector3.up * .18f,
                Vector3.down, placementHits, .42f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            support = null;
            point = default;
            var closest = float.PositiveInfinity;
            if (count == placementHits.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var hit = placementHits[index];
                if (hit.collider == null || hit.distance >= closest ||
                    hit.normal.y < .6f ||
                    hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                    ignoredItem != null &&
                    hit.collider.transform.IsChildOf(ignoredItem.transform))
                    continue;
                closest = hit.distance;
                support = hit.collider;
                point = hit.point;
            }
            return support != null &&
                   HasRequiredSurfaceCoverage(definition, pose.position, pose.rotation,
                       ignoredItem, support, point.y);
        }

        private bool HasRequiredSurfaceCoverage(ItemDefinition definition,
            Vector3 position, Quaternion rotation, GameItem ignoredItem,
            Collider support, float supportHeight)
        {
            var ratio = definition.MinimumSurfaceSupportRatio;
            const float edgeInset = .006f;
            var halfWidth = Mathf.Max(0,
                definition.Footprint.x * .5f * ratio - edgeInset);
            var halfDepth = Mathf.Max(0,
                definition.Footprint.y * .5f * ratio - edgeInset);
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            var strictCorners = ratio >= .999f;
            var sampleCount = strictCorners ? 4 : 8;
            for (var index = 0; index < sampleCount; index++)
            {
                var angle = index * Mathf.PI * 2f / sampleCount;
                var x = strictCorners
                    ? (index & 1) == 0 ? -halfWidth : halfWidth
                    : Mathf.Cos(angle) * halfWidth;
                var z = strictCorners
                    ? (index & 2) == 0 ? -halfDepth : halfDepth
                    : Mathf.Sin(angle) * halfDepth;
                var sample = position + right * x + forward * z;
                sample.y = supportHeight + .08f;
                var count = Physics.RaycastNonAlloc(sample, Vector3.down,
                    placementHits, .16f, Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                if (count == placementHits.Length) return false;
                var found = false;
                for (var hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    var hit = placementHits[hitIndex];
                    if (hit.collider == null || hit.normal.y < .6f ||
                        Mathf.Abs(hit.point.y - supportHeight) > .035f ||
                        hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                        ignoredItem != null &&
                        hit.collider.transform.IsChildOf(ignoredItem.transform) ||
                        !BelongsToSupport(hit.collider, support))
                        continue;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool BelongsToSupport(Collider candidate, Collider support)
        {
            if (candidate == null || support == null) return false;
            if (candidate == support) return true;
            var supportItem = support.GetComponentInParent<GameItem>();
            return supportItem != null &&
                   candidate.transform.IsChildOf(supportItem.transform);
        }

        private static Transform GetVirtualSupportRoot(Collider support)
        {
            var item = support == null ? null : support.GetComponentInParent<GameItem>();
            return item == null ? null : item.transform;
        }

        private bool TryGetWallSupport(ItemDefinition definition, Pose pose,
            GameItem ignoredItem, out Collider support)
        {
            var outward = pose.rotation * Vector3.forward;
            var center = pose.position + Vector3.up * (definition.PlacementHeight * .5f);
            var origin = center + outward * (definition.Footprint.y * .5f + .04f);
            var count = Physics.RaycastNonAlloc(origin, -outward, placementHits,
                definition.Footprint.y + .12f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            support = null;
            var closest = float.PositiveInfinity;
            if (count == placementHits.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var hit = placementHits[index];
                if (hit.collider == null || hit.distance >= closest ||
                    Mathf.Abs(hit.normal.y) > .25f ||
                    Vector3.Dot(hit.normal, outward) < .85f ||
                    hit.collider.GetComponentInParent<AvatarMotor>() != null ||
                    hit.collider.GetComponentInParent<GameItem>() != null ||
                    ignoredItem != null &&
                    hit.collider.transform.IsChildOf(ignoredItem.transform))
                    continue;
                closest = hit.distance;
                support = hit.collider;
            }
            return support != null;
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
            Quaternion rotation, GameItem ignoredItem, Transform ignoredRoot = null,
            Collider ignoredSurface = null)
        {
            var clearance = definition.AllowSurfacePlacement || ignoredSurface != null
                ? .005f
                : .03f;
            var halfHeight = Mathf.Max(.05f, definition.PlacementHeight * .5f - clearance);
            var center = floor + Vector3.up * (halfHeight + clearance);
            var halfExtents = new Vector3(definition.Footprint.x * .5f + clearance, halfHeight,
                definition.Footprint.y * .5f + clearance);
            var count = Physics.OverlapBoxNonAlloc(center, halfExtents, placementOverlaps, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == placementOverlaps.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var collider = placementOverlaps[index];
                if (BelongsToSupport(collider, ignoredSurface)) continue;
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
            Quaternion localRotation, string stateId, string inventoryDefinitionId,
            string inventoryUnitId, CutStateSaveData cutState = null)
        {
            var root = UnityEngine.Object.Instantiate(definition.Prefab, itemRoot);
            try
            {
                root.transform.SetLocalPositionAndRotation(localPosition, localRotation.normalized);
                var cuttable = root.GetComponent<RuntimeCuttable>();
                if (cutState?.planes != null && cutState.planes.Length > 0)
                {
                    if (cuttable == null)
                        throw new InvalidOperationException(
                            $"{definition.DisplayName}存档包含切割历史，但预制体不可切割。");
                    cuttable.Restore(cutState);
                }
                var view = root.AddComponent<GameItem>();
                view.Bind(definition, Items, this, id, stateId,
                    inventoryDefinitionId, inventoryUnitId);
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

        public bool RecycleItem(string id)
        {
            if (!TryRemoveInventoryUnit(id, true, out var view)) return false;
            Notice = view.Definition.ConsumedProduct
                ? $"{view.Definition.DisplayName}已清理。"
                : $"{definitions[view.InventoryDefinitionId].DisplayName}已回收到背包。";
            return true;
        }

        public void RemoveItem(string id) => RecycleItem(id);

        internal bool BreakItem(string id, BreakSeverity severity)
        {
            if (!TryRemoveInventoryUnit(id, false, out var view)) return false;
            Notice = severity == BreakSeverity.Heavy
                ? $"{view.Instance.DisplayName}受到猛烈撞击，碎成了许多小碎片。"
                : $"{view.Instance.DisplayName}受到撞击，裂成了几块。";
            return true;
        }

        private bool TryRemoveInventoryUnit(string id, bool returnToInventory,
            out GameItem view)
        {
            view = null;
            if (Paused || disposed || !views.TryGetValue(id, out view)) return false;
            var owner = view.InventoryDefinitionId;
            var unit = view.InventoryUnitId;
            var removed = views.Values.Where(candidate => candidate != null &&
                candidate.InventoryDefinitionId == owner &&
                candidate.InventoryUnitId == unit).ToArray();
            if (removed.Any(candidate => candidate.IsReserved))
            {
                if (returnToInventory)
                    Notifications.ShowHint($"冷月正在使用{view.Definition.DisplayName}，暂时无法回收。");
                return false;
            }
            foreach (var candidate in removed)
            {
                // Removing from the registry invalidates active action reservations this frame.
                Items.Remove(candidate.Instance.Id);
                views.Remove(candidate.Instance.Id);
                candidate.Retire();
                UnityEngine.Object.Destroy(candidate.gameObject);
            }
            if (returnToInventory && !definitions[owner].UnlimitedInventory &&
                inventory.TryGetValue(owner, out var remaining))
                inventory[owner] = Mathf.Min(
                    definitions[owner].InventoryLimit, remaining + 1);
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
                // PICO's system keyboard can transiently pause or unfocus the app.
                // The conversation owns its timeout and explicit cancellation lifecycle.
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
            CaptureInventory();
            store.Save(saved);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            Conversation.Changed -= OnConversationChanged;
            Conversation.Dispose();
            SpontaneousReplies.Dispose();
            characterContext.Dispose();
            socialAttentionRig?.End(true);
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
            Items.CleanupFailed -= Debug.LogException;
            lifetime.Dispose();
        }

    }
}
