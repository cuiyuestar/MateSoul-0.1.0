using UnityEngine;

namespace MateSoul.Runtime
{
    /// <summary>Marks a virtual fingertip that can depress world-space controls.</summary>
    public sealed class PhysicalPressInteractor : MonoBehaviour
    {
        [SerializeField] private Transform pressPoint;
        public Vector3 PressPosition => pressPoint == null ? transform.position : pressPoint.position;

        public void Configure(Transform point) => pressPoint = point;
    }
}
