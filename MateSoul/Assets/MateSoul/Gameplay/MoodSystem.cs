using System;
using System.Collections.Generic;

namespace MateSoul.Gameplay
{
    public enum MoodLevel { None, Light, Medium, High }

    public sealed class MoodDefinition
    {
        public string Id { get; }
        public int Priority { get; }
        public bool Negative { get; }
        public string ClearsOnSelection { get; }
        public MoodDefinition(string id, int priority, bool negative, string clearsOnSelection = null)
        {
            Id = id; Priority = priority; Negative = negative; ClearsOnSelection = clearsOnSelection;
        }
    }

    [Serializable]
    public sealed class MoodMeterData
    {
        public string id;
        public float value;
    }

    [Serializable]
    public sealed class MoodSaveData
    {
        public MoodMeterData[] meters = Array.Empty<MoodMeterData>();
        public string activeId;
        public MoodLevel level;
        public float remainingSeconds;
    }

    public sealed class MoodSystem
    {
        private readonly Dictionary<string, MoodDefinition> definitions = new Dictionary<string, MoodDefinition>();
        private readonly Dictionary<string, float> meters = new Dictionary<string, float>();
        public string ActiveId { get; private set; }
        public MoodLevel Level { get; private set; }
        public float RemainingSeconds { get; private set; }
        public IEnumerable<MoodDefinition> Definitions => definitions.Values;
        public event Action Changed;

        public MoodSystem(IEnumerable<MoodDefinition> definitions = null)
        {
            foreach (var definition in definitions ?? DefaultDefinitions())
            {
                this.definitions.Add(definition.Id, definition);
                meters.Add(definition.Id, 0);
            }
        }

        public static IEnumerable<MoodDefinition> DefaultDefinitions()
        {
            yield return new MoodDefinition("sadness", 40, true, "anger");
            yield return new MoodDefinition("anger", 30, true);
            yield return new MoodDefinition("sweet", 20, false);
            yield return new MoodDefinition("happy", 10, false);
        }

        public float GetValue(string id) => meters.TryGetValue(id, out var value) ? value : 0;
        public bool Contains(string id) => id != null && meters.ContainsKey(id);

        public void Apply(string id, float delta)
        {
            if (!meters.ContainsKey(id)) throw new ArgumentException($"Unknown mood: {id}");
            if (float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            meters[id] = Math.Max(0, Math.Min(100, meters[id] + delta));
            SelectActive(id);
            Changed?.Invoke();
        }

        private void SelectActive(string changedMeter = null)
        {
            MoodDefinition winner = null;
            foreach (var definition in definitions.Values)
                if (meters[definition.Id] >= 30 &&
                    (winner == null || definition.Priority > winner.Priority))
                    winner = definition;

            var nextId = winner?.Id;
            var changedType = nextId != ActiveId;
            if (changedType)
            {
                if (ActiveId != null) meters[ActiveId] = 0;
                ActiveId = nextId;
                if (winner?.ClearsOnSelection != null && meters.ContainsKey(winner.ClearsOnSelection))
                    meters[winner.ClearsOnSelection] = 0;
            }
            if (winner == null) { Level = MoodLevel.None; RemainingSeconds = 0; return; }
            if (!changedType && changedMeter != ActiveId) return;
            var value = meters[winner.Id];
            var level = value >= 85 ? MoodLevel.High : value >= 60 ? MoodLevel.Medium : MoodLevel.Light;
            if (changedType || level != Level || RemainingSeconds == 0)
            {
                Level = level;
                RemainingSeconds = Duration(winner, level);
            }
        }

        // The session supplies elapsed active time. Offline time is never applied.
        // A due transition waits while a body action owns the character.
        public void Tick(float deltaSeconds, bool bodyOccupied)
        {
            if (ActiveId == null || RemainingSeconds < 0) return;
            RemainingSeconds = Math.Max(0, RemainingSeconds - deltaSeconds);
            if (RemainingSeconds > 0 || bodyOccupied) return;
            if (Level > MoodLevel.Light)
            {
                Level = MoodLevel.Light;
                RemainingSeconds = Duration(definitions[ActiveId], Level);
            }
            else
            {
                meters[ActiveId] = 0;
                ActiveId = null;
                Level = MoodLevel.None;
                SelectActive();
            }
            Changed?.Invoke();
        }

        private static float Duration(MoodDefinition definition, MoodLevel level)
        {
            if (level == MoodLevel.Light) return definition.Negative ? 60f : -1f;
            return definition.Negative && level == MoodLevel.High ? 40f : 15f;
        }

        public MoodSaveData Capture()
        {
            var saved = new MoodMeterData[meters.Count];
            int index = 0;
            foreach (var meter in meters)
                saved[index++] = new MoodMeterData { id = meter.Key, value = meter.Value };
            return new MoodSaveData
            {
                meters = saved, activeId = ActiveId, level = Level, remainingSeconds = RemainingSeconds
            };
        }

        public void Restore(MoodSaveData saved)
        {
            if (saved == null) return;
            if (saved.meters == null || !Enum.IsDefined(typeof(MoodLevel), saved.level) ||
                float.IsNaN(saved.remainingSeconds) || float.IsInfinity(saved.remainingSeconds) ||
                saved.remainingSeconds < -1)
                throw new ArgumentException("Invalid saved mood timer or level.");
            var restored = new Dictionary<string, float>();
            foreach (var entry in saved.meters)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id))
                    throw new ArgumentException("Invalid saved mood entry.");
                if (!definitions.ContainsKey(entry.id)) continue;
                if (float.IsNaN(entry.value) || float.IsInfinity(entry.value) || entry.value < 0 || entry.value > 100)
                    throw new ArgumentException("Invalid saved mood value.");
                restored.Add(entry.id, entry.value);
            }
            var activeId = string.IsNullOrEmpty(saved.activeId) ? null : saved.activeId;
            if (activeId != null && !definitions.ContainsKey(activeId))
                throw new ArgumentException("Saved active mood is not registered.");
            if (activeId != null && (saved.level == MoodLevel.None || !restored.ContainsKey(activeId)))
                throw new ArgumentException("Saved active mood has no meter or level.");
            foreach (var id in definitions.Keys) meters[id] = restored.TryGetValue(id, out var value) ? value : 0;
            ActiveId = activeId;
            Level = ActiveId == null ? MoodLevel.None : saved.level;
            RemainingSeconds = ActiveId == null ? 0 : saved.remainingSeconds;
            Changed?.Invoke();
        }
    }
}
