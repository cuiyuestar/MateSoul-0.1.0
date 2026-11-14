using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    // Removed is loss of an SDK observation, not proof that the room surface is
    // empty. Retain it until positively replaced in the same measured volume, but
    // never forever: a block that stays unobserved for too long is stale room
    // geometry and must stop writing depth, otherwise it becomes an invisible
    // wall that slices through the avatar after re-calibration.
    public sealed class MeasuredMeshRetention
    {
        private readonly Dictionary<Guid, Bounds> bounds = new Dictionary<Guid, Bounds>();
        private readonly Dictionary<Guid, float> unobservedSince = new Dictionary<Guid, float>();
        public int RetainedCount => unobservedSince.Count;

        public void MarkUnobserved(Guid id, float now)
        {
            if (!bounds.ContainsKey(id)) return;
            // Removed is re-reported on every cache refresh; keep the first stamp
            // so repeated reports cannot postpone expiry forever.
            if (!unobservedSince.ContainsKey(id)) unobservedSince[id] = now;
        }

        public List<Guid> Observe(PxrSpatialMeshInfo block, float now)
        {
            var replaced = new List<Guid>();
            if (block.vertices == null || block.vertices.Length < 3) return replaced;
            var measured = new Bounds(block.position + block.rotation * block.vertices[0], Vector3.zero);
            foreach (var vertex in block.vertices) measured.Encapsulate(block.position + block.rotation * vertex);
            var coverage = measured;
            coverage.Expand(.06f);
            foreach (var id in unobservedSince.Keys)
                if (id != block.uuid && coverage.Contains(bounds[id].min) &&
                    coverage.Contains(bounds[id].max)) replaced.Add(id);
            foreach (var id in replaced) { bounds.Remove(id); unobservedSince.Remove(id); }
            bounds[block.uuid] = measured;
            unobservedSince.Remove(block.uuid);
            return replaced;
        }

        // Collects blocks that stayed unobserved longer than the lifetime. The
        // caller owns their depth/collision entries and must drop them.
        public void Expire(float now, float lifetime, List<Guid> expired)
        {
            expired.Clear();
            foreach (var pair in unobservedSince)
                if (now - pair.Value >= lifetime) expired.Add(pair.Key);
            foreach (var id in expired)
            {
                bounds.Remove(id);
                unobservedSince.Remove(id);
            }
        }

        public void Clear() { bounds.Clear(); unobservedSince.Clear(); }
    }
}
