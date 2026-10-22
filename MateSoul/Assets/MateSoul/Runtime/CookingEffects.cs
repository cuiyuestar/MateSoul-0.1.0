using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class CookingEffects : MonoBehaviour
    {
        public const string SizzleResource = "CookingEffects/FoodSizzle";
        public const string SmokeMaterialResource = "CookingEffects/Smoke";

        [SerializeField] private AudioClip sizzleClip;
        [SerializeField] private Material smokeMaterial;

        private AudioSource sizzleSource;
        private ParticleSystem smoke;
        private float targetVolume;
        private bool heating;

        public bool IsHeating => heating;
        public AudioClip SizzleClip => sizzleClip;
        public Material SmokeMaterial => smokeMaterial;
        public AudioSource SizzleSource => sizzleSource;
        public ParticleSystem Smoke => smoke;

        public void Configure(AudioClip clip, Material material)
        {
            if (clip == null || material == null)
                throw new ArgumentException(
                    "Cooking effects require a sizzle clip and smoke material.");
            sizzleClip = clip;
            smokeMaterial = material;
        }

        public void SetHeating(bool value, float temperatureCelsius)
        {
            EnsureRuntime();
            heating = value;
            if (!value)
            {
                targetVolume = 0;
                var emission = smoke.emission;
                emission.enabled = false;
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            var heat = Mathf.InverseLerp(
                HeatingBaseController.MinimumTemperature,
                HeatingBaseController.MaximumTemperature,
                temperatureCelsius);
            targetVolume = Mathf.Lerp(.34f, .62f, heat);
            sizzleSource.pitch = Mathf.Lerp(.92f, 1.08f, heat);
            if (!sizzleSource.isPlaying) sizzleSource.Play();

            var activeEmission = smoke.emission;
            activeEmission.enabled = true;
            activeEmission.rateOverTime = Mathf.Lerp(5f, 13f, heat);
            if (!smoke.isPlaying) smoke.Play(true);
        }

        public void AdvanceForTesting(float seconds)
        {
            AdvanceAudio(seconds);
        }

        private void Awake()
        {
            EnsureRuntime();
        }

        private void Update()
        {
            AdvanceAudio(Time.unscaledDeltaTime);
        }

        private void AdvanceAudio(float seconds)
        {
            if (sizzleSource == null || seconds <= 0) return;
            sizzleSource.volume = Mathf.MoveTowards(
                sizzleSource.volume, targetVolume, seconds * 2.5f);
            if (!heating && sizzleSource.volume <= .001f &&
                sizzleSource.isPlaying)
                sizzleSource.Stop();
        }

        private void EnsureRuntime()
        {
            if (sizzleClip == null)
                sizzleClip = Resources.Load<AudioClip>(SizzleResource);
            if (smokeMaterial == null)
                smokeMaterial = Resources.Load<Material>(SmokeMaterialResource);
            if (sizzleClip == null || smokeMaterial == null)
                throw new InvalidOperationException(
                    "Cooking effect resources are missing.");

            if (sizzleSource == null)
            {
                sizzleSource = gameObject.AddComponent<AudioSource>();
                sizzleSource.clip = sizzleClip;
                sizzleSource.playOnAwake = false;
                sizzleSource.loop = true;
                sizzleSource.spatialBlend = 1f;
                sizzleSource.dopplerLevel = 0;
                sizzleSource.rolloffMode = AudioRolloffMode.Logarithmic;
                sizzleSource.minDistance = .25f;
                sizzleSource.maxDistance = 5f;
                sizzleSource.volume = 0;
            }
            if (smoke == null) smoke = CreateSmoke();
        }

        private ParticleSystem CreateSmoke()
        {
            var smokeObject = new GameObject("Cooking Steam");
            smokeObject.transform.SetParent(transform, false);
            var shapeBounds = GetComponentInChildren<Collider>().bounds;
            smokeObject.transform.position = new Vector3(
                shapeBounds.center.x,
                shapeBounds.max.y + .004f,
                shapeBounds.center.z);

            var system = smokeObject.AddComponent<ParticleSystem>();
            system.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.035f, .09f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .06f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, .14f),
                new Color(.92f, .96f, 1f, .28f));
            main.maxParticles = 36;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                Mathf.Max(.03f, shapeBounds.size.x * .72f),
                .004f,
                Mathf.Max(.03f, shapeBounds.size.z * .72f));

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(.045f, .11f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .035f;
            noise.frequency = .45f;
            noise.scrollSpeed = .18f;
            noise.damping = true;

            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0),
                    new GradientColorKey(new Color(.92f, .96f, 1f), 1)
                },
                new[]
                {
                    new GradientAlphaKey(0, 0),
                    new GradientAlphaKey(.34f, .18f),
                    new GradientAlphaKey(.18f, .72f),
                    new GradientAlphaKey(0, 1)
                });
            color.color = gradient;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0, .45f, 1, 1.7f));

            var renderer = smokeObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            return system;
        }

        private void OnDisable()
        {
            heating = false;
            targetVolume = 0;
            if (sizzleSource != null) sizzleSource.Stop();
            if (smoke != null)
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
