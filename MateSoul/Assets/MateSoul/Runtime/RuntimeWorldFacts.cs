using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public interface IRuntimeWorldFactSource
    {
        bool TryCaptureRuntimeWorldFact(out RuntimeWorldFactSnapshot fact);
    }

    public static class RuntimeWorldFact
    {
        public static RuntimeWorldFactSnapshot Create(
            Component source, string category, string state, string value)
        {
            var item = source == null ? null : source.GetComponentInParent<GameItem>();
            return new RuntimeWorldFactSnapshot
            {
                subject_id = item?.Instance?.Id ?? source?.gameObject.name ?? "unknown",
                subject_name = item?.Definition?.DisplayName ?? source?.gameObject.name ?? "unknown",
                category = category,
                state = state,
                value = value
            };
        }
    }
}
