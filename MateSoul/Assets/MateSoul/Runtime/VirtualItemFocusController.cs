using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class VirtualItemFocusController : MonoBehaviour
    {
        [SerializeField, Min(.01f)] private float focusRadius = .09f;
        [SerializeField, Min(.5f)] private float focusDistance = 8f;
        private readonly RaycastHit[] focusHits = new RaycastHit[64];
        private GameItem focused;
        public GameItem FocusedItem => focused;

        private void Update()
        {
            var cameraTransform = Camera.main == null ? null : Camera.main.transform;
            var next = ResolveFocusTarget(cameraTransform);
            if (next == focused) return;
            if (focused != null) focused.SetFocused(false);
            focused = next;
            if (focused != null) focused.SetFocused(true);
        }

        private GameItem ResolveFocusTarget(Transform cameraTransform)
        {
            if (cameraTransform == null) return null;
            var count = Physics.SphereCastNonAlloc(cameraTransform.position, focusRadius,
                cameraTransform.forward, focusHits, focusDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var closestDistance = float.PositiveInfinity;
            Collider closest = null;
            for (var i = 0; i < count; i++)
            {
                var candidate = focusHits[i].collider;
                if (candidate == null || candidate.GetComponentInParent<AvatarMotor>() != null ||
                    focusHits[i].distance >= closestDistance)
                    continue;
                closestDistance = focusHits[i].distance;
                closest = candidate;
            }
            var item = closest == null ? null : closest.GetComponentInParent<GameItem>();
            return item != null && !item.IsHeld ? item : null;
        }

        private void OnDisable()
        {
            if (focused != null) focused.SetFocused(false);
            focused = null;
        }
    }
}
