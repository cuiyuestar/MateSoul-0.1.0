using System;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class CoffeeGrinderController : MonoBehaviour, IRuntimeWorldFactSource
    {
        [SerializeField] private CoffeeBeanVolumeRenderer beanVolume;
        [SerializeField] private GameObject powderVisual;
        [SerializeField] private ParticleSystem powderParticles;
        [SerializeField] private Transform interiorCenter;
        [SerializeField, Min(.1f)] private float requiredStirSeconds = 4f;
        [SerializeField, Min(.01f)] private float minimumTipSpeed = .035f;
        [SerializeField, Min(.01f)] private float interiorRadius = .061f;
        [SerializeField, Min(.01f)] private float interiorHeight = .075f;

        private CoffeePestle observedPestle;
        private Vector3 previousTip;
        private bool hasPreviousTip;
        private int receivedBeanCount;
        private int groundBeanCount;
        private float grindingProgress;
        private SystemNotifications notifications;
        private Vector3 powderBaseScale;

        public int ReceivedBeanCount => receivedBeanCount;
        public int GroundBeanCount => groundBeanCount;
        public float GrindingProgress => grindingProgress;
        public float RequiredStirSeconds => requiredStirSeconds;
        public bool HasPowder => groundBeanCount > 0;
        public CoffeeBeanVolumeRenderer BeanVolume => beanVolume;
        public GameObject PowderVisual => powderVisual;

        public bool TryCaptureRuntimeWorldFact(
            out RuntimeWorldFactSnapshot fact)
        {
            if (receivedBeanCount <= 0 && groundBeanCount <= 0 &&
                grindingProgress <= 0)
            {
                fact = null;
                return false;
            }
            var state = groundBeanCount > 0
                ? "ground"
                : grindingProgress > 0 ? "grinding" : "loaded";
            var progress = requiredStirSeconds <= 0
                ? 0
                : Mathf.RoundToInt(Mathf.Clamp01(
                    grindingProgress / requiredStirSeconds) * 100f);
            fact = RuntimeWorldFact.Create(
                this,
                "coffee",
                state,
                $"whole_beans={receivedBeanCount};ground_units={groundBeanCount};" +
                $"progress={progress}%");
            return true;
        }

        public void BindNotifications(SystemNotifications value)
            => notifications = value ??
               throw new ArgumentNullException(nameof(value));

        public void Configure(CoffeeBeanVolumeRenderer beans, GameObject powder,
            ParticleSystem particles, Transform center, float seconds = 4f,
            float tipSpeed = .035f, float radius = .061f, float height = .075f)
        {
            beanVolume = beans != null
                ? beans
                : throw new ArgumentNullException(nameof(beans));
            powderVisual = powder != null
                ? powder
                : throw new ArgumentNullException(nameof(powder));
            powderParticles = particles != null
                ? particles
                : throw new ArgumentNullException(nameof(particles));
            interiorCenter = center != null
                ? center
                : throw new ArgumentNullException(nameof(center));
            if (!PhysicsBody.IsFinite(seconds) || seconds <= 0 ||
                !PhysicsBody.IsFinite(tipSpeed) || tipSpeed <= 0 ||
                !PhysicsBody.IsFinite(radius) || radius <= 0 ||
                !PhysicsBody.IsFinite(height) || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            requiredStirSeconds = seconds;
            minimumTipSpeed = tipSpeed;
            interiorRadius = radius;
            interiorHeight = height;
            powderBaseScale = powderVisual.transform.localScale;
            beanVolume.SetVisibleCount(0);
            RefreshPowderVisual();
            powderParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Awake()
        {
            if (beanVolume == null || powderVisual == null ||
                powderParticles == null || interiorCenter == null)
                throw new InvalidOperationException(
                    "Coffee grinder bindings are incomplete.");
            powderBaseScale = powderVisual.transform.localScale;
            beanVolume.SetVisibleCount(receivedBeanCount);
            RefreshPowderVisual();
        }

        public bool TryAcceptBean(CoffeeBean bean)
        {
            if (bean == null || bean.IsCaptured ||
                receivedBeanCount >= beanVolume.Capacity)
                return false;
            if (!bean.ConsumeIntoGrinder()) return false;
            receivedBeanCount++;
            grindingProgress = 0;
            beanVolume.gameObject.SetActive(true);
            beanVolume.SetVisibleCount(receivedBeanCount);
            return true;
        }

        public void ObservePestle(CoffeePestle pestle, float seconds)
        {
            if (pestle == null || seconds <= 0 || receivedBeanCount <= 0) return;
            var local = interiorCenter.InverseTransformPoint(pestle.TipPosition);
            var inside = new Vector2(local.x, local.z).sqrMagnitude <=
                         interiorRadius * interiorRadius &&
                         local.y >= 0 && local.y <= interiorHeight;
            if (!inside)
            {
                StopObserving(pestle);
                return;
            }
            if (observedPestle != pestle)
            {
                observedPestle = pestle;
                previousTip = pestle.TipPosition;
                hasPreviousTip = true;
                return;
            }
            if (!hasPreviousTip)
            {
                previousTip = pestle.TipPosition;
                hasPreviousTip = true;
                return;
            }
            var speed = Vector3.Distance(previousTip, pestle.TipPosition) /
                        Mathf.Max(.0001f, seconds);
            previousTip = pestle.TipPosition;
            AdvanceGrinding(seconds, speed >= minimumTipSpeed);
        }

        public void StopObserving(CoffeePestle pestle)
        {
            if (pestle != null && observedPestle != pestle) return;
            observedPestle = null;
            hasPreviousTip = false;
        }

        public void AdvanceGrindingForTesting(float seconds, bool stirring)
        {
            if (!PhysicsBody.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            AdvanceGrinding(seconds, stirring);
        }

        public int TransferGroundCoffeeTo(CoffeeBrewReceiver target,
            int requestedUnits, Vector3 targetWorldPoint)
        {
            if (target == null || requestedUnits <= 0 || groundBeanCount <= 0)
                return 0;
            var available = Mathf.Min(requestedUnits, groundBeanCount);
            var accepted = target.ReceiveGroundCoffee(
                available, targetWorldPoint);
            if (accepted <= 0) return 0;
            groundBeanCount -= accepted;
            RefreshPowderVisual();
            return accepted;
        }

        private void AdvanceGrinding(float seconds, bool stirring)
        {
            if (receivedBeanCount <= 0 || !stirring || seconds <= 0) return;
            grindingProgress += seconds;
            if (grindingProgress >= requiredStirSeconds) CompleteGrinding();
        }

        private void CompleteGrinding()
        {
            groundBeanCount += receivedBeanCount;
            receivedBeanCount = 0;
            grindingProgress = 0;
            beanVolume.SetVisibleCount(0);
            beanVolume.gameObject.SetActive(false);
            RefreshPowderVisual();
            powderParticles.Play(true);
            if (notifications != null)
            {
                notifications.ShowHint("咖啡粉磨制完成");
                return;
            }
            var item = GetComponent<GameItem>();
            if (item?.Session != null)
                item.Session.Notifications.ShowHint("咖啡粉磨制完成");
            else
            {
                var bootstrap = FindObjectOfType<GameBootstrap>();
                bootstrap?.Notifications.ShowHint("咖啡粉磨制完成");
            }
        }

        private void RefreshPowderVisual()
        {
            if (powderVisual == null) return;
            powderVisual.SetActive(HasPowder);
            if (!HasPowder) return;
            var amount = Mathf.Clamp01(groundBeanCount / 240f);
            powderVisual.transform.localScale = new Vector3(
                powderBaseScale.x,
                powderBaseScale.y * Mathf.Lerp(.30f, 1f, amount),
                powderBaseScale.z);
        }
    }
}
