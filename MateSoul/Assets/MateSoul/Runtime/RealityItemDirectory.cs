using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealityItemRecord
    {
        private readonly HashSet<string> availableInteractions =
            new HashSet<string>(StringComparer.Ordinal);

        internal RealityItemRecord(string id, RealitySemantic semantic, Vector3 center,
            Quaternion rotation, Vector3 size, bool capturedBySdk, bool geometryInferred)
        {
            Id = id;
            Semantic = semantic;
            Center = center;
            Rotation = rotation;
            Size = size;
            CapturedBySdk = capturedBySdk;
            GeometryInferred = geometryInferred;
        }

        public string Id { get; }
        public RealitySemantic Semantic { get; }
        public RealitySemanticDefinition Definition => RealitySemanticCatalog.Get(Semantic);
        public Vector3 Center { get; internal set; }
        public Quaternion Rotation { get; internal set; }
        public Vector3 Size { get; internal set; }
        public bool CapturedBySdk { get; internal set; }
        public bool GeometryInferred { get; internal set; }
        public IEnumerable<RealityInteractionDefinition> DeclaredInteractions
            => RealityInteractionCatalog.For(Semantic);
        public IEnumerable<string> AvailableInteractions => availableInteractions;
        internal bool AddInteraction(string id) => availableInteractions.Add(id);
        internal bool RemoveInteraction(string id) => availableInteractions.Remove(id);
        internal bool HasAvailableInteractions => availableInteractions.Count > 0;
    }

    public sealed class RealityItemDirectory : IDisposable
    {
        private readonly ItemRegistry actionItems;
        private readonly Dictionary<string, RealityItemRecord> records =
            new Dictionary<string, RealityItemRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> projectedIds =
            new HashSet<string>(StringComparer.Ordinal);
        private bool disposed;

        public RealityItemDirectory(ItemRegistry registry)
        {
            actionItems = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IReadOnlyList<RealitySemanticDefinition> Definitions => RealitySemanticCatalog.Items;
        public IEnumerable<RealityItemRecord> Items => records.Values;
        public int Count => records.Count;

        public void Load(IEnumerable<RealityItemObservation> observations)
        {
            ThrowIfDisposed();
            foreach (var observation in observations ?? Array.Empty<RealityItemObservation>())
            {
                var id = "reality-" + observation.Id;
                if (records.TryGetValue(id, out var existing))
                {
                    if (existing.Semantic != observation.Semantic)
                        throw new InvalidOperationException(
                            $"Reality item '{id}' changed semantic identity.");
                    Update(existing, observation.Center, observation.Rotation, observation.Size);
                    existing.CapturedBySdk = true;
                    continue;
                }
                records.Add(id, new RealityItemRecord(id, observation.Semantic,
                    observation.Center, observation.Rotation, observation.Size, true, false));
            }
        }

        public void PublishInteraction(string id, RealitySemantic semantic, Vector3 center,
            Quaternion rotation, Vector3 size, bool geometryInferred, string interactionId)
        {
            ThrowIfDisposed();
            var interaction = RealityInteractionCatalog.Get(interactionId);
            if (string.IsNullOrWhiteSpace(id) || size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new ArgumentException("A validated reality target requires an ID and positive size.");
            if (interaction == null || !interaction.Supports(semantic))
                throw new InvalidOperationException(
                    $"Interaction '{interactionId}' is not mapped to {semantic}.");
            var hasActionItem = actionItems.TryGet(id, out _);
            if (hasActionItem && !projectedIds.Contains(id))
                throw new InvalidOperationException(
                    $"Reality item '{id}' collides with another item registry owner.");

            if (!records.TryGetValue(id, out var record))
            {
                record = new RealityItemRecord(id, semantic, center, rotation, size,
                    false, geometryInferred);
                records.Add(id, record);
            }
            else if (record.Semantic != semantic)
                throw new InvalidOperationException($"Reality item '{id}' changed semantic identity.");

            Update(record, center, rotation, size);
            record.GeometryInferred |= geometryInferred;
            if (!record.AddInteraction(interactionId)) return;

            if (!hasActionItem)
            {
                actionItems.Register(new ItemInstance(id, "reality-" + record.Definition.Id,
                    "现实" + record.Definition.DisplayName, new[] { interactionId }));
                projectedIds.Add(id);
            }
            else actionItems.AddCapability(id, interactionId);
        }

        public void WithdrawInteraction(string id, string interactionId)
        {
            if (disposed || !records.TryGetValue(id, out var record) ||
                !record.RemoveInteraction(interactionId)) return;
            actionItems.RemoveCapability(id, interactionId);
            if (!record.HasAvailableInteractions)
            {
                actionItems.Remove(id);
                projectedIds.Remove(id);
                if (!record.CapturedBySdk) records.Remove(id);
            }
        }

        public bool TryGet(string id, out RealityItemRecord item)
            => records.TryGetValue(id, out item);

        private static void Update(RealityItemRecord record, Vector3 center,
            Quaternion rotation, Vector3 size)
        {
            record.Center = center;
            record.Rotation = rotation;
            record.Size = size;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RealityItemDirectory));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var id in projectedIds.ToArray()) actionItems.Remove(id);
            projectedIds.Clear();
            records.Clear();
        }
    }
}
