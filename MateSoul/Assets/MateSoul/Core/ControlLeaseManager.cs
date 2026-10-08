using System;
using System.Collections.Generic;

namespace MateSoul.Core
{
    // A lease lives exactly as long as its action, including its exit phase.
    // Timeouts belong to the coordinator; there is no second lease clock.
    public sealed class ControlLeaseManager
    {
        private readonly Dictionary<Guid, ControlChannels> leases = new Dictionary<Guid, ControlChannels>();
        public int Count => leases.Count;

        public bool IsOccupied(ControlChannels channels)
        {
            foreach (var held in leases.Values)
                if ((held & channels) != 0) return true;
            return false;
        }

        internal bool TryAcquire(Guid owner, ControlChannels channels)
        {
            if (IsOccupied(channels)) return false;
            leases.Add(owner, channels);
            return true;
        }

        internal void Release(Guid owner) { leases.Remove(owner); }
    }
}
