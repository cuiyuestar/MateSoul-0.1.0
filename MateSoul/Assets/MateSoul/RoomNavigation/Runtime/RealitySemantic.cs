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

        private static RealitySemanticDefinition Define(RealitySemantic semantic, string id,
            string displayName, RealitySemanticRole role, RealityGeometryKind geometry)
            => new RealitySemanticDefinition(semantic, id, displayName, role, geometry);
    }
}
