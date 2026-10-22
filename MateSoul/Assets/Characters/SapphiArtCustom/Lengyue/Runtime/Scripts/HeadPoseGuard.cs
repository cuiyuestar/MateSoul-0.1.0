using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Animation
{
    /// <summary>
    /// Keeps the head and neck stable after body animation is evaluated.
    /// Production clips must not contain Neck, Head, eye, or facial curves.
    /// </summary>
    [DefaultExecutionOrder(12100)]
    public sealed class HeadPoseGuard : MonoBehaviour
    {
        public bool autoBind = true;
        public Transform hips;
        public Transform spine;
        public Transform chest;
        public Transform neck;
        public Transform head;

        public bool IsInitialized => initialized;
        public float LastHeadWorldRotationDeviation { get; private set; }
        public float LastHeadNeckDistanceRatioDeviation { get; private set; }

        [SerializeField, HideInInspector] bool hasRestPose;
        [SerializeField, HideInInspector] Quaternion restHipsLocalRotation;
        [SerializeField, HideInInspector] Vector3 restHipsLocalScale;
        [SerializeField, HideInInspector] Quaternion restHeadRootRotation;
        [SerializeField, HideInInspector] Quaternion restSpineLocalRotation;
        [SerializeField, HideInInspector] Vector3 restSpineLocalPosition;
        [SerializeField, HideInInspector] Vector3 restSpineLocalScale;
        [SerializeField, HideInInspector] Quaternion restChestLocalRotation;
        [SerializeField, HideInInspector] Vector3 restChestLocalPosition;
        [SerializeField, HideInInspector] Vector3 restChestLocalScale;
        [SerializeField, HideInInspector] Quaternion restNeckLocalRotation;
        [SerializeField, HideInInspector] Vector3 restNeckLocalPosition;
        [SerializeField, HideInInspector] Vector3 restNeckLocalScale;
        [SerializeField, HideInInspector] Quaternion restHeadLocalRotation;
        [SerializeField, HideInInspector] Vector3 restHeadLocalPosition;
        [SerializeField, HideInInspector] Vector3 restHeadLocalScale;
        [SerializeField, HideInInspector] float restHeadNeckDistance;
        bool initialized;

        void Awake()
        {
            if (autoBind) BindByName();
            Initialize();
        }

        public void BindByName()
        {
            var lookup = new Dictionary<string, Transform>();
            foreach (Transform item in
                transform.GetComponentsInChildren<Transform>(true))
                if (!lookup.ContainsKey(item.name))
                    lookup.Add(item.name, item);

            if (!lookup.TryGetValue("Hips", out hips))
                throw new InvalidOperationException(
                    "Missing head-pose-guard bone: Hips");
            if (!lookup.TryGetValue("Spine", out spine))
                throw new InvalidOperationException(
                    "Missing head-pose-guard bone: Spine");
            if (!lookup.TryGetValue("Chest", out chest))
                throw new InvalidOperationException(
                    "Missing head-pose-guard bone: Chest");
            if (!lookup.TryGetValue("Neck", out neck))
                throw new InvalidOperationException(
                    "Missing head-pose-guard bone: Neck");
            if (!lookup.TryGetValue("Head 1", out head))
                throw new InvalidOperationException(
                    "Missing head-pose-guard bone: Head 1");
        }

        public void Initialize()
        {
            if (!hips || !spine || !chest || !neck || !head)
            {
                initialized = false;
                return;
            }
            if (!hasRestPose)
            {
                restHipsLocalRotation = hips.localRotation;
                restHipsLocalScale = hips.localScale;
                restHeadRootRotation =
                    Quaternion.Inverse(transform.rotation) * head.rotation;
                restSpineLocalRotation = spine.localRotation;
                restSpineLocalPosition = spine.localPosition;
                restSpineLocalScale = spine.localScale;
                restChestLocalRotation = chest.localRotation;
                restChestLocalPosition = chest.localPosition;
                restChestLocalScale = chest.localScale;
                restNeckLocalRotation = neck.localRotation;
                restNeckLocalPosition = neck.localPosition;
                restNeckLocalScale = neck.localScale;
                restHeadLocalRotation = head.localRotation;
                restHeadLocalPosition = head.localPosition;
                restHeadLocalScale = head.localScale;
                restHeadNeckDistance = Vector3.Distance(
                    neck.position, head.position);
                hasRestPose = true;
            }
            initialized = restHeadNeckDistance > Mathf.Epsilon;
        }

        void LateUpdate()
        {
            Evaluate();
        }

        public void Evaluate()
        {
            if (!initialized)
            {
                if (autoBind) BindByName();
                Initialize();
                if (!initialized) return;
            }

            hips.localRotation = restHipsLocalRotation;
            hips.localScale = restHipsLocalScale;
            spine.SetLocalPositionAndRotation(
                restSpineLocalPosition, restSpineLocalRotation);
            spine.localScale = restSpineLocalScale;
            chest.SetLocalPositionAndRotation(
                restChestLocalPosition, restChestLocalRotation);
            chest.localScale = restChestLocalScale;
            neck.SetLocalPositionAndRotation(
                restNeckLocalPosition, restNeckLocalRotation);
            neck.localScale = restNeckLocalScale;
            head.localPosition = restHeadLocalPosition;
            head.localRotation = restHeadLocalRotation;
            head.localScale = restHeadLocalScale;

            LastHeadWorldRotationDeviation = Quaternion.Angle(
                transform.rotation * restHeadRootRotation,
                head.rotation);
            LastHeadNeckDistanceRatioDeviation = Mathf.Abs(
                Vector3.Distance(neck.position, head.position)
                    / restHeadNeckDistance - 1f);
        }
    }
}
