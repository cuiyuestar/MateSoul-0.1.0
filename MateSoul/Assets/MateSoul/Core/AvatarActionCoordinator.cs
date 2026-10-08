using System;
using System.Collections.Generic;

namespace MateSoul.Core
{
    // Driven on the session thread. Factories and actions never receive this
    // coordinator, so an executor cannot silently start a parallel control path.
    public sealed class AvatarActionCoordinator : IDisposable
    {
        private sealed class Execution
        {
            public ActionHandle Handle;
            public ActionDefinition Definition;
            public IAvatarAction Action;
            public int Priority;
            public float ExitElapsed;
        }

        private readonly ActionCatalog catalog;
        private readonly List<Execution> executions = new List<Execution>();
        private bool disposed;
        public ControlLeaseManager Leases { get; } = new ControlLeaseManager();
        // Session lifecycle admission applies equally to UI, AI and autonomy.
        public bool Suspended { get; set; }
        public int Count => executions.Count;
        public bool IsIdle => executions.Count == 0;
        public event Action<AvatarActionSnapshot> Changed;

        public bool HasClaim(ControlChannels channels)
        {
            foreach (var execution in executions)
                if ((execution.Definition.Channels & channels) != 0) return true;
            return false;
        }

        public AvatarActionCoordinator(ActionCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ActionHandle Submit(AvatarIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var handle = new ActionHandle(intent);
            if (disposed) return Reject(handle, "The avatar session has ended.");
            if (Suspended) return Reject(handle, "角色暂不可用，房间正在恢复或会话已暂停。");
            if (!catalog.TryGet(intent.ActionId, out var definition))
                return Reject(handle, $"Unknown action: {intent.ActionId}");
            if (definition.RequiresTarget && string.IsNullOrWhiteSpace(intent.TargetId))
                return Reject(handle, "This action requires a target.");

            IAvatarAction action = null;
            string admissionError = null;
            try
            {
                action = definition.Factory(intent);
                if (action == null) throw new InvalidOperationException("Action factory returned null.");
                admissionError = action.Check();
            }
            catch (Exception exception) { admissionError = exception.Message; }
            if (admissionError != null)
            {
                try { action?.Dispose(); }
                catch (Exception cleanup) { admissionError += "; " + cleanup.Message; }
                return Reject(handle, admissionError);
            }

            int priority = PriorityOf(intent.Source);
            foreach (var existing in executions)
            {
                if ((existing.Definition.Channels & definition.Channels) == 0) continue;
                if (existing.Priority >= priority)
                {
                    try { action.Dispose(); }
                    catch (Exception exception) { return Reject(handle, exception.Message); }
                    return Reject(handle, "A conflicting action already owns or awaits control.");
                }
            }

            // Admission is complete before any existing work is interrupted.
            for (int i = executions.Count - 1; i >= 0; i--)
            {
                var existing = executions[i];
                if ((existing.Definition.Channels & definition.Channels) != 0)
                    RequestCancel(existing, ActionStopReason.Preempted);
            }

            var execution = new Execution
            {
                Handle = handle, Definition = definition, Action = action, Priority = priority
            };
            executions.Add(execution);
            Notify(execution);
            TryStart(execution);
            return handle;
        }

        public IReadOnlyList<AvatarActionSnapshot> Capture()
        {
            var snapshot = new AvatarActionSnapshot[executions.Count];
            for (var index = 0; index < executions.Count; index++)
                snapshot[index] = CreateSnapshot(executions[index]);
            return snapshot;
        }

        public void Tick(float deltaSeconds)
        {
            if (disposed) return;
            if (deltaSeconds < 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

            for (int i = executions.Count - 1; i >= 0; i--)
            {
                var execution = executions[i];
                var handle = execution.Handle;
                handle.ElapsedSeconds += deltaSeconds;
                if (handle.State != ActionState.Cancelling &&
                    handle.ElapsedSeconds >= execution.Definition.TimeoutSeconds)
                    RequestCancel(execution, ActionStopReason.Timeout);
                if (handle.IsFinished) continue;
                if (handle.State == ActionState.Queued) continue;

                if (handle.State == ActionState.Cancelling)
                {
                    execution.ExitElapsed += deltaSeconds;
                    if (execution.ExitElapsed >= execution.Definition.ExitTimeoutSeconds)
                    {
                        Finish(execution, ActionState.Failed, "Action exit exceeded its deadline.");
                        continue;
                    }
                }
                try
                {
                    if (execution.Action.Tick(deltaSeconds) == ActionProgress.Succeeded)
                        Finish(execution, handle.State == ActionState.Cancelling
                            ? (handle.StopReason == ActionStopReason.Timeout ? ActionState.Failed : ActionState.Cancelled)
                            : ActionState.Succeeded,
                            handle.StopReason == ActionStopReason.Timeout ? "Action timed out." : null);
                }
                catch (Exception exception)
                {
                    Finish(execution, ActionState.Failed, exception.Message);
                }
            }
            // Exit phases have completed. Only now can waiting actions own resources.
            for (int i = executions.Count - 1; i >= 0; i--)
                if (executions[i].Handle.State == ActionState.Queued) TryStart(executions[i]);
        }

        public void Cancel(ActionHandle handle, ActionStopReason reason = ActionStopReason.Requested)
        {
            if (handle == null) return;
            for (int i = executions.Count - 1; i >= 0; i--)
                if (executions[i].Handle == handle) { RequestCancel(executions[i], reason); return; }
        }

        // Lifecycle shutdown cannot await an animation exit. Dispose must stop
        // owned motors/IK and unsubscribe from target events immediately.
        public void StopAll(ActionStopReason reason)
        {
            for (int i = executions.Count - 1; i >= 0; i--)
            {
                var execution = executions[i];
                execution.Handle.StopReason = reason;
                Finish(execution, ActionState.Cancelled, null);
            }
        }

        private void RequestCancel(Execution execution, ActionStopReason reason)
        {
            if (execution.Handle.IsFinished || execution.Handle.State == ActionState.Cancelling) return;
            execution.Handle.StopReason = reason;
            if (execution.Handle.State == ActionState.Queued)
            {
                Finish(execution, reason == ActionStopReason.Timeout ? ActionState.Failed : ActionState.Cancelled,
                    reason == ActionStopReason.Timeout ? "Action timed out while waiting." : null);
                return;
            }
            execution.Handle.State = ActionState.Cancelling;
            Notify(execution);
            try { execution.Action.Cancel(reason); }
            catch (Exception exception) { Finish(execution, ActionState.Failed, exception.Message); }
        }

        private void TryStart(Execution execution)
        {
            if (!Leases.TryAcquire(execution.Handle.Id, execution.Definition.Channels)) return;
            try
            {
                // A target may disappear while an earlier action is exiting.
                var error = execution.Action.Check();
                if (error != null) { Finish(execution, ActionState.Rejected, error); return; }
                execution.Handle.State = ActionState.Running;
                execution.Action.Start();
                Notify(execution);
            }
            catch (Exception exception) { Finish(execution, ActionState.Failed, exception.Message); }
        }

        private void Finish(Execution execution, ActionState state, string error)
        {
            if (execution.Handle.IsFinished) return;
            try { execution.Action.Dispose(); }
            catch (Exception exception) { state = ActionState.Failed; error = exception.Message; }
            finally
            {
                Leases.Release(execution.Handle.Id);
                executions.Remove(execution);
                execution.Handle.Error = error;
                execution.Handle.State = state;
            }
            Notify(execution);
        }

        private void Notify(Execution execution)
            => Changed?.Invoke(CreateSnapshot(execution));

        private static AvatarActionSnapshot CreateSnapshot(Execution execution)
        {
            var runtime = execution.Action as IActionRuntimeStatus;
            return new AvatarActionSnapshot
            {
                action_id = execution.Handle.Intent.ActionId,
                target_id = execution.Handle.Intent.TargetId,
                source = execution.Handle.Intent.Source.ToString().ToLowerInvariant(),
                state = execution.Handle.State.ToString().ToLowerInvariant(),
                phase = runtime?.PhaseId ?? execution.Handle.State.ToString().ToLowerInvariant(),
                posture = runtime?.PostureId,
                elapsed_seconds = execution.Handle.ElapsedSeconds
            };
        }

        private static ActionHandle Reject(ActionHandle handle, string error)
        {
            handle.State = ActionState.Rejected;
            handle.Error = error;
            return handle;
        }

        private static int PriorityOf(IntentSource source)
        {
            switch (source)
            {
                case IntentSource.System: return 100;
                case IntentSource.Player: return 90;
                case IntentSource.UI:
                case IntentSource.AI: return 60;
                default: return 10;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            StopAll(ActionStopReason.SessionEnded);
            disposed = true;
        }
    }
}
