using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Wardrobe;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public static class CharacterPersonaLoader
    {
        public static CharacterPersonaDefinition Load(TextAsset asset)
        {
            if (asset == null) throw new InvalidOperationException("Character persona asset is missing.");
            var definition = JsonUtility.FromJson<CharacterPersonaDefinition>(asset.text);
            return definition ??
                throw new InvalidOperationException("Character persona asset contains no data.");
        }
    }

    public sealed class CharacterRuntimeContextProvider : ICharacterContextProvider, IDisposable
    {
        private const int RecentEventLimit = 8;
        private readonly CharacterPersonaState persona;
        private readonly LengyueWardrobeController wardrobe;
        private readonly MoodSystem mood;
        private readonly AvatarActionCoordinator avatar;
        private readonly ActionCatalog catalog;
        private readonly ItemRegistry items;
        private readonly AvatarMotor motor;
        private readonly Queue<RuntimeEventSnapshot> recentEvents =
            new Queue<RuntimeEventSnapshot>();
        private string lastMoodId;
        private MoodLevel lastMoodLevel;
        private long revision = 1;
        private bool disposed;

        public CharacterRuntimeContextProvider(CharacterPersonaState persona,
            LengyueWardrobeController wardrobe, MoodSystem mood,
            AvatarActionCoordinator avatar, ActionCatalog catalog, ItemRegistry items, AvatarMotor motor)
        {
            this.persona = persona ?? throw new ArgumentNullException(nameof(persona));
            this.wardrobe = wardrobe ?? throw new ArgumentNullException(nameof(wardrobe));
            this.mood = mood ?? throw new ArgumentNullException(nameof(mood));
            this.avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.items = items ?? throw new ArgumentNullException(nameof(items));
            this.motor = motor;
            lastMoodId = mood.ActiveId;
            lastMoodLevel = mood.Level;
            persona.Changed += OnPersonaChanged;
            wardrobe.OutfitChanged += OnOutfitChanged;
            mood.Changed += OnMoodChanged;
            avatar.Changed += OnActionChanged;
        }

        public CharacterContextSnapshot Capture()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CharacterRuntimeContextProvider));
            var active = avatar.Capture();
            var actions = active.Select(ToRuntimeAction).ToArray();
            var posture = motor?.ContactPosture ?? active.Select(action => action.posture)
                .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? "standing";
            var capturedOutfitId = wardrobe.CurrentOutfitId;
            var capturedDisplayName = wardrobe.CurrentDisplayName;
#region debug-point B-D-E:context-capture
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugPayload = $"{{\"sessionId\":\"outfit-context-stale\",\"runId\":\"pre-fix\",\"hypothesisId\":\"B,D,E\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"CharacterRuntimeContextProvider.Capture\",\"msg\":\"[DEBUG] runtime context captured\",\"data\":{{\"wardrobeInstanceId\":{wardrobe.GetInstanceID()},\"revision\":{revision},\"outfitId\":\"{capturedOutfitId}\",\"displayName\":\"{capturedDisplayName}\",\"recentEvents\":{recentEvents.Count}}}}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", debugPayload, "application/json").SendWebRequest();
#endif
#endregion
            return new CharacterContextSnapshot
            {
                persona = persona.Capture(),
                runtime = new CharacterRuntimeSnapshot
                {
                    revision = revision,
                    appearance = new RuntimeAppearanceSnapshot
                    {
                        outfit_id = capturedOutfitId,
                        display_name = capturedDisplayName
                    },
                    mood = new RuntimeMoodSnapshot
                    {
                        id = mood.ActiveId ?? "neutral",
                        level = mood.ActiveId == null
                            ? "none" : mood.Level.ToString().ToLowerInvariant()
                    },
                    posture = posture,
                    actions = actions,
                    recent_events = recentEvents.ToArray()
                }
            };
        }

        private RuntimeActionSnapshot ToRuntimeAction(AvatarActionSnapshot action)
        {
            catalog.TryGet(action.action_id, out var definition);
            RuntimeActionTargetSnapshot target = null;
            if (items.TryGet(action.target_id, out var item))
                target = new RuntimeActionTargetSnapshot
                {
                    id = item.Id,
                    name = item.DisplayName,
                    state = item.StateId
                };
            return new RuntimeActionSnapshot
            {
                id = action.action_id,
                description = definition?.Description ?? action.action_id,
                state = action.state,
                phase = action.phase,
                initiator = action.source,
                elapsed_seconds = action.elapsed_seconds,
                target = target
            };
        }

        private void OnPersonaChanged()
        {
            revision++;
            AddEvent(new RuntimeEventSnapshot { type = "personality_changed" });
        }

        private void OnOutfitChanged(string outfitId)
        {
            revision++;
#region debug-point B-E:outfit-event
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugPayload = $"{{\"sessionId\":\"outfit-context-stale\",\"runId\":\"pre-fix\",\"hypothesisId\":\"B,E\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"CharacterRuntimeContextProvider.OnOutfitChanged\",\"msg\":\"[DEBUG] outfit event received\",\"data\":{{\"wardrobeInstanceId\":{wardrobe.GetInstanceID()},\"eventOutfitId\":\"{outfitId}\",\"currentOutfitId\":\"{wardrobe.CurrentOutfitId}\",\"revision\":{revision}}}}}";
            UnityWebRequest.Post("http://127.0.0.1:7782/event", debugPayload, "application/json").SendWebRequest();
#endif
#endregion
            AddEvent(new RuntimeEventSnapshot
            {
                type = "outfit_changed",
                value = outfitId
            });
        }

        private void OnMoodChanged()
        {
            if (lastMoodId == mood.ActiveId && lastMoodLevel == mood.Level) return;
            lastMoodId = mood.ActiveId;
            lastMoodLevel = mood.Level;
            revision++;
            AddEvent(new RuntimeEventSnapshot
            {
                type = "mood_changed",
                value = mood.ActiveId ?? "neutral"
            });
        }

        private void OnActionChanged(AvatarActionSnapshot action)
        {
            revision++;
            if (action.state != "succeeded" && action.state != "cancelled" &&
                action.state != "failed" && action.state != "rejected")
                return;
            items.TryGet(action.target_id, out var target);
            AddEvent(new RuntimeEventSnapshot
            {
                type = "action_finished",
                action_id = action.action_id,
                state = action.state,
                target_id = action.target_id,
                target_name = target?.DisplayName
            });
        }

        private void AddEvent(RuntimeEventSnapshot value)
        {
            recentEvents.Enqueue(value);
            while (recentEvents.Count > RecentEventLimit) recentEvents.Dequeue();
        }

        public void Dispose()
        {
            if (disposed) return;
            persona.Changed -= OnPersonaChanged;
            wardrobe.OutfitChanged -= OnOutfitChanged;
            mood.Changed -= OnMoodChanged;
            avatar.Changed -= OnActionChanged;
            disposed = true;
        }
    }
}
