using System;
using System.Collections.Generic;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class GameSession : IPlacementCommandHandler
    {
        private const int PlacementSurfaceLimit = 24;
        private const int PlacementDestinationLimit = 32;

        // Candidate positions for one named destination, in the order they are tried.
        private sealed class PlacementTarget
        {
            public readonly List<Vector3> Points = new List<Vector3>();
        }

        public bool TryPlace(PlacementDirective directive, string itemInstanceId, out string failure)
        {
            failure = null;
            // "把苹果捡起来放到床上": the reply may name only the destination.
            // Recover the item the player said to fetch before deciding that the
            // hand is empty, so the combined command stays a single action chain.
            if (string.IsNullOrEmpty(itemInstanceId) && !IsHoldingAnyItem())
            {
                itemInstanceId = InferPlacementItem(directive?.Request);
                if (itemInstanceId != null)
                    Debug.Log($"MATESOUL_PLACE infer item={itemInstanceId} " +
                        $"request={directive.Request}");
            }
            // Implicit prerequisite: an empty hand picks the item up first.
            if (!string.IsNullOrEmpty(itemInstanceId) && !IsHeldByCharacter(itemInstanceId) &&
                views.TryGetValue(itemInstanceId, out var target) &&
                PickupItemAction.Supports(target))
            {
                pendingPlacementDirective = directive;
                pendingPlacementItem = itemInstanceId;
                var pickup = Avatar.Submit(new AvatarIntent(PickupItemAction.Id,
                    IntentSource.AI, itemInstanceId), preemptSamePriority: true);
                failure = pickup.State == ActionState.Rejected ? pickup.Error : null;
                Debug.Log($"MATESOUL_PLACE defer item={itemInstanceId} " +
                    $"pickup={pickup.State} failure={failure}");
                return failure == null;
            }
            var item = ResolvePlacementItem(itemInstanceId, out failure);
            if (item == null) return false;
            if (!item.Definition.AllowSurfacePlacement &&
                !item.Definition.ImmovableAfterPlacement &&
                item.Definition.PlacementSurface != ItemPlacementSurface.Ground)
            {
                failure = $"{item.Definition.DisplayName}没有办法稳稳地放下。";
                return false;
            }

            var destination = directive?.Destination?.Trim() ?? string.Empty;
            var kind = directive?.Kind ?? PlacementDestinationKind.Any;
            if (kind == PlacementDestinationKind.Any) kind = InferDestinationKind(destination);

            if (string.IsNullOrEmpty(destination) || MatchesFloor(destination))
                return TryPlaceOnFloorNearAvatar(item, out failure);

            var targets = new List<PlacementTarget>();
            if (kind != PlacementDestinationKind.Virtual)
                targets.AddRange(FindRealityTargets(destination));
            if (kind != PlacementDestinationKind.Real)
                targets.AddRange(FindVirtualTargets(destination));
            Shuffle(targets);

            string reason = null;
            if (TryResolveTargets(item, targets, out var placement))
            {
                Debug.Log($"MATESOUL_PLACE resolve item={item.Instance.Id} " +
                    $"destination={destination} kind={kind} route=reality point={placement.position}");
                return RetargetPickup(item, placement, out failure);
            }
            if (targets.Count > 0)
                reason = $"“{destination}”附近没有能稳定放下{item.Definition.DisplayName}的位置";

            if (kind != PlacementDestinationKind.Virtual)
            {
                var meshTargets = FindMeshTargets(destination);
                Shuffle(meshTargets);
                if (TryResolveTargets(item, meshTargets, out placement))
                {
                    Debug.Log($"MATESOUL_PLACE resolve item={item.Instance.Id} " +
                        $"destination={destination} kind={kind} route=mesh point={placement.position}");
                    return RetargetPickup(item, placement, out failure);
                }
                if (reason == null && meshTargets.Count > 0)
                    reason = $"“{destination}”附近没有能稳定放下{item.Definition.DisplayName}的位置";
            }

            failure = reason ?? (kind == PlacementDestinationKind.Real
                ? $"在现实场景里没有找到可以放置的“{destination}”。"
                : kind == PlacementDestinationKind.Virtual
                    ? $"在虚拟物品里没有找到可以放置的“{destination}”。"
                    : $"没有找到可以放置的“{destination}”，现实房间标定、实时网格和虚拟物品里都没有找到。");
            return false;
        }

        private AvatarItemRig ItemRig =>
            Motor is AvatarMotor avatar ? avatar.GetComponent<AvatarItemRig>() : null;

        private string pendingPlacementItem;
        private PlacementDirective pendingPlacementDirective;

        // "Put the carrot on the bed" while her hand is empty picks the carrot up
        // first; the placement resumes as soon as the pickup finishes.
        private void TickPendingPlacement()
        {
            if (pendingPlacementDirective == null) return;
            var held = PassivelyHeldItem();
            if (held == null || held.Instance.Id != pendingPlacementItem) return;
            var directive = pendingPlacementDirective;
            var itemId = pendingPlacementItem;
            pendingPlacementDirective = null;
            pendingPlacementItem = null;
            Debug.Log($"MATESOUL_PLACE resume item={itemId}");
            TryPlace(directive, itemId, out var failure);
            if (failure != null) Debug.Log($"MATESOUL_PLACE resume failed item={itemId} reason={failure}");
        }

        private bool IsHoldingAnyItem()
        {
            if (Avatar.TryGetRunning(PickupItemAction.Id, out PickupItemAction pickup) &&
                pickup.IsHolding && pickup.HeldItem != null)
                return true;
            return PassivelyHeldItem() != null;
        }

        // Matches the item the player named in the request. Only the part before
        // the placement verb is searched, so a destination ("放到桌上") is never
        // mistaken for the item, and the rightmost match wins because the item
        // being moved usually comes last before that verb.
        private string InferPlacementItem(string request)
        {
            if (string.IsNullOrWhiteSpace(request)) return null;
            var scope = request;
            foreach (var marker in PlacementVerbs)
            {
                var index = request.IndexOf(marker, StringComparison.Ordinal);
                if (index > 0 && index < scope.Length) scope = request.Substring(0, index);
            }
            string best = null;
            var bestPosition = int.MinValue;
            var bestLength = 0;
            foreach (var view in views.Values)
            {
                if (view == null || view.Instance == null || !view.Instance.IsPresent ||
                    view.Instance.IsOccupied || !PickupItemAction.Supports(view)) continue;
                foreach (var name in new[]
                {
                    view.Definition.DisplayName, view.Instance.DisplayName, view.Definition.Id
                })
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Length < bestLength) continue;
                    var index = scope.LastIndexOf(name, StringComparison.OrdinalIgnoreCase);
                    if (index < 0 || index < bestPosition) continue;
                    if (index == bestPosition && name.Length <= bestLength) continue;
                    bestPosition = index;
                    bestLength = name.Length;
                    best = view.Instance.Id;
                }
            }
            return best;
        }

        private static readonly string[] PlacementVerbs =
            { "放到", "放在", "摆到", "摆在", "搁到", "搁在", "丢到", "扔到", "移到", "移至" };

        private bool IsHeldByCharacter(string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;
            if (Avatar.TryGetRunning(PickupItemAction.Id, out PickupItemAction pickup) &&
                pickup.IsHolding && pickup.HeldItem != null &&
                pickup.HeldItem.Instance.Id == itemInstanceId)
                return true;
            var held = PassivelyHeldItem();
            return held != null && held.Instance.Id == itemInstanceId;
        }

        // Items whose pickup action has already finished but that are still carried.
        private GameItem PassivelyHeldItem()
        {
            var body = ItemRig != null ? ItemRig.PassiveHoldBody : null;
            if (body == null) return null;
            foreach (var view in views.Values)
                if (view != null && view.Body == body) return view;
            return null;
        }

        private GameItem ResolvePlacementItem(string itemInstanceId, out string failure)
        {
            failure = null;
            if (!string.IsNullOrEmpty(itemInstanceId))
            {
                if (!views.TryGetValue(itemInstanceId, out var view))
                {
                    failure = "冷月手里没有这件物品。";
                    return null;
                }
                var carried = ItemRig != null ? ItemRig.PassiveHoldBody : null;
                if (carried != null && view.Body == carried) return view;
                if (view.Instance.ControlState != ItemControlState.HeldByCharacter)
                {
                    failure = $"{view.Definition.DisplayName}不在冷月手里。";
                    return null;
                }
                if (!Avatar.TryGetRunning(PickupItemAction.Id, out PickupItemAction running) ||
                    !running.IsHolding || !ReferenceEquals(running.HeldItem, view))
                {
                    failure = "冷月还没有拿稳这件物品。";
                    return null;
                }
                return view;
            }
            if (Avatar.TryGetRunning(PickupItemAction.Id, out PickupItemAction pickup) &&
                pickup.IsHolding && pickup.HeldItem != null)
                return pickup.HeldItem;
            var passive = PassivelyHeldItem();
            if (passive != null) return passive;
            failure = "冷月现在手里没有可以放下的物品。";
            return null;
        }

        private bool RetargetPickup(GameItem item, Pose placement, out string failure)
        {
            if (Avatar.TryGetRunning(PickupItemAction.Id, out PickupItemAction pickup) &&
                pickup.IsHolding && ReferenceEquals(pickup.HeldItem, item))
                return pickup.TryRetarget(() => placement, out failure);
            // The pickup already finished and left the item in her hand, so the
            // placement continues as its own action instead of retargeting.
            var rig = Motor is AvatarMotor avatar ? avatar.GetComponent<AvatarItemRig>() : null;
            if (rig != null && rig.PassiveHoldBody != null && rig.PassiveHoldBody == item.Body)
            {
                var handle = Avatar.Submit(new AvatarIntent(PlaceHeldAction.Id,
                    IntentSource.AI, item.Instance.Id, placement), preemptSamePriority: true);
                failure = handle.State == ActionState.Rejected ? handle.Error : null;
                return failure == null;
            }
            failure = "冷月还没有拿稳这件物品。";
            return false;
        }

        private bool TryPlaceOnFloorNearAvatar(GameItem item, out string failure)
        {
            failure = null;
            var origin = Motor.Position;
            var facing = Motor is AvatarMotor avatar
                ? avatar.transform.rotation
                : Quaternion.identity;
            var forward = Vector3.ProjectOnPlane(facing * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            forward.Normalize();
            var offsets = new[] { .55f, .75f, .4f, 1f };
            foreach (var offset in offsets)
            foreach (var side in new[] { 0f, -35f, 35f, -70f, 70f })
            {
                var direction = Quaternion.Euler(0, side, 0) * forward;
                var desired = origin + direction * offset;
                desired.y = Navigation.FloorHeight;
                var pose = new Pose(desired,
                    item.Definition.ResolvePlacementRotation(-direction));
                if (TryResolveExactPlacement(item.Definition, pose, item, out var placement))
                    return RetargetPickup(item, placement, out failure);
            }
            failure = $"冷月身边没有可以放下{item.Definition.DisplayName}的空位。";
            return false;
        }

        private bool TryResolveTargets(GameItem item, List<PlacementTarget> targets, out Pose placement)
        {
            placement = default;
            foreach (var target in targets)
            {
                // Try the spot nearest to her first. A surface offers its centre
                // as well as its edges, and picking the centre of a bed puts the
                // item outside arm reach from every reachable standing position.
                target.Points.Sort((left, right) =>
                    (left - Motor.Position).sqrMagnitude.CompareTo(
                        (right - Motor.Position).sqrMagnitude));
                foreach (var point in target.Points)
                {
                    var facesPlayer = Motor.Position - point;
                    var pose = new Pose(point,
                        item.Definition.ResolvePlacementRotation(facesPlayer));
                    if (TryResolveExactPlacement(item.Definition, pose, item, out placement))
                        return true;
                }
            }
            return false;
        }

        private List<PlacementTarget> FindRealityTargets(string destination)
        {
            var result = new List<PlacementTarget>();
            foreach (var record in RealityItems.Items)
            {
                if (!record.Definition.IsItem) continue;
                if (!IsPlacementDestination(record.Semantic)) continue;
                // The English semantic id ("bed") and the Chinese aliases ("床")
                // are two ways to name the same object; either one is enough.
                if (!Matches(destination, record.Id, record.Definition.Id,
                        record.Definition.DisplayName) &&
                    !Array.Exists(RealAliases(record.Semantic),
                        alias => Matches(destination, alias)))
                    continue;
                var target = new PlacementTarget();
                if (HasSurfaceSupport(record.Semantic))
                    AddRealitySurfacePoints(target.Points, record);
                else
                    AddFloorRingPoints(target.Points, record.Center,
                        new Vector2(record.Size.x, record.Size.z));
                if (target.Points.Count > 0) result.Add(target);
            }
            return result;
        }

        private List<PlacementTarget> FindVirtualTargets(string destination)
        {
            var result = new List<PlacementTarget>();
            foreach (var view in views.Values)
            {
                if (view == null) continue;
                var definition = view.Definition;
                if (!IsPlacementFurniture(definition)) continue;
                if (view.Instance.ControlState == ItemControlState.HeldByCharacter) continue;
                if (!Matches(destination, view.Instance.Id, definition.Id,
                        definition.DisplayName, view.Instance.DisplayName) &&
                    !Array.Exists(VirtualAliases(definition.Id),
                        alias => Matches(destination, alias)))
                    continue;
                var target = new PlacementTarget();
                if (definition.ProvidesPlacementSurface)
                    AddVirtualSurfacePoints(target.Points, view, definition);
                else
                    AddVirtualBesidePoints(target.Points, view, definition);
                if (target.Points.Count > 0) result.Add(target);
            }
            return result;
        }

        // Room calibration can lack a semantic volume for the requested object.
        // The live spatial mesh then supplies the surfaces, first by matching
        // label and finally by plain horizontal-surface heuristics.
        private List<PlacementTarget> FindMeshTargets(string destination)
        {
            var result = new List<PlacementTarget>();
            var mesh = RealityMesh;
            if (mesh == null) return result;
            var semantic = MatchSemantic(destination);
            if (!semantic.HasValue || !IsPlacementDestination(semantic.Value)) return result;
            var label = (ByteDance.PICO.XR.PxrSemanticLabel)(int)semantic.Value;

            var surfaces = new List<RealityTriangle>();
            foreach (var triangle in mesh.Geometry)
                if (triangle.Normal.y >= .82f && triangle.Area >= .012f &&
                    triangle.HasSemantic(label))
                    surfaces.Add(triangle);
            if (surfaces.Count == 0 && HasSurfaceSupport(semantic.Value))
                foreach (var triangle in mesh.Geometry)
                    if (triangle.Normal.y >= .85f && triangle.Area >= .015f &&
                        triangle.Center.y >= Navigation.FloorHeight + .5f &&
                        triangle.Center.y <= Navigation.FloorHeight + 1f)
                        surfaces.Add(triangle);
            if (surfaces.Count == 0) return result;

            surfaces.Sort((left, right) =>
                (left.Center - Motor.Position).sqrMagnitude.CompareTo(
                    (right.Center - Motor.Position).sqrMagnitude));
            var points = new List<Vector3>();
            foreach (var triangle in surfaces)
            {
                if (points.Count >= PlacementSurfaceLimit) break;
                var center = triangle.Center + Vector3.up * .004f;
                var duplicate = false;
                foreach (var existing in points)
                    if ((existing - center).sqrMagnitude < .09f) { duplicate = true; break; }
                if (!duplicate) points.Add(center);
            }
            if (points.Count == 0) return result;
            var target = new PlacementTarget();
            target.Points.AddRange(points);
            result.Add(target);
            return result;
        }

        private static void AddRealitySurfacePoints(List<Vector3> points,
            RealityItemRecord record)
        {
            // Room Capture volumes are centred boxes, while the chair/bed scanners
            // publish the usable surface itself as the centre. Adding half the
            // height to a scanner record lifted every point ~25cm above the bed.
            var top = record.CapturedBySdk
                ? record.Center.y + record.Size.y * .5f
                : record.Center.y + .004f;
            var center = record.Center;
            var rotation = record.Rotation;
            var size = record.Size;
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            var x = Mathf.Max(.1f, size.x * .5f - .16f);
            var z = Mathf.Max(.1f, size.z * .5f - .16f);
            points.Add(new Vector3(center.x, top, center.z));
            points.Add(new Vector3(center.x + right.x * x, top, center.z + right.z * x));
            points.Add(new Vector3(center.x - right.x * x, top, center.z - right.z * x));
            points.Add(new Vector3(center.x + forward.x * z, top, center.z + forward.z * z));
            points.Add(new Vector3(center.x - forward.x * z, top, center.z - forward.z * z));
        }

        private void AddVirtualSurfacePoints(List<Vector3> points, GameItem view,
            ItemDefinition definition)
        {
            var position = view.transform.position;
            var rotation = view.transform.rotation;
            var top = position + Vector3.up * Mathf.Max(.05f, definition.PlacementHeight);
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            var x = Mathf.Max(.1f, definition.Footprint.x * .5f - .16f);
            var z = Mathf.Max(.1f, definition.Footprint.y * .5f - .16f);
            AddSurfaceProbe(points, top, view);
            AddSurfaceProbe(points, top + right * x, view);
            AddSurfaceProbe(points, top - right * x, view);
            AddSurfaceProbe(points, top + forward * z, view);
            AddSurfaceProbe(points, top - forward * z, view);
        }

        // The authored placement height is the item's overall height, so a chair
        // reports its backrest rather than the seat. Probing down onto the item
        // itself turns each footprint sample into the surface actually under it.
        private static void AddSurfaceProbe(List<Vector3> points, Vector3 candidate, GameItem view)
        {
            var origin = new Vector3(candidate.x, candidate.y + .2f, candidate.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 1.2f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            var surface = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.normal.y < .7f) continue;
                if (hit.collider.GetComponentInParent<AvatarMotor>() != null) continue;
                if (!hit.collider.transform.IsChildOf(view.transform)) continue;
                if (hit.point.y > surface) surface = hit.point.y;
            }
            points.Add(float.IsNegativeInfinity(surface)
                ? candidate
                : new Vector3(candidate.x, surface, candidate.z));
        }

        private void AddVirtualBesidePoints(List<Vector3> points, GameItem view,
            ItemDefinition definition)
        {
            AddFloorRingPoints(points, view.transform.position,
                new Vector2(definition.Footprint.x, definition.Footprint.y));
        }

        private void AddFloorRingPoints(List<Vector3> points, Vector3 origin, Vector2 footprint)
        {
            var baseRadius = Mathf.Max(.55f, footprint.magnitude * .5f + .3f);
            var floor = Navigation.FloorHeight;
            foreach (var extra in new[] { 0f, .22f, .45f })
            foreach (var angle in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
            {
                var radians = angle * Mathf.Deg2Rad;
                var point = new Vector3(
                    origin.x + Mathf.Cos(radians) * (baseRadius + extra),
                    floor,
                    origin.z + Mathf.Sin(radians) * (baseRadius + extra));
                if (!Navigation.TrySample(point, .35f, out var sampled)) continue;
                points.Add(new Vector3(sampled.x, floor + .01f, sampled.z));
            }
        }

        private RuntimePlacementDestinationSnapshot[] CapturePlacementDestinations()
        {
            var result = new List<RuntimePlacementDestinationSnapshot>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in RealityItems.Items)
            {
                if (!record.Definition.IsItem) continue;
                if (!IsPlacementDestination(record.Semantic)) continue;
                if (!seen.Add("real:" + record.Definition.Id)) continue;
                result.Add(new RuntimePlacementDestinationSnapshot
                {
                    id = record.Definition.Id,
                    name = record.Definition.DisplayName,
                    kind = "real",
                    has_surface = HasSurfaceSupport(record.Semantic)
                });
                if (result.Count >= PlacementDestinationLimit) return result.ToArray();
            }
            foreach (var view in views.Values)
            {
                if (view == null) continue;
                var definition = view.Definition;
                if (!IsPlacementFurniture(definition)) continue;
                if (view.Instance.ControlState == ItemControlState.HeldByCharacter) continue;
                if (!seen.Add("virtual:" + definition.Id)) continue;
                result.Add(new RuntimePlacementDestinationSnapshot
                {
                    id = definition.Id,
                    name = definition.DisplayName,
                    kind = "virtual",
                    has_surface = definition.ProvidesPlacementSurface
                });
                if (result.Count >= PlacementDestinationLimit) break;
            }
            return result.ToArray();
        }

        private static bool IsPlacementFurniture(ItemDefinition definition)
            => definition.Category != ItemCategory.Food &&
               definition.Category != ItemCategory.Tableware &&
               definition.Category != ItemCategory.Kitchenware;

        // Doors, windows and curtains are semantic items too, but they are not
        // useful destinations to put something on or beside.
        private static bool IsPlacementDestination(RealitySemantic semantic)
            => HasSurfaceSupport(semantic) || semantic == RealitySemantic.Plant ||
               semantic == RealitySemantic.Screen || semantic == RealitySemantic.Lamp ||
               semantic == RealitySemantic.AirConditioner ||
               semantic == RealitySemantic.WallArt;

        private static bool HasSurfaceSupport(RealitySemantic semantic)
            => semantic == RealitySemantic.Table || semantic == RealitySemantic.Chair ||
               semantic == RealitySemantic.Sofa || semantic == RealitySemantic.Cabinet ||
               semantic == RealitySemantic.Bed || semantic == RealitySemantic.Refrigerator ||
               semantic == RealitySemantic.WashingMachine;

        private static bool MatchesFloor(string destination)
            => Matches(destination, "地面", "地板", "地上", "地板上面");

        private static RealitySemantic? MatchSemantic(string destination)
        {
            foreach (var definition in RealitySemanticCatalog.Items)
                if (Matches(destination, definition.Id, definition.DisplayName) ||
                    Array.Exists(RealAliases(definition.Semantic),
                        alias => Matches(destination, alias)))
                    return definition.Semantic;
            return null;
        }

        private static PlacementDestinationKind InferDestinationKind(string destination)
        {
            if (Matches(destination, "现实", "真实", "房间里", "实际")) return PlacementDestinationKind.Real;
            if (Matches(destination, "虚拟", "游戏里", "模型")) return PlacementDestinationKind.Virtual;
            return PlacementDestinationKind.Any;
        }

        private static bool Matches(string destination, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(destination)) return false;
            foreach (var name in names)
                if (!string.IsNullOrWhiteSpace(name) &&
                    destination.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static string[] RealAliases(RealitySemantic semantic)
        {
            switch (semantic)
            {
                case RealitySemantic.Table: return new[] { "桌", "桌子", "餐桌", "书桌", "茶几" };
                case RealitySemantic.Chair: return new[] { "椅", "椅子", "餐椅" };
                case RealitySemantic.Sofa: return new[] { "沙发" };
                case RealitySemantic.Cabinet: return new[] { "柜", "柜子", "橱柜", "衣柜" };
                case RealitySemantic.Bed: return new[] { "床" };
                case RealitySemantic.Refrigerator: return new[] { "冰箱" };
                case RealitySemantic.WashingMachine: return new[] { "洗衣机" };
                case RealitySemantic.Plant: return new[] { "植物", "绿植", "花" };
                case RealitySemantic.Screen: return new[] { "屏幕", "电视", "显示器" };
                case RealitySemantic.Lamp: return new[] { "灯", "台灯", "落地灯" };
                case RealitySemantic.AirConditioner: return new[] { "空调" };
                case RealitySemantic.WallArt: return new[] { "画", "挂画", "装饰" };
                default: return Array.Empty<string>();
            }
        }

        private static string[] VirtualAliases(string definitionId)
        {
            switch (definitionId)
            {
                case "table": return new[] { "桌", "桌子", "餐桌" };
                case "chair": return new[] { "椅", "椅子", "餐椅" };
                case "television": return new[] { "电视", "电视机", "屏幕" };
                case "water-dispenser": return new[] { "饮水机" };
                case "radio": return new[] { "收音机" };
                case "plush-doll": return new[] { "玩偶", "娃娃", "公仔" };
                case "clock": return new[] { "时钟", "钟" };
                case "dartboard": return new[] { "飞镖盘", "飞镖" };
                case "electric-kettle": return new[] { "水壶", "电热水壶" };
                default: return Array.Empty<string>();
            }
        }

        private static void Shuffle<T>(List<T> values)
        {
            for (var index = values.Count - 1; index > 0; index--)
            {
                var other = UnityEngine.Random.Range(0, index + 1);
                var value = values[index];
                values[index] = values[other];
                values[other] = value;
            }
        }
    }
}
