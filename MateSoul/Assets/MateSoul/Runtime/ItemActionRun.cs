using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public readonly struct ItemActionRequirement
    {
        public readonly string Role, Capability, DisplayName, InstanceId;
        public ItemActionRequirement(string role, string capability, string name, string instanceId = null)
        { Role = role; Capability = capability; DisplayName = name; InstanceId = instanceId; }
    }

    public sealed class ItemActionBinding
    {
        public GameItem View { get; internal set; }
        public ItemReservation Lease { get; internal set; }
        public ItemInteractionProfile Profile => View.Definition.Interaction;
        public bool Consumed { get; internal set; }
    }

    public interface IItemActionStep : IDisposable
    {
        string Label { get; }
        void Start();
        bool Tick(float dt);
    }

    // A single action owns the sequence. Substeps never submit competing avatar actions.
    public abstract class ItemActionRun : IAvatarAction, IActionRuntimeStatus
    {
        protected readonly GameSession Session;
        protected readonly AvatarMotor Motor;
        protected readonly AvatarItemRig Rig;
        protected readonly Guid RunId = Guid.NewGuid();
        protected readonly Dictionary<string, ItemActionBinding> Bindings =
            new Dictionary<string, ItemActionBinding>(StringComparer.Ordinal);
        private readonly ItemActionRequirement[] requirements;
        private IEnumerator<IItemActionStep> sequence;
        private IItemActionStep step;
        private bool started, cancelled, finished;
#region debug-point C-D-E:action-physics
#if UNITY_EDITOR
        private float debugNextPhysicsReport;
#endif
#endregion
        public string PhaseId => step?.Label ?? "准备";
        public string PostureId => "standing";
        protected virtual string ActionName => "切水果";
        protected virtual string StartNotice => "冷月正在切水果。";

        protected ItemActionRun(GameSession session, params ItemActionRequirement[] requirements)
        {
            Session = session;
            Motor = session.Motor as AvatarMotor;
            Rig = Motor != null ? Motor.GetComponent<AvatarItemRig>() : null;
            this.requirements = requirements;
        }

        public string Check()
        {
            if (!Session.ItemsReady) return "房间物品仍在恢复，请稍候。";
            Bindings.Clear();
            var missing = new List<string>();
            var busy = new List<string>();
            foreach (var requirement in requirements)
            {
                var candidates = Session.ItemViews.Where(view => view != null &&
                    Session.Items.IsCurrent(view.Instance) &&
                    view.Definition.Interaction != null &&
                    (requirement.InstanceId == null || view.Instance.Id == requirement.InstanceId) &&
                    view.Instance.Supports(requirement.Capability)).ToArray();
                var chosen = candidates.Where(view => !view.Instance.IsOccupied)
                    .OrderBy(view => (view.Body.Pose.position - Session.Motor.Position).sqrMagnitude)
                    .ThenBy(view => view.Instance.Id, StringComparer.Ordinal).FirstOrDefault();
                if (chosen == null)
                {
                    (candidates.Length == 0 ? missing : busy).Add(requirement.DisplayName);
                    continue;
                }
                Bindings.Add(requirement.Role, new ItemActionBinding { View = chosen });
            }
            if (missing.Count > 0) return ActionName + "需要先放置：" + string.Join("、", missing) + "。";
            if (busy.Count > 0) return string.Join("、", busy) + "正在被使用，请放下后重试。";
            if (Rig == null || !Rig.IsConfigured) return "角色缺少物品交互动作，请重建游戏内容。";
            return CheckBindings();
        }

        protected abstract string CheckBindings();
        protected abstract IEnumerable<IItemActionStep> BuildSteps();

        public void Start()
        {
#region debug-point C-D-E:before-reservation
#if UNITY_EDITOR
            var debugBeforeItems = string.Join(",", Bindings.Select(pair =>
            {
                var body = pair.Value.View.Body;
                return $"{{\"role\":\"{pair.Key}\",\"item\":\"{pair.Value.View.Definition.Id}\"," +
                       $"\"position\":{JsonUtility.ToJson(body.Pose.position)}," +
                       $"\"control\":\"{pair.Value.View.Instance.ControlState}\"," +
                       $"\"kinematic\":{body.Rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                       $"\"gravity\":{body.Rigidbody.useGravity.ToString().ToLowerInvariant()}," +
                       $"\"detectCollisions\":{body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                       $"\"support\":{body.HasSupport().ToString().ToLowerInvariant()}}}";
            }));
            var debugBeforePayload =
                $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"C-D-E\",\"location\":\"ItemActionRun.Start.BeforeReservation\"," +
                $"\"msg\":\"[DEBUG] item action before reservation\",\"data\":{{\"items\":[{debugBeforeItems}]}}}}";
            var debugBeforeRequest = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7777/event", debugBeforePayload, "application/json");
            debugBeforeRequest.SendWebRequest().completed += _ => debugBeforeRequest.Dispose();
#endif
#endregion
            var requests = requirements.Select(requirement => new ItemReservationRequest(
                Bindings[requirement.Role].View.Instance, requirement.Capability)).ToArray();
            if (!Session.Items.TryReserveBundle(requests, this, RunId, out var leases))
                throw new InvalidOperationException("物品状态已变化，请重新选择" + ActionName + "。");
            for (var i = 0; i < leases.Length; i++) Bindings[requirements[i].Role].Lease = leases[i];
            started = true;
            Motor.EndBodyContact();
            Motor.Stop();
            Rig.Begin(RunId);
            sequence = BuildSteps().GetEnumerator();
            Session.Notifications.ShowHint(StartNotice);
            Advance();
#region debug-point C-D-E:after-first-step
#if UNITY_EDITOR
            var debugAfterItems = string.Join(",", Bindings.Select(pair =>
            {
                var body = pair.Value.View.Body;
                return $"{{\"role\":\"{pair.Key}\",\"item\":\"{pair.Value.View.Definition.Id}\"," +
                       $"\"position\":{JsonUtility.ToJson(body.Pose.position)}," +
                       $"\"control\":\"{pair.Value.View.Instance.ControlState}\"," +
                       $"\"kinematic\":{body.Rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                       $"\"gravity\":{body.Rigidbody.useGravity.ToString().ToLowerInvariant()}," +
                       $"\"detectCollisions\":{body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                       $"\"support\":{body.HasSupport().ToString().ToLowerInvariant()}," +
                       $"\"controlled\":{body.IsInteractionControlled.ToString().ToLowerInvariant()}}}";
            }));
            var debugAfterPayload =
                $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"C-D-E\",\"location\":\"ItemActionRun.Start.AfterFirstStep\"," +
                $"\"msg\":\"[DEBUG] item action after first step start\",\"data\":{{\"phase\":\"{PhaseId}\",\"items\":[{debugAfterItems}]}}}}";
            var debugAfterRequest = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7777/event", debugAfterPayload, "application/json");
            debugAfterRequest.SendWebRequest().completed += _ => debugAfterRequest.Dispose();
#endif
#endregion
        }

        private void Advance()
        {
            step?.Dispose();
            step = null;
            if (!sequence.MoveNext()) { finished = true; return; }
            step = sequence.Current;
            step.Start();
        }

        public ActionProgress Tick(float dt)
        {
            if (cancelled || finished) return ActionProgress.Succeeded;
#region debug-point C-D-E:reserved-physics-sample
#if UNITY_EDITOR
            if (Time.unscaledTime >= debugNextPhysicsReport)
            {
                debugNextPhysicsReport = Time.unscaledTime + .25f;
                var debugItems = string.Join(",", Bindings.Where(pair =>
                    !pair.Value.Consumed && pair.Value.View != null).Select(pair =>
                {
                    var body = pair.Value.View.Body;
                    return $"{{\"role\":\"{pair.Key}\",\"item\":\"{pair.Value.View.Definition.Id}\"," +
                           $"\"y\":{body.Pose.position.y},\"control\":\"{pair.Value.View.Instance.ControlState}\"," +
                           $"\"kinematic\":{body.Rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                           $"\"gravity\":{body.Rigidbody.useGravity.ToString().ToLowerInvariant()}," +
                           $"\"detectCollisions\":{body.Rigidbody.detectCollisions.ToString().ToLowerInvariant()}," +
                           $"\"support\":{body.HasSupport().ToString().ToLowerInvariant()}," +
                           $"\"controlled\":{body.IsInteractionControlled.ToString().ToLowerInvariant()}}}";
                }));
                var debugPayload =
                    $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"C-D-E\",\"location\":\"ItemActionRun.Tick\"," +
                    $"\"msg\":\"[DEBUG] reserved item physics sample\",\"data\":{{\"phase\":\"{PhaseId}\",\"items\":[{debugItems}]}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7777/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
            foreach (var binding in Bindings.Values)
            {
                if (binding.Consumed) continue;
                if (binding.View == null || !binding.Lease.IsValid)
                    throw new InvalidOperationException(ActionName + "所需物品已失效。");
                binding.Lease.Heartbeat(Time.unscaledTimeAsDouble);
            }
            if (step.Tick(dt)) Advance();
            return finished ? ActionProgress.Succeeded : ActionProgress.Running;
        }

        public void Cancel(ActionStopReason reason) { cancelled = true; }

        public void Dispose()
        {
            // Admission failure has no authority and must not affect another action.
            if (!started) return;
            try { step?.Dispose(); sequence?.Dispose(); }
            finally
            {
                Session.SetItemActionAttention(null, false);
                Motor.Stop();
                Rig.End(RunId);
                foreach (var binding in Bindings.Values) binding.Lease?.Dispose();
                Session.RequestPhysicsSave();
                started = false;
            }
        }
    }
}
