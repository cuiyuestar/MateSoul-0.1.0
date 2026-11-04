using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class VirtualItemFocusController : MonoBehaviour
    {
        [SerializeField, Min(.01f)] private float focusRadius = .09f;
        [SerializeField, Min(.5f)] private float focusDistance = 8f;
        private readonly RaycastHit[] focusHits = new RaycastHit[64];
        private GameItem focused;
        private PlayerInteractionModeController input;
        public GameItem FocusedItem => focused;
        public bool RecycleSelectionActive { get; private set; }

        public void BeginRecycleSelection()
        {
            if (RecycleSelectionActive) return;
            RecycleSelectionActive = true;
            if (focused == null) return;
            focused.SetFocused(false);
            focused.SetRecycleFocused(true);
        }

        public void CancelRecycleSelection()
        {
            if (!RecycleSelectionActive) return;
            RecycleSelectionActive = false;
            if (focused == null) return;
            focused.SetRecycleFocused(false);
            focused.SetFocused(true);
        }

        private void Update()
        {
            var cameraTransform = Camera.main == null ? null : Camera.main.transform;
            var next = ResolveFocusTarget(cameraTransform);
            if (next == focused) return;
            ClearFocus(focused);
            focused = next;
            ApplyFocus(focused);
        }

        private void ApplyFocus(GameItem item)
        {
            if (item == null) return;
            if (RecycleSelectionActive) item.SetRecycleFocused(true);
            else item.SetFocused(true);
        }

        private static void ClearFocus(GameItem item)
        {
            if (item == null) return;
            item.SetFocused(false);
            item.SetRecycleFocused(false);
        }

        private GameItem ResolveFocusTarget(Transform cameraTransform)
        {
            if (input == null) input = FindObjectOfType<PlayerInteractionModeController>();
            if (input != null && input.DirectHandsEnabled)
            {
                if (input.Hands == null) return null;
                for (var index = input.Hands.Length - 1; index >= 0; index--)
                {
                    var hand = input.Hands[index];
                    var nearby = hand.Direct.NearbyItem;
                    if (hand.Tracked && nearby != null && !nearby.IsHeld && !nearby.IsReserved)
                        return nearby;
                }
                return null;
            }
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
            return item != null && !item.IsHeld && !item.IsReserved ? item : null;
        }

        private void OnDisable()
        {
            ClearFocus(focused);
            focused = null;
            RecycleSelectionActive = false;
        }
    }
}
