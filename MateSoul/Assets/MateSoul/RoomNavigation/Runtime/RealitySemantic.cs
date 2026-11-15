using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;

namespace MateSoul.RoomNavigation
{
    public enum RealitySemantic
    {
        Unknown = 0,
        Floor = 1,
        Ceiling = 2,
        Wall = 3,
        Door = 4,
        Window = 5,
        Opening = 6,
        Table = 7,
        Sofa = 8,
        Chair = 9,
        Human = 10,
        Curtain = 13,
        Cabinet = 14,
        Bed = 15,
        Plant = 16,
        Screen = 17,
        VirtualWall = 18,
        Refrigerator = 19,
        WashingMachine = 20,
        AirConditioner = 21,
        Lamp = 22,
        WallArt = 23,
        Stairway = 24
    }

    // Static objects never move in practice, so the room calibration is authoritative
    // and the live mesh is only a sanity check. Dynamic objects are re-localised from
    // the live mesh every time and fall back to calibration when it goes stale.
    public static class RealitySemanticMotion
    {
        public static bool IsDynamic(RealitySemantic semantic)
        {
            switch (semantic)
            {
                case RealitySemantic.Chair:
                case RealitySemantic.Human:
                case RealitySemantic.Unknown:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsStatic(RealitySemantic semantic) => !IsDynamic(semantic);
    }

    public enum RealitySemanticRole
    {
        Unknown,
        Surface,
        Item,
        Person,
        SyntheticBoundary
    }

    public enum RealityGeometryKind
    {
        None,
        Polygon,
        Box2D,
        Box3D
    }

    public sealed class RealitySemanticDefinition
    {
        public RealitySemanticDefinition(RealitySemantic semantic, string id, string displayName,
            RealitySemanticRole role, RealityGeometryKind geometry)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Reality semantic definitions require an ID and display name.");
            Semantic = semantic;
            Id = id;
            DisplayName = displayName;
            Role = role;
            Geometry = geometry;
        }

        public RealitySemantic Semantic { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public RealitySemanticRole Role { get; }
        public RealityGeometryKind Geometry { get; }
        public bool IsItem => Role == RealitySemanticRole.Item;
    }

    public static class RealitySemanticCatalog
    {
        private static readonly RealitySemanticDefinition[] definitions =
        {
            Define(RealitySemantic.Unknown, "unknown", "未知", RealitySemanticRole.Unknown, RealityGeometryKind.None),
            Define(RealitySemantic.Floor, "floor", "地面", RealitySemanticRole.Surface, RealityGeometryKind.Polygon),
            Define(RealitySemantic.Ceiling, "ceiling", "天花板", RealitySemanticRole.Surface, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Wall, "wall", "墙面", RealitySemanticRole.Surface, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Door, "door", "门", RealitySemanticRole.Item, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Window, "window", "窗户", RealitySemanticRole.Item, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Opening, "opening", "开口", RealitySemanticRole.Surface, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Table, "table", "桌子", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Sofa, "sofa", "沙发", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Chair, "chair", "椅子", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Human, "human", "人物", RealitySemanticRole.Person, RealityGeometryKind.None),
            Define(RealitySemantic.Curtain, "curtain", "窗帘", RealitySemanticRole.Item, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Cabinet, "cabinet", "柜子", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Bed, "bed", "床", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Plant, "plant", "植物", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Screen, "screen", "屏幕", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.VirtualWall, "virtual-wall", "虚拟墙",
                RealitySemanticRole.SyntheticBoundary, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Refrigerator, "refrigerator", "冰箱",
                RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.WashingMachine, "washing-machine", "洗衣机",
                RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.AirConditioner, "air-conditioner", "空调",
                RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.Lamp, "lamp", "灯", RealitySemanticRole.Item, RealityGeometryKind.Box3D),
            Define(RealitySemantic.WallArt, "wall-art", "墙面装饰",
                RealitySemanticRole.Item, RealityGeometryKind.Box2D),
            Define(RealitySemantic.Stairway, "stairway", "楼梯",
                RealitySemanticRole.Surface, RealityGeometryKind.None)
        };

        private static readonly IReadOnlyList<RealitySemanticDefinition> all =
            Array.AsReadOnly(definitions);
        private static readonly IReadOnlyList<RealitySemanticDefinition> items =
            Array.AsReadOnly(definitions.Where(definition => definition.IsItem).ToArray());
        private static readonly Dictionary<RealitySemantic, RealitySemanticDefinition> bySemantic =
            definitions.ToDictionary(definition => definition.Semantic);
        // The AI speaks in room vocabulary ("table", "cabinet", 书桌, 衣柜) rather
        // than echoing instance ids, so every semantic also accepts the names
        // people and the model actually use.
        private static readonly Dictionary<string, RealitySemantic> vocabulary =
            BuildVocabulary();

        static RealitySemanticCatalog()
        {
            var sdkValues = ((PxrSemanticLabel[])Enum.GetValues(typeof(PxrSemanticLabel)))
                .Select(value => (int)value).OrderBy(value => value).ToArray();
            var catalogValues = definitions.Select(definition => (int)definition.Semantic)
                .OrderBy(value => value).ToArray();
            if (!sdkValues.SequenceEqual(catalogValues))
                throw new InvalidOperationException(
                    "RealitySemanticCatalog must explicitly classify every PICO semantic label.");
        }

        public static IReadOnlyList<RealitySemanticDefinition> All => all;
        public static IReadOnlyList<RealitySemanticDefinition> Items => items;

        public static RealitySemantic FromPico(PxrSemanticLabel semantic)
            => bySemantic.ContainsKey((RealitySemantic)(int)semantic)
                ? (RealitySemantic)(int)semantic
                : RealitySemantic.Unknown;

        public static RealitySemanticDefinition Get(RealitySemantic semantic)
            => bySemantic.TryGetValue(semantic, out var definition)
                ? definition
                : bySemantic[RealitySemantic.Unknown];

        public static bool TryParse(string value, out RealitySemantic semantic)
        {
            semantic = RealitySemantic.Unknown;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var key = Normalize(value);
            if (vocabulary.TryGetValue(key, out semantic)) return true;
            // Reality item groups are reported as "reality-chair" / "现实椅子";
            // accept those echoes as the bare semantic name too.
            foreach (var prefix in new[] { "reality ", "real ", "现实" })
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (vocabulary.TryGetValue(key.Substring(prefix.Length).Trim(), out semantic))
                    return true;
            }
            return false;
        }

        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');

        private static Dictionary<string, RealitySemantic> BuildVocabulary()
        {
            var map = new Dictionary<string, RealitySemantic>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                map[Normalize(definition.Id)] = definition.Semantic;
                map[Normalize(definition.DisplayName)] = definition.Semantic;
            }
            Add(map, RealitySemantic.Table,
                "desk", "worktable", "dining table", "coffee table", "书桌", "桌子", "餐桌", "茶几", "桌");
            Add(map, RealitySemantic.Cabinet,
                "wardrobe", "closet", "cupboard", "dresser", "sideboard", "bookshelf", "nightstand",
                "衣柜", "书柜", "橱柜", "储物柜", "床头柜", "柜");
            Add(map, RealitySemantic.Chair, "seat", "stool", "座椅", "凳子", "椅");
            Add(map, RealitySemantic.Bed, "床铺", "大床");
            Add(map, RealitySemantic.Sofa, "couch", "长沙发");
            Add(map, RealitySemantic.Door, "房门");
            Add(map, RealitySemantic.Window, "窗");
            Add(map, RealitySemantic.Curtain, "窗帘布");
            Add(map, RealitySemantic.Plant, "potted plant", "盆栽", "绿植", "花");
            Add(map, RealitySemantic.Screen, "tv", "television", "monitor", "电视", "电视机", "显示器");
            Add(map, RealitySemantic.Refrigerator, "fridge", "电冰箱");
            Add(map, RealitySemantic.WashingMachine, "washer", "滚筒洗衣机");
            Add(map, RealitySemantic.AirConditioner, "ac", "aircon", "冷气");
            Add(map, RealitySemantic.Lamp, "light", "台灯", "落地灯");
            Add(map, RealitySemantic.WallArt, "painting", "picture", "挂画", "装饰画", "壁画");
            Add(map, RealitySemantic.Stairway, "stairs", "staircase", "阶梯");
            return map;
        }

        private static void Add(Dictionary<string, RealitySemantic> map,
            RealitySemantic semantic, params string[] names)
        {
            foreach (var name in names) map[Normalize(name)] = semantic;
        }

        private static RealitySemanticDefinition Define(RealitySemantic semantic, string id,
            string displayName, RealitySemanticRole role, RealityGeometryKind geometry)
            => new RealitySemanticDefinition(semantic, id, displayName, role, geometry);
    }
}
