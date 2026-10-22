using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class CoffeePestle : MonoBehaviour
    {
        [SerializeField] private Transform grindingTip;

        public Transform GrindingTip => grindingTip;
        public Vector3 TipPosition => grindingTip != null
            ? grindingTip.position
            : transform.position;

        public void Configure(Transform tip)
            => grindingTip = tip != null
                ? tip
                : throw new ArgumentNullException(nameof(tip));
    }
}
