using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(PhysicsBody), typeof(PhysicsGrabInteractable))]
    public sealed class BowlingBall : MonoBehaviour
    {
        public PhysicsBody Body { get; private set; }
        public PhysicsGrabInteractable Grab { get; private set; }
        public event Action Released;
        public event Action PinHit;
        private bool playerCanGrab;

        private void Awake()
        {
            Body = GetComponent<PhysicsBody>();
            Body.Initialize();
            Grab = GetComponent<PhysicsGrabInteractable>();
            Grab.selectExited.AddListener(OnRelease);
        }

        public void AllowPlayer(bool value)
        {
            playerCanGrab = value;
            Grab.PlayerGrabbable = value;
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            if (playerCanGrab && !args.isCanceled) Released?.Invoke();
        }

        private void OnCollisionEnter(Collision collision) => Report(collision);
        private void OnCollisionStay(Collision collision) => Report(collision);
        private void Report(Collision collision)
        {
            if (collision.collider.GetComponentInParent<BowlingPin>() != null) PinHit?.Invoke();
        }

        private void OnDestroy()
        {
            if (Grab != null) Grab.selectExited.RemoveListener(OnRelease);
        }
    }
}
