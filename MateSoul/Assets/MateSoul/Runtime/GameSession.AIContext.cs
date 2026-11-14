using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class GameSession
    {
        private const int WorldItemGroupLimit = 24;
        private const int WorldSalientItemLimit = 12;
        private const int WorldHeldItemLimit = 8;
        private const int WorldFactLimit = 12;
        private const int WorldInventoryAlertLimit = 8;
        private RuntimeEnvironmentSnapshot aiEnvironment;
        private VirtualItemFocusController aiFocus;
        private MiniGameHost aiMiniGames;
        private string aiWorldSignature;
        private long aiWorldRevision;

        public void ConfigureAIWorldContext(RoomMapSnapshot room,
            VirtualItemFocusController focus, MiniGameHost miniGames)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            aiEnvironment = BuildEnvironmentSnapshot(room);
            aiFocus = focus;
            aiMiniGames = miniGames;
            aiWorldSignature = null;
            aiWorldRevision = 0;
        }

        private RuntimeWorldSnapshot CaptureAIWorldContext()
        {
            if (aiEnvironment == null) return null;
            var itemViews = views.Values
                .Where(view => view != null && view.Instance != null)
                .OrderBy(view => view.Instance.Id, StringComparer.Ordinal)
                .ToArray();
            var player = CapturePlayerContext();
            var virtualWorld = CaptureVirtualWorld(itemViews);
            var world = new RuntimeWorldSnapshot
            {
                player = player,
                held_items = CaptureHeldItems(itemViews),
                activity = aiMiniGames?.Active?.CaptureRuntimeContext(),
                active_facts = CaptureWorldFacts(itemViews),
                inventory_alerts = CaptureInventoryAlerts(),
                environment = aiEnvironment,
                virtual_world = virtualWorld,
                placement_destinations = CapturePlacementDestinations()
            };
            var signature = JsonUtility.ToJson(world);
            if (!string.Equals(signature, aiWorldSignature, StringComparison.Ordinal))
            {
                aiWorldSignature = signature;
                aiWorldRevision++;
            }
            world.revision = aiWorldRevision;
            virtualWorld.revision = aiWorldRevision;
            return world;
        }

        private RuntimePlayerSnapshot CapturePlayerContext()
        {
            var hasHead = playerHead != null;
            var distance = hasHead
                ? Vector3.ProjectOnPlane(
                    playerHead.position - Motor.Position, Vector3.up).magnitude
                : 0f;
            var focused = aiFocus?.FocusedItem;
            return new RuntimePlayerSnapshot
            {
                nearby = playerNearby,
                proximity = hasHead ? DistanceBand(distance) : "unknown",
                distance_m = Round(distance),
                dialogue_open = dialogueAttentionRequested,
                interaction_mode = aiMiniGames?.Input == null
                    ? "unknown"
                    : InteractionModeId(aiMiniGames.Input.CurrentMode),
                focused_item = focused == null
                    ? null
                    : ToWorldItem(focused, hasHead ? playerHead.position : (Vector3?)null)
            };
        }

        private RuntimeHeldItemSnapshot[] CaptureHeldItems(GameItem[] itemViews)
        {
            return itemViews
                .Where(view => view.Instance.ControlState == ItemControlState.PlayerHeld ||
                               view.Instance.ControlState == ItemControlState.PlayerUsing ||
                               view.Instance.IsCharacterOwned)
                .Take(WorldHeldItemLimit)
                .Select(view => new RuntimeHeldItemSnapshot
                {
                    id = view.Instance.Id,
                    name = view.Instance.DisplayName,
                    state = view.Instance.StateId,
                    holder = view.Instance.ControlState == ItemControlState.PlayerHeld ||
                             view.Instance.ControlState == ItemControlState.PlayerUsing
                        ? "player"
                        : "character"
                }).ToArray();
        }

        private RuntimeWorldFactSnapshot[] CaptureWorldFacts(GameItem[] itemViews)
        {
            var facts = new List<RuntimeWorldFactSnapshot>(WorldFactLimit);
            foreach (var view in itemViews)
            {
                view.CaptureRuntimeWorldFacts(facts, WorldFactLimit);
                if (facts.Count >= WorldFactLimit) break;
            }
            return facts.ToArray();
        }

        private RuntimeInventoryAlertSnapshot[] CaptureInventoryAlerts()
        {
            return CatalogItems
                .Where(definition => !definition.UnlimitedInventory &&
                                     definition.InventoryLimit > 0)
                .Select(definition => new
                {
                    Definition = definition,
                    Remaining = inventory.TryGetValue(definition.Id, out var remaining)
                        ? remaining
                        : 0
                })
                .Where(entry => entry.Remaining <=
                                Mathf.Max(1, Mathf.CeilToInt(
                                    entry.Definition.InventoryLimit * .2f)))
                .OrderBy(entry => entry.Remaining)
                .ThenBy(entry => entry.Definition.Id, StringComparer.Ordinal)
                .Take(WorldInventoryAlertLimit)
                .Select(entry => new RuntimeInventoryAlertSnapshot
                {
                    definition_id = entry.Definition.Id,
                    name = entry.Definition.DisplayName,
                    remaining = entry.Remaining,
                    limit = entry.Definition.InventoryLimit,
                    severity = entry.Remaining == 0 ? "empty" : "low"
                }).ToArray();
        }

        private RuntimeVirtualWorldSnapshot CaptureVirtualWorld(GameItem[] itemViews)
        {
            var groups = itemViews
                .GroupBy(view => new
                {
                    view.Instance.DefinitionId,
                    view.Instance.DisplayName,
                    view.Instance.StateId,
                    Control = ControlStateId(view.Instance.ControlState)
                })
                .Select(group => new RuntimeVirtualItemGroupSnapshot
                {
                    definition_id = group.Key.DefinitionId,
                    name = group.Key.DisplayName,
                    state = group.Key.StateId,
                    control = group.Key.Control,
                    count = group.Count()
                })
                .OrderByDescending(group => group.count)
                .ThenBy(group => group.definition_id, StringComparer.Ordinal)
                .ThenBy(group => group.state, StringComparer.Ordinal)
                .ToArray();
            var controls = itemViews
                .GroupBy(view => ControlStateId(view.Instance.ControlState))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new RuntimeControlCountSnapshot
                {
                    control = group.Key,
                    count = group.Count()
                }).ToArray();
            return new RuntimeVirtualWorldSnapshot
            {
                total_count = itemViews.Length,
                omitted_group_count = Mathf.Max(0, groups.Length - WorldItemGroupLimit),
                item_groups = groups.Take(WorldItemGroupLimit).ToArray(),
                control_counts = controls,
                salient_items = CaptureSalientItems(itemViews)
            };
        }

        private RuntimeWorldItemSnapshot[] CaptureSalientItems(GameItem[] itemViews)
        {
            var result = new List<RuntimeWorldItemSnapshot>(WorldSalientItemLimit);
            var included = new HashSet<string>(StringComparer.Ordinal);
            var origin = playerHead == null ? (Vector3?)null : playerHead.position;

            void Add(GameItem view)
            {
                if (view == null || result.Count >= WorldSalientItemLimit ||
                    !included.Add(view.Instance.Id))
                    return;
                result.Add(ToWorldItem(view, origin));
            }

            Add(aiFocus?.FocusedItem);
            foreach (var view in itemViews.Where(view =>
                         view.IsHeld || view.IsReserved))
                Add(view);
            if (origin.HasValue)
                foreach (var view in itemViews
                             .OrderBy(view => Vector3.Distance(
                                 view.transform.position, origin.Value)))
                    Add(view);
            else
                foreach (var view in itemViews) Add(view);
            return result.ToArray();
        }

        private static RuntimeWorldItemSnapshot ToWorldItem(
            GameItem view, Vector3? playerPosition)
        {
            var distance = playerPosition.HasValue
                ? Vector3.Distance(view.transform.position, playerPosition.Value)
                : -1f;
            return new RuntimeWorldItemSnapshot
            {
                id = view.Instance.Id,
                name = view.Instance.DisplayName,
                state = view.Instance.StateId,
                control = ControlStateId(view.Instance.ControlState),
                distance = distance < 0 ? "unknown" : DistanceBand(distance)
            };
        }

        private static RuntimeEnvironmentSnapshot BuildEnvironmentSnapshot(
            RoomMapSnapshot room)
        {
            var floorArea = room.Floors.Sum(floor => floor.Area);
            var occupiedArea = room.Obstacles
                .Where(obstacle => obstacle.Kind != RoomObstacleKind.Wall &&
                                   obstacle.Kind != RoomObstacleKind.VirtualWall)
                .Sum(obstacle => obstacle.Size.x * obstacle.Size.z);
            var openRatio = floorArea <= .01f
                ? 0f
                : Mathf.Clamp01(1f - occupiedArea / floorArea);
            var hasRealityItems = room.RealityItems.Count > 0;
            var exactCounts = hasRealityItems &&
                !string.IsNullOrWhiteSpace(room.Source) &&
                room.Source.IndexOf(
                    "Room Capture", StringComparison.OrdinalIgnoreCase) >= 0;
            var counts = hasRealityItems
                ? room.RealityItems
                    .GroupBy(item => item.Semantic)
                    .Select(group =>
                    {
                        var definition = RealitySemanticCatalog.Get(group.Key);
                        return new RuntimeEnvironmentCountSnapshot
                        {
                            id = definition.Id,
                            name = definition.DisplayName,
                            count = group.Count(),
                            estimated = !exactCounts
                        };
                    })
                : room.Obstacles
                    .Select(obstacle => ObstacleSemantic(obstacle.Kind))
                    .Where(value => value != null)
                    .Select(value => value.Value)
                    .GroupBy(value => value.id)
                    .Select(group => new RuntimeEnvironmentCountSnapshot
                    {
                        id = group.Key,
                        name = group.First().name,
                        count = group.Count(),
                        estimated = true
                    });
            var semanticCounts = counts
                .OrderByDescending(value => value.count)
                .ThenBy(value => value.id, StringComparer.Ordinal)
                .Take(24)
                .ToArray();
            return new RuntimeEnvironmentSnapshot
            {
                revision = 1,
                source = string.IsNullOrWhiteSpace(room.Source)
                    ? "unknown"
                    : room.Source,
                confidence = EnvironmentConfidence(room),
                count_quality = exactCounts ? "observed" : "estimated",
                floor_area_m2 = Round(floorArea),
                width_m = Round(room.Bounds.size.x),
                length_m = Round(room.Bounds.size.z),
                height_m = Round(room.Bounds.size.y),
                shape = RoomShape(room),
                open_space = openRatio >= .65f
                    ? "open"
                    : openRatio >= .35f ? "balanced" : "dense",
                floor_count = room.Floors.Count,
                semantic_counts = semanticCounts,
                zones = InferZones(semanticCounts),
                relations = exactCounts
                    ? InferRelations(room.RealityItems)
                    : Array.Empty<RuntimeEnvironmentRelationSnapshot>()
            };
        }

        private static RuntimeEnvironmentZoneSnapshot[] InferZones(
            RuntimeEnvironmentCountSnapshot[] counts)
        {
            var ids = new HashSet<string>(
                counts.Select(value => value.id), StringComparer.Ordinal);
            var zones = new List<RuntimeEnvironmentZoneSnapshot>(5);
            if (ids.Contains("table") || ids.Contains("chair"))
                zones.Add(new RuntimeEnvironmentZoneSnapshot
                    { id = "dining", description = "table or seating area" });
            if (ids.Contains("bed") || ids.Contains("sofa"))
                zones.Add(new RuntimeEnvironmentZoneSnapshot
                    { id = "resting", description = "bed or sofa area" });
            if (ids.Contains("screen"))
                zones.Add(new RuntimeEnvironmentZoneSnapshot
                    { id = "media", description = "screen viewing area" });
            if (ids.Contains("refrigerator") || ids.Contains("washing-machine") ||
                ids.Contains("cabinet"))
                zones.Add(new RuntimeEnvironmentZoneSnapshot
                    { id = "utility", description = "storage or appliance area" });
            if (zones.Count == 0)
                zones.Add(new RuntimeEnvironmentZoneSnapshot
                    { id = "open-area", description = "general open room area" });
            return zones.ToArray();
        }

        private static RuntimeEnvironmentRelationSnapshot[] InferRelations(
            IReadOnlyList<RealityItemObservation> items)
        {
            var result = new List<RuntimeEnvironmentRelationSnapshot>(8);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < items.Count && result.Count < 8; index++)
            {
                var source = items[index];
                RealityItemObservation nearest = null;
                var nearestDistance = float.PositiveInfinity;
                for (var candidateIndex = 0; candidateIndex < items.Count; candidateIndex++)
                {
                    if (candidateIndex == index) continue;
                    var candidate = items[candidateIndex];
                    var distance = Vector3.Distance(source.Center, candidate.Center);
                    if (distance >= nearestDistance) continue;
                    nearest = candidate;
                    nearestDistance = distance;
                }
                if (nearest == null || nearestDistance > 2.5f) continue;
                var sourceId = RealitySemanticCatalog.Get(source.Semantic).Id;
                var targetId = RealitySemanticCatalog.Get(nearest.Semantic).Id;
                var key = string.CompareOrdinal(sourceId, targetId) <= 0
                    ? $"{sourceId}|{targetId}"
                    : $"{targetId}|{sourceId}";
                if (!unique.Add(key)) continue;
                result.Add(new RuntimeEnvironmentRelationSnapshot
                {
                    subject = sourceId,
                    relation = "near",
                    target = targetId
                });
            }
            return result.ToArray();
        }

        private static (string id, string name)? ObstacleSemantic(
            RoomObstacleKind kind)
        {
            switch (kind)
            {
                case RoomObstacleKind.Table: return ("table", "桌子");
                case RoomObstacleKind.Sofa: return ("sofa", "沙发");
                case RoomObstacleKind.Chair: return ("chair", "椅子");
                case RoomObstacleKind.Cabinet: return ("cabinet", "柜子");
                case RoomObstacleKind.Bed: return ("bed", "床");
                case RoomObstacleKind.Plant: return ("plant", "植物");
                case RoomObstacleKind.Screen: return ("screen", "屏幕");
                case RoomObstacleKind.Refrigerator: return ("refrigerator", "冰箱");
                case RoomObstacleKind.WashingMachine: return ("washing-machine", "洗衣机");
                case RoomObstacleKind.AirConditioner: return ("air-conditioner", "空调");
                case RoomObstacleKind.Lamp: return ("lamp", "灯");
                default: return null;
            }
        }

        private static string EnvironmentConfidence(RoomMapSnapshot room)
        {
            if (room.RealityItems.Count > 0 &&
                !string.IsNullOrWhiteSpace(room.Source) &&
                room.Source.IndexOf("Room Capture", StringComparison.OrdinalIgnoreCase) >= 0)
                return "high";
            return room.Floors.Count > 0 ? "medium" : "low";
        }

        private static string RoomShape(RoomMapSnapshot room)
        {
            if (room.Floors.Count > 1) return "multi-area";
            var vertices = room.MainFloor?.Vertices.Count ?? 0;
            if (vertices == 4) return "rectangular";
            return vertices > 4 ? "irregular" : "unknown";
        }

        private static string InteractionModeId(PlayerInteractionMode mode)
        {
            switch (mode)
            {
                case PlayerInteractionMode.EyeHand: return "eye-hand";
                case PlayerInteractionMode.Gesture: return "gesture";
                default: return "controller";
            }
        }

        private static string ControlStateId(ItemControlState state)
        {
            switch (state)
            {
                case ItemControlState.PlayerHeld: return "player-held";
                case ItemControlState.PlayerUsing: return "player-using";
                case ItemControlState.ReservedByCharacter: return "character-reserved";
                case ItemControlState.HeldByCharacter: return "character-held";
                case ItemControlState.StagedForAction: return "action-staged";
                case ItemControlState.InUseByCharacter: return "character-using";
                case ItemControlState.Removed: return "removed";
                default: return "available";
            }
        }

        private static string DistanceBand(float distance)
        {
            if (distance <= .8f) return "within-reach";
            if (distance <= 2f) return "near";
            if (distance <= 5f) return "in-room";
            return "far";
        }

        private static float Round(float value)
            => (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
