using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MateSoul.Runtime
{
    public enum SeasoningDispenseMode { InvertedSalt, SqueezedSauce }

    /// <summary>Owns dispensing visuals only. XRI and PhysicsBody retain grip/motion ownership.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class SeasoningDispenser : MonoBehaviour
    {
        public const int SaltParticleLimit = 768;
        public const int SauceParticleLimit = 160;
        public const float SaltEnterDegrees = 160f;
        public const float SaltExitDegrees = 150f;

        [SerializeField] private SeasoningDispenseMode mode;
        [SerializeField] private Transform outlet;
        [SerializeField] private ParticleSystem stream;
        [SerializeField] private SkinnedMeshRenderer[] squeezeRenderers =
            Array.Empty<SkinnedMeshRenderer>();
        private PhysicsBody body;
        private PhysicsGrabInteractable grab;
        private Camera playerCamera;
        private IXRSelectInteractor inputOwner;
        private float relaxedGrip;
        private bool triggerArmed, suspended;
        private float compression;

        public SeasoningDispenseMode Mode => mode;
        public Transform Outlet => outlet;
        public ParticleSystem Stream => stream;
        public bool IsDispensing { get; private set; }
        public float Pressure { get; private set; }

        public void Configure(SeasoningDispenseMode dispenseMode,
            Transform pourOutlet, ParticleSystem particles,
            params SkinnedMeshRenderer[] deformingMeshes)
        {
            if (pourOutlet == null || particles == null ||
                !pourOutlet.IsChildOf(transform) ||
                !particles.transform.IsChildOf(pourOutlet) ||
                deformingMeshes == null ||
                (dispenseMode != SeasoningDispenseMode.InvertedSalt &&
                 dispenseMode != SeasoningDispenseMode.SqueezedSauce))
                throw new ArgumentException("Seasoning dispenser bindings are incomplete.");
            foreach (var renderer in deformingMeshes)
                if (renderer == null || !renderer.transform.IsChildOf(transform) ||
                    renderer.sharedMesh.GetBlendShapeIndex("Squeeze") < 0)
                    throw new ArgumentException("Bottle mesh needs its Squeeze blend shape.");
            if (dispenseMode == SeasoningDispenseMode.SqueezedSauce &&
                deformingMeshes.Length == 0)
                throw new ArgumentException("Sauce bottle needs a deforming body.");
            mode = dispenseMode;
            outlet = pourOutlet;
            stream = particles;
            squeezeRenderers = (SkinnedMeshRenderer[])deformingMeshes.Clone();
            stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Awake()
        {
            if (outlet == null || stream == null)
                throw new InvalidOperationException("Seasoning dispenser has no outlet/effect.");
            body = GetComponent<PhysicsBody>();
            playerCamera = Camera.main;
            stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Update() => Advance(Time.deltaTime);

        private void Advance(float delta)
        {
            if (suspended || body.IsPaused || delta <= 0)
            {
                ResetDispensing(true);
                return;
            }
            if (playerCamera != null &&
                (playerCamera.transform.position - outlet.position).sqrMagnitude > 9f)
            {
                ResetDispensing(false);
                return;
            }

            var pressure = 0f;
            if (mode == SeasoningDispenseMode.InvertedSalt)
            {
                var tilt = Vector3.Dot(outlet.up, Vector3.up);
                var threshold = Mathf.Cos(
                    (IsDispensing ? SaltExitDegrees : SaltEnterDegrees) * Mathf.Deg2Rad);
                if (tilt <= threshold)
                    pressure = Mathf.Lerp(.65f, 1f, Mathf.InverseLerp(threshold, -1f, tilt));
            }
            else
            {
                if (grab == null) grab = GetComponent<PhysicsGrabInteractable>();
                pressure = ReadSqueezePressure();
            }
            SetPressure(pressure, delta);
        }

        private float ReadSqueezePressure()
        {
            var owner = grab != null && grab.isSelected ? grab.DrivingHand : null;
            if (owner == null || !owner.transform.gameObject.activeInHierarchy)
            {
                inputOwner = null;
                return 0;
            }
            float strength;
            var direct = owner as PicoDirectHandInteractor;
            if (direct != null)
            {
                if (!direct.TryGetSqueezeStrength(out strength))
                {
                    inputOwner = null;
                    return 0;
                }
            }
            else
            {
                var controller = (owner as XRBaseInputInteractor)?.xrController;
                if (controller == null ||
                    controller.enableInputTracking &&
                    (controller.currentControllerState.inputTrackingState &
                     (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                    (InputTrackingState.Position | InputTrackingState.Rotation))
                {
                    inputOwner = null;
                    return 0;
                }
                // The production controller already maps the trigger to UI Press.
                // Read its analogue value without adding a second input action.
                strength = controller.uiPressInteractionState.value;
            }
            if (!PhysicsBody.IsFinite(strength))
            {
                inputOwner = null;
                return 0;
            }
            strength = Mathf.Clamp01(strength);
            if (!ReferenceEquals(inputOwner, owner))
            {
                inputOwner = owner;
                relaxedGrip = strength;
                triggerArmed = strength <= .1f;
                return 0;
            }
            if (direct == null)
            {
                if (strength <= .1f) triggerArmed = true;
                return triggerArmed ? Mathf.InverseLerp(.15f, 1f, strength) : 0;
            }
            relaxedGrip = Mathf.Min(relaxedGrip, strength);
            var extraCurl = strength - relaxedGrip;
            if (extraCurl < (IsDispensing ? .06f : .12f)) return 0;
            return Mathf.InverseLerp(.06f, Mathf.Max(.24f, 1f - relaxedGrip), extraCurl);
        }

        private void SetPressure(float value, float delta)
        {
            Pressure = value;
            compression = Mathf.MoveTowards(compression, value, delta * 9f);
            foreach (var renderer in squeezeRenderers)
                renderer.SetBlendShapeWeight(
                    renderer.sharedMesh.GetBlendShapeIndex("Squeeze"), compression * 100f);
            IsDispensing = value > 0;
            if (IsDispensing)
            {
                var emission = stream.emission;
                emission.rateOverTime = mode == SeasoningDispenseMode.InvertedSalt
                    ? 800f * value : 180f * value;
                var main = stream.main;
                main.startSpeed = mode == SeasoningDispenseMode.InvertedSalt
                    ? .12f : Mathf.Lerp(.35f, 1.15f, value);
                if (!stream.isPlaying) stream.Play(true);
            }
            else if (stream.isPlaying || stream.isEmitting)
                stream.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void ResetDispensing(bool clear)
        {
            inputOwner = null;
            Pressure = compression = 0;
            IsDispensing = false;
            foreach (var renderer in squeezeRenderers)
                renderer.SetBlendShapeWeight(
                    renderer.sharedMesh.GetBlendShapeIndex("Squeeze"), 0);
            if (stream != null && (clear || stream.isPlaying || stream.isEmitting))
                stream.Stop(true, clear
                    ? ParticleSystemStopBehavior.StopEmittingAndClear
                    : ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnApplicationPause(bool paused)
        {
            suspended = paused;
            if (paused) ResetDispensing(true);
        }

        private void OnDisable() => ResetDispensing(true);
    }
}
