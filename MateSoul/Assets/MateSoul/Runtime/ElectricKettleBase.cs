using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ElectricKettleBase : MonoBehaviour
    {
        [SerializeField] private Transform contactPoint;

        public Transform ContactPoint => contactPoint;

        public void Configure(Transform point)
        {
            contactPoint = point != null
                ? point
                : throw new ArgumentNullException(nameof(point));
        }

        private void Awake()
        {
            if (contactPoint == null || !contactPoint.IsChildOf(transform))
                throw new InvalidOperationException(
                    "Electric kettle base contact point is missing.");
        }
    }
}
