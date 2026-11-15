using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Core;
using UnityEngine;

namespace MateSoul.Runtime
{
    // AI-driven full-body gestures (dance, no-energy, yawn). They play on their
    // own override layer so they never fight the synchronized emotion
    // performances, and the dance gesture always switches the room radio on
    // before the first beat.
    [DisallowMultipleComponent]
    public sealed class GesturePerformance : MonoBehaviour
    {
        public const string LayerName = "Gesture";
        public const string DanceActionId = "dance";
        public const string NoEnergyActionId = "perform-no-energy";
        public const string YawnActionId = "perform-yawn";
        public const string DanceStateName = "Dance";
        public const string NoEnergyStateName = "NoEnergy";
        public const string YawnStateName = "Yawn";
        public const string DanceClipName = "lengyue_clip_dance01";
        public const string NoEnergyClipName = "lengyue_clip_noenergy01";
        public const string YawnClipName = "lengyue_clip_yawn01";
        private const float FadeSeconds = .12f;
        private static readonly ControlChannels Channels =
            ControlChannels.Locomotion | ControlChannels.FullBody |
            ControlChannels.Gaze | ControlChannels.Face;

        private sealed class Entry
        {
            public string ActionId;
            public AnimationClip Clip;
            public int StateHash;
        }

        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private GameSession session;
        private Animator animator;
        private int layer = -1;
        private Entry active;
        private float elapsed;
        private float weight;

        public static GesturePerformance Attach(GameObject character, GameSession session)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (session == null) throw new ArgumentNullException(nameof(session));
            var player = character.GetComponent<GesturePerformance>() ??
                         character.AddComponent<GesturePerformance>();
            player.BindRuntime(session);
            return player;
        }

        public void BindRuntime(GameSession value)
        {
            session = value;
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("MATESOUL_GESTURE_ANIMATOR_MISSING");
                return;
            }
            layer = animator.GetLayerIndex(LayerName);
            if (layer < 0)
            {
                Debug.LogWarning("MATESOUL_GESTURE_LAYER_MISSING layer=" + LayerName);
                return;
            }
            Register(NoEnergyStateName, NoEnergyClipName, NoEnergyActionId,
                "Look listless and low on energy: slump and lose momentum. Use when " +
                "the player asks you to look tired or worn out.");
            Register(YawnStateName, YawnClipName, YawnActionId,
                "Yawn sleepily: stretch and yawn. Use when the player asks you to " +
                "yawn or when you are sleepy.");
            Register(DanceStateName, DanceClipName, DanceActionId,
                "Dance to the music. Switches the room radio on first when it is " +
                "off. Use when the player asks you to dance.");
        }

        private void Register(string stateName, string clipName, string actionId,
            string description)
        {
            if (entries.ContainsKey(actionId)) return;
            var clip = animator.runtimeAnimatorController?.animationClips
                .FirstOrDefault(value => value != null && value.name == clipName);
            if (clip == null || clip.length <= 0)
            {
                Debug.LogWarning(
                    $"MATESOUL_GESTURE_CLIP_MISSING action={actionId} clip={clipName}");
                return;
            }
            var entry = new Entry
            {
                ActionId = actionId,
                Clip = clip,
                StateHash = Animator.StringToHash(LayerName + "." + stateName)
            };
            entries.Add(actionId, entry);
            Debug.Log($"MATESOUL_GESTURE_PERFORMANCE action={actionId} " +
                $"clip={clipName} seconds={clip.length:F3} state={stateName}");
            if (session.Catalog.TryGet(actionId, out _)) return;
            session.Catalog.Register(new ActionDefinition(
                actionId, description, Channels,
                intent => new GestureAction(this, entry),
                timeoutSeconds: clip.length + 3f,
                exposeToAI: true,
                exitTimeoutSeconds: .25f));
        }

        private void Begin(Entry entry)
        {
            if (entry.ActionId == DanceActionId) EnsureRadio();
            animator.SetLayerWeight(layer, 0f);
            animator.Play(entry.StateHash, layer, 0f);
            animator.Update(0);
            active = entry;
            elapsed = 0f;
            weight = 0f;
            Debug.Log($"MATESOUL_GESTURE begin action={entry.ActionId} " +
                $"clip={entry.Clip.name} seconds={entry.Clip.length:F3} layer={layer}");
        }

        private bool Tick(float deltaSeconds)
        {
            if (active == null) return false;
            elapsed += deltaSeconds;
            var normalized = Mathf.Clamp01(elapsed / active.Clip.length);
            weight = Mathf.MoveTowards(weight, 1f, deltaSeconds / FadeSeconds);
            animator.SetLayerWeight(layer, weight);
            animator.Play(active.StateHash, layer, Mathf.Min(normalized, .999f));
            return normalized < 1f;
        }

        private void End()
        {
            if (active == null) return;
            Debug.Log($"MATESOUL_GESTURE end action={active.ActionId}");
            if (animator != null && layer >= 0)
                animator.SetLayerWeight(layer, 0f);
            active = null;
            weight = 0f;
        }

        // The dance event is preceded by music: any radio already placed in the
        // room is switched on before the first beat. A missing radio is not an
        // error, the dance simply plays without a soundtrack.
        private void EnsureRadio()
        {
            var radio = UnityEngine.Object.FindObjectOfType<RadioController>();
            if (radio == null)
            {
                Debug.Log("MATESOUL_DANCE radio=none");
                return;
            }
            if (!radio.IsPoweredOn) radio.SetPower(true);
            Debug.Log($"MATESOUL_DANCE radio=on station={radio.StationIndex + 1}/" +
                $"{radio.Stations.Count} tuning={radio.Tuning:F2}");
        }

        private sealed class GestureAction : IAvatarAction, IActionRuntimeStatus
        {
            private readonly GesturePerformance owner;
            private readonly Entry entry;
            private bool cancelled;
            private bool started;

            public GestureAction(GesturePerformance owner, Entry entry)
            {
                this.owner = owner;
                this.entry = entry;
            }

            public string PhaseId => "performing";
            public string PostureId => "standing";

            public string Check()
                => owner == null || owner.animator == null || owner.layer < 0
                    ? "Gesture runtime is unavailable."
                    : entry.Clip == null
                        ? "Gesture clip is missing."
                        : null;

            public void Start()
            {
                if (owner == null)
                    throw new InvalidOperationException(
                        "Gesture runtime is unavailable.");
                owner.Begin(entry);
                started = true;
            }

            public ActionProgress Tick(float deltaSeconds)
                => cancelled || owner == null || !owner.Tick(deltaSeconds)
                    ? ActionProgress.Succeeded
                    : ActionProgress.Running;

            public void Cancel(ActionStopReason reason)
            {
                cancelled = true;
                if (started && owner != null) owner.End();
                started = false;
            }

            public void Dispose()
            {
                if (started && owner != null) owner.End();
                started = false;
            }
        }
    }
}
