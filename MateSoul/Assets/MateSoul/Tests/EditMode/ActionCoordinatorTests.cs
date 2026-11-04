using System;
using MateSoul.Core;
using NUnit.Framework;

namespace MateSoul.Tests
{
    public sealed class ActionCoordinatorTests
    {
        private sealed class ProbeAction : IAvatarAction
        {
            public string AdmissionError;
            public int Starts, Disposals, Ticks, Cancels;
            public bool Complete, ExitReady = true, ThrowStart, ThrowTick, ThrowDispose;
            public string Check() => AdmissionError;
            public void Start() { Starts++; if (ThrowStart) throw new InvalidOperationException("start"); }
            public ActionProgress Tick(float dt)
            {
                Ticks++;
                if (ThrowTick) throw new InvalidOperationException("tick");
                return Complete || (Cancels > 0 && ExitReady) ? ActionProgress.Succeeded : ActionProgress.Running;
            }
            public void Cancel(ActionStopReason reason) { Cancels++; }
            public void Dispose() { Disposals++; if (ThrowDispose) throw new InvalidOperationException("cleanup"); }
        }

        private ActionCatalog catalog;
        private AvatarActionCoordinator coordinator;
        [SetUp] public void SetUp() { catalog = new ActionCatalog(); coordinator = new AvatarActionCoordinator(catalog); }
        [TearDown] public void TearDown() { coordinator.Dispose(); }

        private ProbeAction Register(string id, ControlChannels channels = ControlChannels.Locomotion,
            float timeout = 30)
        {
            var probe = new ProbeAction();
            catalog.Register(new ActionDefinition(id, id, channels, _ => probe, timeout));
            return probe;
        }

        private ActionHandle Submit(string id, IntentSource source = IntentSource.UI)
            => coordinator.Submit(new AvatarIntent(id, source));

        [Test]
        public void FaceAndMovementRunInParallelButFullBodyConflictsWithArms()
        {
            Register("walk");
            Register("talk", ControlChannels.Face);
            Register("pose", ControlChannels.FullBody);
            Register("hand", ControlChannels.LeftArm);
            Assert.That(Submit("walk").State, Is.EqualTo(ActionState.Running));
            Assert.That(Submit("talk").State, Is.EqualTo(ActionState.Running));
            Assert.That(Submit("pose").State, Is.EqualTo(ActionState.Running));
            Assert.That(Submit("hand").State, Is.EqualTo(ActionState.Rejected));
            Assert.That(coordinator.Leases.Count, Is.EqualTo(3));
        }

