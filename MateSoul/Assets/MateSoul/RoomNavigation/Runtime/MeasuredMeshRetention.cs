using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    // Removed is loss of an SDK observation, not proof that the room surface is
    // empty. Retain it until positively replaced in the same measured volume.
    public sealed class MeasuredMeshRetention
    {
        private readonly Dictionary<Guid, Bounds> bounds = new Dictionary<Guid, Bounds>();
        private readonly HashSet<Guid> unobserved = new HashSet<Guid>();
        public int RetainedCount => unobserved.Count;

        public void MarkUnobserved(Guid id)
        {
            if (bounds.ContainsKey(id)) unobserved.Add(id);
        }

        public List<Guid> Observe(PxrSpatialMeshInfo block)
        {
            var replaced = new List<Guid>();
            if (block.vertices == null || block.vertices.Length < 3) return replaced;
            var measured = new Bounds(block.position + block.rotation * block.vertices[0], Vector3.zero);
            foreach (var vertex in block.vertices) measured.Encapsulate(block.position + block.rotation * vertex);
            var coverage = measured;
            coverage.Expand(.06f);
            foreach (var id in unobserved)
                if (id != block.uuid && coverage.Contains(bounds[id].min) &&
                    coverage.Contains(bounds[id].max)) replaced.Add(id);
            foreach (var id in replaced) { bounds.Remove(id); unobserved.Remove(id); }
            bounds[block.uuid] = measured;
            unobserved.Remove(block.uuid);
            return replaced;
        }

        public void Clear() { bounds.Clear(); unobserved.Clear(); }
    }
}
