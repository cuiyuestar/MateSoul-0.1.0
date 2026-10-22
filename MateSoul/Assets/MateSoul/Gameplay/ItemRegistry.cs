using System;
using System.Collections.Generic;

namespace MateSoul.Gameplay
{
    public enum ItemControlState
    {
        Available, PlayerHeld, PlayerUsing, ReservedByCharacter,
        HeldByCharacter, StagedForAction, InUseByCharacter, Removed
    }

    public enum ItemReleaseReason { Completed, Cancelled, Expired, Removed, SessionEnded }

    public sealed class ItemInstance
    {
        private readonly HashSet<string> capabilities;
        internal ItemReservation Reservation;
        public string Id { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public string StateId { get; private set; }
        public long Generation { get; internal set; }
        public bool IsPresent { get; internal set; } = true;
        public bool IsOccupied => Reservation != null;
        public ItemControlState ControlState { get; internal set; } = ItemControlState.Available;
        public Guid InteractionId => Reservation?.InteractionId ?? Guid.Empty;
        public bool IsCharacterOwned => ControlState == ItemControlState.ReservedByCharacter ||
            ControlState == ItemControlState.HeldByCharacter ||
            ControlState == ItemControlState.StagedForAction ||
            ControlState == ItemControlState.InUseByCharacter;
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
        private readonly ItemRegistry registry;
        private Action<ItemReleaseReason> releaseHandler;
        private bool released;
        internal readonly ItemInstance Item;
        private readonly string capability;
        public object Owner { get; }
        public Guid InteractionId { get; }
        internal double ExpiresAt;
        public bool IsValid => !released && Item.IsPresent && Item.Supports(capability) &&
                               ReferenceEquals(Item.Reservation, this);
        public ItemControlState State => IsValid ? Item.ControlState : ItemControlState.Removed;

        internal ItemReservation(ItemRegistry registry, ItemInstance item, string capability,
            object owner, Guid interactionId, double expiresAt)
        {
            this.registry = registry; Item = item; this.capability = capability;
            Owner = owner; InteractionId = interactionId; ExpiresAt = expiresAt;
        }

        // Exactly one runtime owner restores physics before the authority becomes available.
        public void OnRelease(Action<ItemReleaseReason> handler)
        {
            if (!IsValid || releaseHandler != null || handler == null)
                throw new InvalidOperationException("A live reservation permits one release handler.");
            releaseHandler = handler;
        }

        public bool Transition(ItemControlState expected, ItemControlState next)
        {
            if (!IsValid || Item.ControlState != expected || !Item.IsCharacterOwned) return false;
            if (next != ItemControlState.HeldByCharacter && next != ItemControlState.StagedForAction &&
                next != ItemControlState.InUseByCharacter) return false;
            Item.ControlState = next;
            return true;
        }

        public void Heartbeat(double now, double graceSeconds = 4)
        {
            if (!IsValid) return;
            if (double.IsNaN(now) || double.IsInfinity(now) || graceSeconds <= 0 ||
                double.IsNaN(graceSeconds) || double.IsInfinity(graceSeconds))
                throw new ArgumentOutOfRangeException(nameof(now));
            ExpiresAt = now + graceSeconds;
        }

        internal void Release(ItemReleaseReason reason)
        {
            if (released) return;
            released = true;
            try { releaseHandler?.Invoke(reason); }
            finally
            {
                releaseHandler = null;
                if (ReferenceEquals(Item.Reservation, this))
                {
                    Item.Reservation = null;
                    Item.ControlState = Item.IsPresent
                        ? ItemControlState.Available : ItemControlState.Removed;
                }
            }
        }

        public void Dispose() => registry.Release(this, ItemReleaseReason.Cancelled);
        public void Complete() => registry.Release(this, ItemReleaseReason.Completed);
    }

    public readonly struct ItemReservationRequest
    {
        public readonly ItemInstance Item;
        public readonly string Capability;
        public ItemReservationRequest(ItemInstance item, string capability)
        { Item = item; Capability = capability; }
    }

