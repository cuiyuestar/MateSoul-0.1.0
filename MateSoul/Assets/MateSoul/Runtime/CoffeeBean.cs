using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class CoffeeBean : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float maximumDynamicTime = 1.1f;
        [SerializeField, Min(0)] private float disappearanceDelay = 1.5f;
        [SerializeField, Min(.1f)] private float disappearanceDuration = 6f;

        private Rigidbody body;
        private Collider shape;
        private Vector3 initialScale;
        private float age;
        private float quietTime;
        private float landedAt = -1;
        private bool captured;
        private CoffeeBeanJarController poolOwner;

        public bool IsSettled { get; private set; }
        public bool IsCaptured => captured;
        public bool IsDisappearing => landedAt >= 0 && !captured;
        public float Age => age;

        public void SetPoolOwner(CoffeeBeanJarController owner)
        {
            poolOwner = owner;
        }

        public void Configure(float dynamicSeconds = 1.1f, float delaySeconds = 1.5f,
            float fadeSeconds = 6f)
        {
            if (!PhysicsBody.IsFinite(dynamicSeconds) || dynamicSeconds <= 0 ||
                !PhysicsBody.IsFinite(delaySeconds) || delaySeconds < 0 ||
                !PhysicsBody.IsFinite(fadeSeconds) || fadeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(dynamicSeconds));
            maximumDynamicTime = dynamicSeconds;
            disappearanceDelay = delaySeconds;
            disappearanceDuration = fadeSeconds;
            PrepareBody();
        }

        private void Awake() => PrepareBody();

        private void PrepareBody()
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<Collider>();
            initialScale = transform.localScale;
            body.mass = .00016f;
            body.drag = .08f;
            body.angularDrag = .08f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.maxAngularVelocity = 24f;
        }

        public void Launch(Pose pose, Vector3 velocity, Vector3 angularVelocity)
        {
            if (!PhysicsBody.IsFinite(pose.position) ||
                !PhysicsBody.IsFinite(velocity) ||
                !PhysicsBody.IsFinite(angularVelocity))
                throw new ArgumentException("Coffee bean launch state must be finite.");
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            transform.localScale = initialScale;
            age = 0;
            quietTime = 0;
            landedAt = -1;
            captured = false;
            IsSettled = false;
            shape.enabled = true;
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.velocity = velocity;
            body.angularVelocity = angularVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (captured || collision == null || collision.contactCount == 0) return;
            for (var index = 0; index < collision.contactCount; index++)
            {
                if (Vector3.Dot(collision.GetContact(index).normal, Vector3.up) < .3f)
                    continue;
                MarkLanded();
                return;
            }
        }

        public void MarkLanded()
        {
            if (captured || landedAt >= 0) return;
            landedAt = age;
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float seconds)
        {
            if (!PhysicsBody.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            Advance(seconds);
        }

        private void Advance(float seconds)
        {
            if (seconds <= 0 || captured) return;
            age += seconds;
            if (!IsSettled)
            {
                var quiet = body.velocity.sqrMagnitude < .0025f &&
                            body.angularVelocity.sqrMagnitude < .25f;
                quietTime = quiet ? quietTime + seconds : 0;
                if (quietTime >= .35f || age >= maximumDynamicTime)
                    Settle();
            }
            if (landedAt < 0) return;
            var progress = Mathf.Clamp01(
                (age - landedAt - disappearanceDelay) / disappearanceDuration);
            if (progress <= 0) return;
            transform.localScale = initialScale *
                                   Mathf.SmoothStep(1f, 0f, progress);
            if (progress >= 1) Release();
        }

        public void ForceSettle()
        {
            if (captured || IsSettled) return;
            Settle();
        }

        private void Settle()
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.useGravity = false;
            body.isKinematic = true;
            IsSettled = true;
            MarkLanded();
        }

        public bool ConsumeIntoGrinder()
        {
            if (captured) return false;
            captured = true;
            shape.enabled = false;
            StopDynamicMotion();
            body.useGravity = false;
            body.isKinematic = true;
            gameObject.SetActive(false);
            Release();
            return true;
        }

        public void Recycle()
        {
            captured = false;
            IsSettled = true;
            shape.enabled = false;
            StopDynamicMotion();
            body.useGravity = false;
            body.isKinematic = true;
            transform.localScale = initialScale;
            gameObject.SetActive(false);
        }

        private void Release()
        {
            if (poolOwner != null)
                poolOwner.ReturnBean(this);
            else
                Destroy(gameObject);
        }

        private void StopDynamicMotion()
        {
            if (body.isKinematic) return;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
