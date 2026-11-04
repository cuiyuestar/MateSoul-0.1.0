using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MateSoul.Runtime
{
    /// <summary>Physical switches respond on fingertip contact; mouse input keeps release-click.</summary>
    public class PhysicalUiButton : Button
    {
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (IsTouch(eventData) && eventData.button == PointerEventData.InputButton.Left &&
                IsActive() && IsInteractable())
                onClick.Invoke();
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (!IsTouch(eventData)) base.OnPointerClick(eventData);
        }

        private static bool IsTouch(PointerEventData eventData)
            => eventData is TrackedDeviceEventData tracked && tracked.interactor is PicoHandPoke;
    }
}
