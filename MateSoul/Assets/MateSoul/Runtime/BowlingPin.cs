using UnityEngine;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(PhysicsBody))]
    public sealed class BowlingPin : MonoBehaviour
    {
        private const float PlacementLockSeconds = 1f;
        // Enough samples to see whether a racked pin keeps its footing or starts to tip.
        private const int StabilitySamples = 6;
        private const float StabilityInterval = .5f;
        private float unlockAt = -1, rackedAt, nextStabilitySample;
        private int stabilitySamples;
        public PhysicsBody Body { get; private set; }
        public Pose StandingPose { get; private set; }
        public bool IsPlacementLocked => unlockAt >= 0;
        public bool IsDown => Vector3.Dot(Body.Pose.rotation * Vector3.up, Vector3.up) <= .707107f ||
                              Body.Pose.position.y < StandingPose.position.y - .19f;

        public void Rack(Pose pose)
        {
            if (Body == null) { Body = GetComponent<PhysicsBody>(); Body.Initialize(); }
            StandingPose = pose;
            if (Body.IsContact) Body.SetContact(false);
            Body.Place(pose);
            Body.SetContact(true);
            unlockAt = Time.time + PlacementLockSeconds;
            rackedAt = Time.unscaledTime;
            nextStabilitySample = rackedAt + StabilityInterval;
            stabilitySamples = 0;
            Debug.Log($"MATESOUL_BOWLING pin rack id={GetInstanceID()} y={pose.position.y:F3} " +
                      $"support={Support()}");
        }

        private void FixedUpdate()
        {
            if (Body == null || !IsPlacementLocked || Time.time < unlockAt || Body.IsPaused) return;
            unlockAt = -1;
            // Begin the simulation from the exact standing pose with no inherited motion so a
            // settled rack stays upright until the ball actually reaches it.
            Body.Place(StandingPose);
            Body.SetContact(false);
            Body.Rigidbody.velocity = Vector3.zero;
            Body.Rigidbody.angularVelocity = Vector3.zero;
            Debug.Log($"MATESOUL_BOWLING pin unlock id={GetInstanceID()} y={StandingPose.position.y:F3} " +
                      $"support={Support()}");
        }

        private void Update()
        {
            if (Body == null || IsPlacementLocked || stabilitySamples >= StabilitySamples) return;
            if (Time.unscaledTime < nextStabilitySample) return;
            nextStabilitySample = Time.unscaledTime + StabilityInterval;
            stabilitySamples++;
            var rigidbody = Body.Rigidbody;
            var tilt = Vector3.Angle(Body.Pose.rotation * Vector3.up, Vector3.up);
            Debug.Log($"MATESOUL_BOWLING pin sample id={GetInstanceID()} n={stabilitySamples} " +
                      $"t={Time.unscaledTime - rackedAt:F2} y={Body.Pose.position.y:F3} " +
                      $"drop={Body.Pose.position.y - StandingPose.position.y:F3} tilt={tilt:F1} " +
                      $"speed={rigidbody.velocity.magnitude:F2} " +
                      $"spin={rigidbody.angularVelocity.magnitude:F2} " +
                      $"kinematic={rigidbody.isKinematic} contact={Body.IsContact} support={Support()}");
        }

        // The contact the pin actually rests on. Pins ignore every non-support collider for
        // collision, so a scan surface showing up here while the pin falls is direct evidence
        // that the spawn height came from the wrong geometry.
        private string Support()
        {
            var origin = Body != null ? Body.Pose.position + Vector3.up * .05f : transform.position;
            return Physics.Raycast(origin, Vector3.down, out var hit, .4f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore)
                ? $"{hit.collider.name}@{hit.point.y:F3}" : "none";
        }
    }
}