        [Test]
        public void PreemptionWaitsForExitBeforeTransferringTheMotor()
        {
            var walking = Register("walk");
            walking.ExitReady = false;
            var hand = Register("hand");
            var old = Submit("walk", IntentSource.Autonomous);
            var next = Submit("hand", IntentSource.Player);
            Assert.That(old.State, Is.EqualTo(ActionState.Cancelling));
            Assert.That(next.State, Is.EqualTo(ActionState.Queued));
            coordinator.Tick(.1f);
            Assert.That(hand.Starts, Is.Zero);
            walking.ExitReady = true;
            coordinator.Tick(.1f);
            Assert.That(old.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(next.State, Is.EqualTo(ActionState.Running));
            Assert.That(walking.Disposals, Is.EqualTo(1));
            Assert.That(coordinator.Leases.Count, Is.EqualTo(1));
        }

        [TestCase(IntentSource.Autonomous)]
        [TestCase(IntentSource.AI)]
        [TestCase(IntentSource.UI)]
        [TestCase(IntentSource.Player)]
        [TestCase(IntentSource.System)]
        public void EveryFormalActionPreemptsAmbientFullBodyAttention(
            IntentSource source)
        {
            var ambient = Register("attention", ControlChannels.All);
            Register("action", ControlChannels.Face);
            var attention = Submit("attention", IntentSource.Ambient);
            var action = Submit("action", source);

            Assert.That(attention.State, Is.EqualTo(ActionState.Cancelling));
            Assert.That(ambient.Cancels, Is.EqualTo(1));
            Assert.That(action.State, Is.EqualTo(ActionState.Queued));
            coordinator.Tick(.1f);
            Assert.That(attention.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(action.State, Is.EqualTo(ActionState.Running));
        }

        [Test]
        public void MultiChannelRequestDoesNotPartiallyAcquireOrInterruptOnRejection()
        {
            var move = Register("move", ControlChannels.Locomotion);
            var hand = Register("hand", ControlChannels.LeftArm);
            Register("combined", ControlChannels.LeftArm | ControlChannels.Locomotion);
            var low = Submit("move", IntentSource.Autonomous);
            Submit("hand", IntentSource.Player);
            Assert.That(Submit("combined").State, Is.EqualTo(ActionState.Rejected));
            Assert.That(low.State, Is.EqualTo(ActionState.Running));
            Assert.That(move.Cancels + hand.Cancels, Is.Zero);
        }

        [Test]
        public void PendingActionRechecksTargetAfterPreviousOwnerExits()
        {
            Register("walk");
            var target = Register("visit");
            Submit("walk", IntentSource.Autonomous);
            var next = Submit("visit");
            target.AdmissionError = "target removed";
            coordinator.Tick(.1f);
            Assert.That(next.State, Is.EqualTo(ActionState.Rejected));
            Assert.That(target.Starts, Is.Zero);
            Assert.That(coordinator.Leases.Count, Is.Zero);
        }

        [TestCase("start")]
        [TestCase("tick")]
        [TestCase("cleanup")]
        public void ExceptionsEndTheActionAndReleaseItsResourcesExactlyOnce(string phase)
        {
            var action = Register("failing");
            action.ThrowStart = phase == "start";
            action.ThrowTick = phase == "tick";
            action.ThrowDispose = phase == "cleanup";
            action.Complete = true;
            var handle = Submit("failing");
            coordinator.Tick(.1f);
            coordinator.StopAll(ActionStopReason.Paused);
            Assert.That(handle.State, Is.EqualTo(ActionState.Failed));
            Assert.That(action.Disposals, Is.EqualTo(1));
            Assert.That(coordinator.Leases.Count, Is.Zero);
        }

        [Test]
        public void FailedAdmissionAndThrowingCleanupDoNotDisposeTwice()
        {
            var action = Register("unavailable");
            action.AdmissionError = "missing target";
            action.ThrowDispose = true;
            Assert.That(Submit("unavailable").State, Is.EqualTo(ActionState.Rejected));
            Assert.That(action.Disposals, Is.EqualTo(1));
            Assert.That(coordinator.Count, Is.Zero);
        }

        [Test]
        public void TimeoutEndsWithFailureAndReleasesControl()
        {
            var action = Register("stalled", timeout: .2f);
            var handle = Submit("stalled");
            coordinator.Tick(.3f);
            Assert.That(handle.State, Is.EqualTo(ActionState.Failed));
            Assert.That(handle.StopReason, Is.EqualTo(ActionStopReason.Timeout));
            Assert.That(action.Disposals, Is.EqualTo(1));
            Assert.That(coordinator.Leases.Count, Is.Zero);
        }

        [Test]
        public void ExitDeadlinePreventsAnUnfinishedActionFromBlockingTheSessionForever()
        {
            var action = Register("stalled");
            action.ExitReady = false;
            var handle = Submit("stalled");
            coordinator.Cancel(handle);
            coordinator.Tick(3.1f);
            Assert.That(handle.State, Is.EqualTo(ActionState.Failed));
            Assert.That(coordinator.Count, Is.Zero);
        }

        [Test]
        public void CancellingQueuedActionDoesNotStartItOrReleaseTheOtherOwner()
        {
            var old = Register("walk");
            old.ExitReady = false;
            var next = Register("hand");
            Submit("walk", IntentSource.Autonomous);
            var pending = Submit("hand", IntentSource.Player);
            coordinator.Cancel(pending);
            Assert.That(pending.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(next.Starts, Is.Zero);
            Assert.That(coordinator.Leases.Count, Is.EqualTo(1));
        }

        [Test]
        public void PauseReleasesEverythingAndTheSameRuntimeCanResume()
        {
            Register("walk");
            Register("new-action", ControlChannels.Face).Complete = true;
            var handle = Submit("walk");
            coordinator.StopAll(ActionStopReason.Paused);
            Assert.That(handle.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(coordinator.Leases.Count, Is.Zero);
            var extension = Submit("new-action");
            coordinator.Tick(.1f);
            Assert.That(extension.State, Is.EqualTo(ActionState.Succeeded));
        }

        [Test]
        public void EndedSessionRejectsFurtherRequests()
        {
            Register("walk");
            coordinator.Dispose();
            Assert.That(Submit("walk").State, Is.EqualTo(ActionState.Rejected));
        }

        [Test]
        public void InvalidActionCannotPreemptExistingWork()
        {
            Register("walk");
            var active = Submit("walk", IntentSource.Autonomous);
            Assert.That(Submit("unknown", IntentSource.System).State, Is.EqualTo(ActionState.Rejected));
            Assert.That(active.State, Is.EqualTo(ActionState.Running));
        }
    }
}
