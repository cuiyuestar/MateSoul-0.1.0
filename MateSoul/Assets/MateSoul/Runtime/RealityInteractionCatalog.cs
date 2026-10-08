using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.RoomNavigation;

namespace MateSoul.Runtime
{
    public static class RealityInteractionIds
    {
        public const string Sit = "sit";
        public const string Lie = "lie";
    }

    public sealed class RealityInteractionDefinition
    {
        private readonly HashSet<RealitySemantic> semantics;

        public RealityInteractionDefinition(string id, string displayName, string description,
            params RealitySemantic[] supportedSemantics)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName) ||
                supportedSemantics == null || supportedSemantics.Length == 0)
                throw new ArgumentException("Reality interactions require an ID, name, and target semantics.");
            Id = id;
            DisplayName = displayName;
            Description = description;
            semantics = new HashSet<RealitySemantic>(supportedSemantics);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IEnumerable<RealitySemantic> SupportedSemantics => semantics;
        public bool Supports(RealitySemantic semantic) => semantics.Contains(semantic);
    }

    public static class RealityInteractionCatalog
    {
        private static readonly RealityInteractionDefinition[] definitions =
        {
            new RealityInteractionDefinition(RealityInteractionIds.Sit, "坐下",
                "Walk to a real chair and sit until asked to stand", RealitySemantic.Chair),
            new RealityInteractionDefinition(RealityInteractionIds.Lie, "躺下",
                "Walk to a real bed and lie down until asked to get up", RealitySemantic.Bed)
        };

        private static readonly IReadOnlyList<RealityInteractionDefinition> all =
            Array.AsReadOnly(definitions);
        private static readonly Dictionary<string, RealityInteractionDefinition> byId =
            definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

        public static IReadOnlyList<RealityInteractionDefinition> All => all;
        public static RealityInteractionDefinition Get(string id)
            => id != null && byId.TryGetValue(id, out var definition) ? definition : null;
        public static IEnumerable<RealityInteractionDefinition> For(RealitySemantic semantic)
            => definitions.Where(definition => definition.Supports(semantic));
    }
}
