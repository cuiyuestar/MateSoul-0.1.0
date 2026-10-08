using System;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(GameItem))]
    public sealed class BreakableItem : MonoBehaviour
    {
        private const float ArmingDelay = .25f;

        private GameItem item;
        private BreakableSettings settings;
        private float armedAt;
        private bool broken;

        public void Configure(GameItem owner, BreakableSettings value)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            value.Validate();
            if (!value.enabled) throw new ArgumentException("BreakableItem requires enabled settings.");
            item = owner;
            settings = value;
            armedAt = Time.fixedTime + ArmingDelay;
            broken = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null) return;
            var force = collision.impulse.magnitude / Mathf.Max(Time.fixedDeltaTime, .001f);
            var point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            HandleImpact(force, point);
        }

        private void HandleImpact(float force, Vector3 point)
        {
            if (broken || item == null || !gameObject.activeInHierarchy ||
                Time.fixedTime < armedAt || !PhysicsBody.IsFinite(force) ||
                force < settings.lightForceThreshold ||
                item.IsHeld || item.Body.IsFollowing || item.Body.IsPaused || item.Body.IsContact)
                return;

            var severity = force >= settings.heavyForceThreshold
                ? BreakSeverity.Heavy
                : BreakSeverity.Light;
            var pose = item.Body.Pose;
            var inheritedVelocity = item.Body.Rigidbody.velocity;
            var inheritedAngularVelocity = item.Body.Rigidbody.angularVelocity;
            var parent = transform.parent;
            var definition = item.Definition;
            var session = item.Session;
            var materials = GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && material != definition.FocusMaterial)
                .Distinct()
                .ToArray();
            if (parent == null || session == null || materials.Length == 0) return;

            broken = true;
            BreakableDebris debris = null;
            try
            {
                debris = BreakableDebris.Create(session, parent, definition.DisplayName, pose,
                    definition.Footprint, definition.PlacementHeight, definition.Physics,
                    settings, severity, materials, point, inheritedVelocity,
                    inheritedAngularVelocity);
                if (!item.TryBreak(severity))
                {
                    Destroy(debris.gameObject);
                    broken = false;
                }
            }
            catch
            {
                if (debris != null) Destroy(debris.gameObject);
                broken = false;
                throw;
            }
        }

#if UNITY_EDITOR
        public void ApplyImpactForTesting(float force)
            => HandleImpact(force, transform.position - transform.up * .01f);
#endif
    }
}
