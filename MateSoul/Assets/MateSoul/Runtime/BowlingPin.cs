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

        public void Rack(Pose pose)
        {
            if (Body == null) { Body = GetComponent<PhysicsBody>(); Body.Initialize(); }
            StandingPose = pose;
            if (Body.IsContact) Body.SetContact(false);
            Body.Place(pose);
            Body.SetContact(true);
            unlockAt = Time.time + PlacementLockSeconds;
        }

        private void FixedUpdate()
        {
            if (!IsPlacementLocked || Time.time < unlockAt || Body.IsPaused) return;
            unlockAt = -1;
            Body.SetContact(false);
        }

    }
}