    public sealed class ItemRegistry : IDisposable
    {
        private readonly Dictionary<string, ItemInstance> items =
            new Dictionary<string, ItemInstance>(StringComparer.Ordinal);
        private long generation;
        private readonly List<ItemReservation> expired = new List<ItemReservation>();
        public event Action<Exception> CleanupFailed;
        public IEnumerable<ItemInstance> Items => items.Values;
        public int Count => items.Count;
        public void Register(ItemInstance item)
        {
            items.Add(item.Id, item);
            item.Generation = ++generation;
        }
        public bool TryGet(string id, out ItemInstance item)
        {
            item = null;
            return id != null && items.TryGetValue(id, out item);
        }

        public ItemReservation Reserve(string id, string capability, object owner)
        {
            return TryGet(id, out var item)
                ? TryReserve(item, capability, owner, Guid.NewGuid()) : null;
        }

        public ItemReservation TryReserve(ItemInstance item, string capability, object owner,
            Guid interactionId, ItemControlState state = ItemControlState.ReservedByCharacter)
        {
            if (owner == null || interactionId == Guid.Empty)
                throw new ArgumentException("Reservation owner and run ID are required.");
            if (state != ItemControlState.ReservedByCharacter &&
                state != ItemControlState.PlayerHeld && state != ItemControlState.PlayerUsing)
                throw new ArgumentException("Invalid initial control state.");
            if (!IsCurrent(item) || !item.Supports(capability)) return null;
            if (item.Reservation != null)
                return item.Reservation.InteractionId == interactionId &&
                       ReferenceEquals(item.Reservation.Owner, owner) &&
                       item.ControlState == state ? item.Reservation : null;
            var lease = new ItemReservation(this, item, capability, owner, interactionId,
                double.PositiveInfinity);
            item.Reservation = lease;
            item.ControlState = state;
            return lease;
        }

        public bool TryReserveBundle(IReadOnlyList<ItemReservationRequest> requests,
            object owner, Guid interactionId, out ItemReservation[] leases)
        {
            leases = null;
            if (owner == null || interactionId == Guid.Empty || requests == null || requests.Count == 0)
                throw new ArgumentException("A bundle requires an owner, run ID and items.");
            var unique = new HashSet<ItemInstance>();
            // Main-thread check/commit contains no callbacks, awaits or partial acquisitions.
            foreach (var request in requests)
                if (!IsCurrent(request.Item) || !unique.Add(request.Item) ||
                    !request.Item.Supports(request.Capability) ||
                    request.Item.IsOccupied && (request.Item.InteractionId != interactionId ||
                        !ReferenceEquals(request.Item.Reservation.Owner, owner))) return false;
            leases = new ItemReservation[requests.Count];
            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                leases[i] = request.Item.Reservation ??
                    TryReserve(request.Item, request.Capability, owner, interactionId);
            }
            return true;
        }

        public bool IsCurrent(ItemInstance item) => item != null && item.IsPresent &&
            items.TryGetValue(item.Id, out var current) && ReferenceEquals(current, item);

        internal void Release(ItemReservation lease, ItemReleaseReason reason)
        {
            try { lease.Release(reason); }
            catch (Exception error) { CleanupFailed?.Invoke(error); }
        }

        public void Tick(double now)
        {
            expired.Clear();
            foreach (var item in items.Values)
                if (item.Reservation != null &&
                    (!item.Reservation.IsValid || item.Reservation.ExpiresAt <= now))
                    expired.Add(item.Reservation);
            foreach (var lease in expired) Release(lease, ItemReleaseReason.Expired);
            expired.Clear();
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
            if (item.Reservation != null) Release(item.Reservation, ItemReleaseReason.Removed);
            item.ControlState = ItemControlState.Removed;
            items.Remove(id);
        }

        public void Dispose()
        {
            foreach (var item in items.Values)
            {
                item.IsPresent = false;
                if (item.Reservation != null) Release(item.Reservation, ItemReleaseReason.SessionEnded);
                item.ControlState = ItemControlState.Removed;
            }
            items.Clear();
        }
    }
}
