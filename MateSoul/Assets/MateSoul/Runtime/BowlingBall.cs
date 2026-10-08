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
#region debug-point C:player-grab-state
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload = $"{{\"sessionId\":\"bowling-controller-grab\",\"runId\":\"post-fix-2\"," +
                          $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                          $"\"location\":\"BowlingBall.AllowPlayer\",\"msg\":\"[DEBUG] player grab state changed\"," +
                          $"\"data\":{{\"allowed\":{value.ToString().ToLowerInvariant()}," +
                          $"\"contact\":{Body.IsContact.ToString().ToLowerInvariant()}" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", payload, "application/json")
                .SendWebRequest();
#endif
#endregion
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
