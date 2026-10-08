using UnityEngine;
using UnityEngine.InputSystem;

namespace MateSoul.Runtime
{
    public sealed class HandPinchPose : MonoBehaviour
    {
        [SerializeField] private InputActionProperty position;
        [SerializeField] private InputActionProperty rotation;

        public void Configure(InputActionProperty positionAction, InputActionProperty rotationAction)
        {
            position = positionAction;
            rotation = rotationAction;
        }

        private void OnEnable()
        {
            position.action?.Enable();
            rotation.action?.Enable();
        }

        private void OnDisable()
        {
            position.action?.Disable();
            rotation.action?.Disable();
        }

        private void Update()
        {
            if (position.action == null || rotation.action == null) return;
            var nextPosition = position.action.ReadValue<Vector3>();
            var nextRotation = rotation.action.ReadValue<Quaternion>();
            if (!PhysicsBody.IsFinite(nextPosition) ||
                !PhysicsBody.IsFinite(nextRotation.x) || !PhysicsBody.IsFinite(nextRotation.y) ||
                !PhysicsBody.IsFinite(nextRotation.z) || !PhysicsBody.IsFinite(nextRotation.w)) return;
            transform.SetLocalPositionAndRotation(nextPosition, nextRotation);
        }
    }
}
