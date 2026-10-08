using UnityEngine;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(PhysicsBody))]
    public sealed class BowlingPin : MonoBehaviour
    {
        private const float PlacementLockSeconds = 1f;
        private float unlockAt = -1;
        public PhysicsBody Body { get; private set; }
        public Pose StandingPose { get; private set; }
        public bool IsPlacementLocked => unlockAt >= 0;
        public bool IsDown => Vector3.Dot(Body.Pose.rotation * Vector3.up, Vector3.up) <= .707107f ||
                              Body.Pose.position.y < StandingPose.position.y - .19f;
#region debug-point C:pin-rack-stability
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private float debugRackStarted = -1;
        private int debugRackSample;
#endif
#endregion

        public void Rack(Pose pose)
        {
            if (Body == null) { Body = GetComponent<PhysicsBody>(); Body.Initialize(); }
            StandingPose = pose;
            if (Body.IsContact) Body.SetContact(false);
            Body.Place(pose);
            Body.SetContact(true);
            unlockAt = Time.time + PlacementLockSeconds;
#region debug-point C:pin-rack-stability
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            debugRackStarted = Time.unscaledTime;
            debugRackSample = 0;
#endif
#endregion
        }

        private void FixedUpdate()
        {
            if (!IsPlacementLocked || Time.time < unlockAt || Body.IsPaused) return;
            unlockAt = -1;
            Body.SetContact(false);
        }

#region debug-point C:pin-rack-stability
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private void Update()
        {
            if (debugRackStarted < 0) return;
            var age = Time.unscaledTime - debugRackStarted;
            if (age + .001f < debugRackSample * .25f) return;
            var rigidbody = Body.Rigidbody;
            var payload = $"{{\"sessionId\":\"bowling-pin-reset-blocked\",\"runId\":\"post-fix\"," +
                          $"\"hypothesisId\":\"C\",\"ts\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                          $"\"location\":\"BowlingPin.Update\",\"msg\":\"[DEBUG] pin rack stability sample\",\"data\":{{" +
                          $"\"pinId\":{GetInstanceID()}," +
                          $"\"sample\":{debugRackSample}," +
                          $"\"age\":{age.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"angle\":{Vector3.Angle(Body.Pose.rotation * Vector3.up, Vector3.up).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"heightDelta\":{(Body.Pose.position.y - StandingPose.position.y).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"speed\":{rigidbody.velocity.magnitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"angularSpeed\":{rigidbody.angularVelocity.magnitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"isKinematic\":{rigidbody.isKinematic.ToString().ToLowerInvariant()}," +
                          $"\"contact\":{Body.IsContact.ToString().ToLowerInvariant()}" + "}}";
            UnityEngine.Networking.UnityWebRequest.Post(
                    "http://10.0.2.2:7782/event", payload, "application/json")
                .SendWebRequest();
            debugRackSample++;
            if (debugRackSample > 4) debugRackStarted = -1;
        }
#endif
#endregion
    }
}
