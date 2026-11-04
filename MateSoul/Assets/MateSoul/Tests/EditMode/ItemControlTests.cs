using System;
using MateSoul.Gameplay;
using NUnit.Framework;

namespace MateSoul.Tests
{
    public sealed class ItemControlTests
    {
        private static ItemInstance Add(ItemRegistry registry, string id)
        {
            var item = new ItemInstance(id, "test-item", id, new[] { "pickup" });
            registry.Register(item);
            return item;
        }

        [Test]
        public void PlayerAndCharacterCompeteThroughTheSameAuthority()
        {
            using (var registry = new ItemRegistry())
            {
                var item = Add(registry, "fruit");
                var owner = new object();
                var run = Guid.NewGuid();
                var player = registry.TryReserve(item, null, owner, run, ItemControlState.PlayerHeld);
                Assert.That(registry.TryReserve(item, "pickup", new object(), Guid.NewGuid()), Is.Null);
                Assert.That(registry.TryReserve(item, null, owner, run, ItemControlState.PlayerHeld),
                    Is.SameAs(player));
                player.Complete();
                var character = registry.TryReserve(item, "pickup", owner, Guid.NewGuid());
                Assert.That(character, Is.Not.Null);
                Assert.That(registry.TryReserve(item, null, new object(), Guid.NewGuid(),
                    ItemControlState.PlayerHeld), Is.Null);
            }
        }

        [Test]
        public void FailedBundleLeavesNoPartialLocksAndCannotReviveOldInstances()
        {
            using (var registry = new ItemRegistry())
            {
                var first = Add(registry, "plate");
                var second = Add(registry, "knife");
                var other = registry.TryReserve(second, "pickup", this, Guid.NewGuid());
                var requests = new[]
                {
                    new ItemReservationRequest(first, "pickup"),
                    new ItemReservationRequest(second, "pickup")
                };
                Assert.That(registry.TryReserveBundle(requests, this, Guid.NewGuid(), out _), Is.False);
                Assert.That(first.IsOccupied, Is.False);
                other.Dispose();
                registry.Remove("knife");
                var replacement = Add(registry, "knife");
                Assert.That(registry.TryReserveBundle(requests, this, Guid.NewGuid(), out _), Is.False);
                Assert.That(replacement.Generation, Is.GreaterThan(second.Generation));
                Assert.That(replacement.IsOccupied, Is.False);
            }
        }

        [Test]
        public void HeldToStagedTransferNeverOpensPlayerAccess()
        {
            using (var registry = new ItemRegistry())
            {
                var item = Add(registry, "fruit");
                var lease = registry.TryReserve(item, "pickup", this, Guid.NewGuid());
                Assert.That(lease.Transition(ItemControlState.ReservedByCharacter,
                    ItemControlState.HeldByCharacter), Is.True);
                Assert.That(lease.Transition(ItemControlState.HeldByCharacter,
                    ItemControlState.StagedForAction), Is.True);
                Assert.That(item.IsCharacterOwned && item.IsOccupied, Is.True);
                Assert.That(registry.TryReserve(item, null, new object(), Guid.NewGuid(),
                    ItemControlState.PlayerHeld), Is.Null);
                lease.Complete();
                Assert.That(item.ControlState, Is.EqualTo(ItemControlState.Available));
            }
        }

        [Test]
        public void ExpiryRestoresBeforeUnlockAndCleanupFailureCannotStrandOtherLeases()
        {
            using (var registry = new ItemRegistry())
            {
                var first = Add(registry, "first");
                var second = Add(registry, "second");
                var a = registry.TryReserve(first, "pickup", this, Guid.NewGuid());
                var b = registry.TryReserve(second, "pickup", this, Guid.NewGuid());
                var calls = 0;
                a.OnRelease(reason =>
                {
                    Assert.That(first.IsOccupied, Is.True);
                    Assert.That(reason, Is.EqualTo(ItemReleaseReason.Expired));
                    calls++;
                    throw new InvalidOperationException("restore failure");
                });
                b.OnRelease(_ => calls++);
                a.Heartbeat(0, 1);
                b.Heartbeat(0, 1);
                registry.Tick(2);
                a.Dispose();
                b.Dispose();
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(first.IsOccupied || second.IsOccupied, Is.False);
            }
        }

        [Test]
        public void RepeatedCancellationAndStaleHandlesNeverReleaseNewOwner()
        {
            using (var registry = new ItemRegistry())
            {
                var item = Add(registry, "fruit");
                for (var i = 0; i < 200; i++)
                {
                    var old = registry.TryReserve(item, "pickup", this, Guid.NewGuid());
                    old.Dispose();
                    var current = registry.TryReserve(item, "pickup", this, Guid.NewGuid());
                    old.Dispose();
                    Assert.That(current.IsValid, Is.True);
                    current.Dispose();
                }
                Assert.That(item.IsOccupied, Is.False);
            }
        }
    }
}
