using System;
using System.Collections.Generic;

namespace MateSoul.Gameplay
{
    public sealed class ItemInstance
    {
        private readonly HashSet<string> capabilities;
        internal object Occupant;
        public string Id { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public string StateId { get; private set; }
        public bool IsPresent { get; internal set; } = true;
        public bool IsOccupied => Occupant != null;
        public IEnumerable<string> Capabilities => capabilities;

        public ItemInstance(string id, string definitionId, string displayName,
            IEnumerable<string> capabilities, string stateId = "default")
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("Item instance and definition IDs are required.");
            Id = id;
            DefinitionId = definitionId;
            DisplayName = displayName;
            this.capabilities = new HashSet<string>(capabilities ?? Array.Empty<string>(), StringComparer.Ordinal);
            StateId = stateId;
        }

        public bool Supports(string capability) => capability == null || capabilities.Contains(capability);
        internal void AddCapability(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
                throw new ArgumentException("Capability ID is required.", nameof(capability));
            capabilities.Add(capability);
        }
        internal void RemoveCapability(string capability) => capabilities.Remove(capability);

        public void SetState(string stateId)
        {
            if (!IsPresent) throw new InvalidOperationException("The item has been removed.");
            StateId = stateId;
        }
    }

    public sealed class ItemReservation : IDisposable
    {
        private ItemInstance item;
        private readonly object owner;
        private readonly string capability;
        internal ItemReservation(ItemInstance item, string capability, object owner)
        { this.item = item; this.capability = capability; this.owner = owner; }
        public bool IsValid => item != null && item.IsPresent && item.Supports(capability) &&
                               ReferenceEquals(item.Occupant, owner);
        public void Dispose()
        {
            if (item == null) return;
            if (ReferenceEquals(item.Occupant, owner)) item.Occupant = null;
            item = null;
        }
    }

    public sealed class ItemRegistry : IDisposable
    {
        private readonly Dictionary<string, ItemInstance> items =
            new Dictionary<string, ItemInstance>(StringComparer.Ordinal);
        public IEnumerable<ItemInstance> Items => items.Values;
        public int Count => items.Count;
        public void Register(ItemInstance item) { items.Add(item.Id, item); }
        public bool TryGet(string id, out ItemInstance item)
        {
            item = null;
            return id != null && items.TryGetValue(id, out item);
        }

        public ItemReservation Reserve(string id, string capability, object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!TryGet(id, out var item) || !item.Supports(capability) || item.IsOccupied)
                return null;
            item.Occupant = owner;
            return new ItemReservation(item, capability, owner);
        }

        public void AddCapability(string id, string capability)
        {
            if (!TryGet(id, out var item))
                throw new KeyNotFoundException($"Item '{id}' is not registered.");
            item.AddCapability(capability);
        }

        public void RemoveCapability(string id, string capability)
        {
            if (TryGet(id, out var item)) item.RemoveCapability(capability);
        }

        public void Remove(string id)
        {
            if (!items.TryGetValue(id, out var item)) return;
            item.IsPresent = false;
            item.Occupant = null;
            items.Remove(id);
        }

        public void Dispose()
        {
            foreach (var item in items.Values)
            {
                item.IsPresent = false;
                item.Occupant = null;
            }
            items.Clear();
        }
    }
}
