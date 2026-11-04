using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealTimeClock : MonoBehaviour
    {
        [SerializeField] private Transform hourHandPivot;
        [SerializeField] private Transform minuteHandPivot;
        [SerializeField] private float authoredHourAngle = -52f;
        [SerializeField] private float authoredMinuteAngle = 56f;
        private Quaternion authoredHourRotation;
        private Quaternion authoredMinuteRotation;
        private float nextUpdate;
        private bool initialized;

        public void Configure(Transform hourPivot, Transform minutePivot,
            float hourAngle = -52f, float minuteAngle = 56f)
        {
            if (hourPivot == null || minutePivot == null || hourPivot == minutePivot)
                throw new ArgumentException("Clock hands require two independent pivots.");
            hourHandPivot = hourPivot;
            minuteHandPivot = minutePivot;
            authoredHourAngle = hourAngle;
            authoredMinuteAngle = minuteAngle;
        }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            Initialize();
            if (initialized) Synchronize();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && initialized && isActiveAndEnabled) Synchronize();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused && initialized && isActiveAndEnabled) Synchronize();
        }

        private void Initialize()
        {
            if (initialized || hourHandPivot == null || minuteHandPivot == null) return;
            authoredHourRotation = hourHandPivot.localRotation;
            authoredMinuteRotation = minuteHandPivot.localRotation;
            initialized = true;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            Synchronize();
        }

        private void Synchronize()
        {
            nextUpdate = Time.unscaledTime + .25f;
            ApplyTime(DateTime.Now);
        }

        public void ApplyTime(DateTime localTime)
        {
            Initialize();
            if (!initialized)
                throw new InvalidOperationException("Clock hand pivots are not configured.");
            var minuteAngle = localTime.Minute * 6f;
            var hourAngle = (localTime.Hour % 12) * 30f +
                            Mathf.Floor(localTime.Minute / 5f) * 2.5f;
            // FBX import reverses Blender's authored Y angles. Add the source
            // angle to cancel the imported pose before applying dial time.
            hourHandPivot.localRotation = authoredHourRotation *
                Quaternion.AngleAxis(hourAngle + authoredHourAngle, Vector3.up);
            minuteHandPivot.localRotation = authoredMinuteRotation *
                Quaternion.AngleAxis(minuteAngle + authoredMinuteAngle, Vector3.up);
        }
    }
}
