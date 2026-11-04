using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class CoffeeBeanJarController : MonoBehaviour, IRuntimeWorldFactSource
    {
        [SerializeField] private CoffeeBeanJarLid lid;
        [SerializeField] private CoffeeBeanVolumeRenderer beanFill;
        [SerializeField] private Transform pourOrigin;
        [SerializeField] private CoffeeBean beanPrefab;
        [SerializeField, Range(1, 1024)] private int beanCapacity = 336;
        [SerializeField, Range(1, 32)] private int beansPerRelease = 8;
        [SerializeField, Min(.02f)] private float releaseInterval = .11f;
        [SerializeField, Range(-1f, 1f)] private float pourUpDotThreshold = .52f;
        [SerializeField, Range(8, 64)] private int maximumPhysicalBeans = 24;
        [SerializeField, Range(1, 8)] private int physicalBeansPerRelease = 2;

        private readonly List<CoffeeBean> released = new List<CoffeeBean>();
        private readonly Queue<CoffeeBean> available = new Queue<CoffeeBean>();
        private PhysicsBody body;
        private Transform poolRoot;
        private float releaseClock;
        private int releasedBeanCount;
        private bool configured;

        public CoffeeBeanJarLid Lid => lid;
        public int BeanCapacity => beanCapacity;
        public int BeansPerRelease => beansPerRelease;
        public int ReleasedBeanCount => releasedBeanCount;
        public int RemainingBeanCount => beanCapacity - releasedBeanCount;
        public int MaximumPhysicalBeans => maximumPhysicalBeans;
        public int PhysicalBeansPerRelease => physicalBeansPerRelease;
        public float FillRatio => beanCapacity <= 0
            ? 0
            : RemainingBeanCount / (float)beanCapacity;
        public CoffeeBeanVolumeRenderer BeanFill => beanFill;
        public IReadOnlyList<CoffeeBean> ReleasedBeans => released;

        public bool TryCaptureRuntimeWorldFact(
            out RuntimeWorldFactSnapshot fact)
        {
            if ((lid == null || !lid.IsOpen) &&
                RemainingBeanCount == beanCapacity)
            {
                fact = null;
                return false;
            }
            var state = RemainingBeanCount <= 0
                ? "empty"
                : lid != null && lid.IsOpen ? "open" : "used";
            fact = RuntimeWorldFact.Create(
                this,
                "coffee",
                state,
                $"beans_remaining={RemainingBeanCount};capacity={beanCapacity}");
            return true;
        }

        public void Configure(CoffeeBeanJarLid lidControl,
            CoffeeBeanVolumeRenderer fillRenderer,
            Transform outlet, CoffeeBean physicalBeanPrefab, int capacity = 336,
            int releaseSize = 8, float interval = .06f,
            float tiltDot = .52f, int physicalLimit = 24,
            int physicalReleaseSize = 2)
        {
            lid = lidControl != null
                ? lidControl
                : throw new ArgumentNullException(nameof(lidControl));
            beanFill = fillRenderer != null
                ? fillRenderer
                : throw new ArgumentNullException(nameof(fillRenderer));
            pourOrigin = outlet != null
                ? outlet
                : throw new ArgumentNullException(nameof(outlet));
            beanPrefab = physicalBeanPrefab != null
                ? physicalBeanPrefab
                : throw new ArgumentNullException(nameof(physicalBeanPrefab));
            if (capacity < 1 || capacity > 1024 ||
                releaseSize < 1 || releaseSize > 32 ||
                physicalLimit < 8 || physicalLimit > 64 ||
                physicalReleaseSize < 1 ||
                physicalReleaseSize > Mathf.Min(8, releaseSize) ||
                !PhysicsBody.IsFinite(interval) || interval < .02f ||
                !PhysicsBody.IsFinite(tiltDot) || tiltDot < -1 || tiltDot > 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            beanCapacity = capacity;
            beansPerRelease = releaseSize;
            releaseInterval = interval;
            pourUpDotThreshold = tiltDot;
            maximumPhysicalBeans = physicalLimit;
            physicalBeansPerRelease = physicalReleaseSize;
            body = GetComponent<PhysicsBody>();
            configured = true;
            UpdateFillVisual();
        }

        private void Awake()
        {
            if (configured) return;
            if (lid == null || beanFill == null || pourOrigin == null ||
                beanPrefab == null)
                throw new InvalidOperationException(
                    "Coffee bean jar bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            configured = true;
            PrewarmPool();
            UpdateFillVisual();
        }

        private void Update() => Advance(Time.deltaTime, false);

        public void AdvanceForTesting(float seconds, bool forcePour = false)
        {
            if (!PhysicsBody.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            Advance(seconds, forcePour);
        }

        private void Advance(float seconds, bool forcePour)
        {
            if (!configured || seconds <= 0 || RemainingBeanCount <= 0)
                return;
            var tilted = Vector3.Dot(transform.up, Vector3.up) <=
                         pourUpDotThreshold;
            if (!lid.IsOpen || !(forcePour || tilted))
            {
                releaseClock = 0;
                return;
            }
            releaseClock += seconds;
            while (releaseClock >= releaseInterval &&
                   RemainingBeanCount > 0)
            {
                releaseClock -= releaseInterval;
                ReleaseBatch();
            }
        }

        public bool ApplyLidImpulse(float impulse) => lid.ApplyImpulse(impulse);

        private void ReleaseBatch()
        {
            var logicalCount = Mathf.Min(beansPerRelease, RemainingBeanCount);
            var physicalCount = Mathf.Min(physicalBeansPerRelease, logicalCount);
            var firstSequence = releasedBeanCount;
            releasedBeanCount += logicalCount;
            for (var index = 0; index < physicalCount; index++)
                ReleaseBean(index, physicalCount, firstSequence + index);
            UpdateFillVisual();
        }

        private void ReleaseBean(int batchIndex, int batchCount, int sequence)
        {
            var bean = AcquireBean();
            bean.name = $"Poured Coffee Bean {sequence + 1:000}";
            var angle = (sequence * 137.50776f +
                         batchIndex * (360f / batchCount)) * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var rotation = Quaternion.Euler(
                sequence * 31f % 360f,
                sequence * 47f % 360f,
                sequence * 17f % 360f);
            var pose = new Pose(
                pourOrigin.position + radial * (.002f + batchIndex * .00045f),
                rotation);
            var inheritedVelocity = body != null
                ? body.Rigidbody.GetPointVelocity(pourOrigin.position)
                : Vector3.zero;
            var velocity = inheritedVelocity + transform.up * .07f +
                           Vector3.down * (.08f + batchIndex * .006f) +
                           radial * (.025f + batchIndex * .003f);
            var angularVelocity = new Vector3(
                .8f + batchIndex * .13f,
                1.5f + sequence % 5 * .2f,
                -.6f + batchIndex * .11f);
            bean.gameObject.SetActive(true);
            bean.Launch(pose, velocity, angularVelocity);
            released.Add(bean);
        }

        private void PrewarmPool()
        {
            if (available.Count > 0 || released.Count > 0) return;
            var poolObject = new GameObject($"{name} Coffee Bean Pool");
            poolRoot = poolObject.transform;
            poolRoot.SetParent(transform.parent, false);
            for (var index = 0; index < maximumPhysicalBeans; index++)
            {
                var bean = Instantiate(beanPrefab, poolRoot);
                bean.name = $"Pooled Coffee Bean {index + 1:00}";
                bean.SetPoolOwner(this);
                bean.Recycle();
                available.Enqueue(bean);
            }
        }

        private CoffeeBean AcquireBean()
        {
            PrewarmPool();
            if (available.Count == 0 && released.Count > 0)
                ReturnBean(released[0]);
            if (available.Count == 0)
                throw new InvalidOperationException(
                    "Coffee bean pool has no reusable instance.");
            return available.Dequeue();
        }

        public void ReturnBean(CoffeeBean bean)
        {
            if (bean == null || !released.Remove(bean) &&
                available.Contains(bean))
                return;
            bean.Recycle();
            bean.transform.SetParent(poolRoot, false);
            available.Enqueue(bean);
        }

        private void OnDestroy()
        {
            released.Clear();
            if (poolRoot != null) Destroy(poolRoot.gameObject);
            if (lid != null && !lid.transform.IsChildOf(transform))
                Destroy(lid.gameObject);
        }

        private void UpdateFillVisual()
        {
            if (beanFill == null) return;
            beanFill.gameObject.SetActive(RemainingBeanCount > 0);
            beanFill.SetVisibleRatio(FillRatio);
        }
    }
}
