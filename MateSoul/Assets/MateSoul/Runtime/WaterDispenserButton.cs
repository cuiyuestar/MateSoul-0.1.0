using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public sealed class WaterDispenserButton : PhysicalUiButton
    {
        [SerializeField] private WaterDispenserController dispenser;
        [SerializeField] private Transform buttonPivot;
        [SerializeField] private Vector3 pressedOffset;

        private MaterialPropertyBlock properties;
        private Renderer[] buttonRenderers;
        private Vector3 releasedPosition;
        private float pressAmount;
        private float clickPulseRemaining;
        private bool pointerPressed;
        private GameItem item;
        private bool initialized;

        public float PressAmount => pressAmount;
        public Transform ButtonPivot => buttonPivot;

        public void Configure(WaterDispenserController owner, Transform pivot,
            Vector3 inwardOffset)
        {
            if (owner == null || pivot == null || inwardOffset.sqrMagnitude < .000001f)
                throw new System.ArgumentException(
                    "Water dispenser button requires a controller, pivot and press offset.");
            dispenser = owner;
            buttonPivot = pivot;
            pressedOffset = inwardOffset;
        }

        protected override void Awake()
        {
            base.Awake();
            if (!Application.isPlaying) return;
            if (dispenser == null || buttonPivot == null)
                throw new System.InvalidOperationException(
                    "Water dispenser button bindings are incomplete.");
            releasedPosition = buttonPivot.localPosition;
            buttonRenderers = buttonPivot.GetComponentsInChildren<Renderer>();
            properties = new MaterialPropertyBlock();
            onClick.AddListener(ToggleWater);
            initialized = true;
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && dispenser != null &&
                   dispenser.isActiveAndEnabled && (item == null || item.CanPlayerUse);
        }

        private void ToggleWater()
        {
            if (!IsInteractable()) return;
            clickPulseRemaining = .12f;
            dispenser.ToggleDispensing();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (IsInteractable()) pointerPressed = true;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            pointerPressed = false;
            base.OnPointerUp(eventData);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            pointerPressed = false;
            base.OnPointerExit(eventData);
        }

        private void Update()
        {
            if (!initialized) return;
            clickPulseRemaining = Mathf.Max(
                0, clickPulseRemaining - Time.unscaledDeltaTime);
            var pressed = pointerPressed || clickPulseRemaining > 0;
            pressAmount = Mathf.MoveTowards(pressAmount,
                pressed ? 1f : 0f,
                Time.unscaledDeltaTime / (pressed ? .045f : .11f));
            buttonPivot.localPosition = releasedPosition + pressedOffset * pressAmount;
            if (buttonRenderers == null) return;
            properties.SetColor("_EmissionColor",
                new Color(.02f, .48f, 1f) *
                (dispenser.IsDispensing ? 1.3f : .15f));
            for (var index = 0; index < buttonRenderers.Length; index++)
                buttonRenderers[index].SetPropertyBlock(properties);
        }

        protected override void OnDisable()
        {
            pointerPressed = false;
            pressAmount = 0;
            clickPulseRemaining = 0;
            if (initialized && buttonPivot != null)
                buttonPivot.localPosition = releasedPosition;
            if (buttonRenderers != null)
                for (var index = 0; index < buttonRenderers.Length; index++)
                    buttonRenderers[index].SetPropertyBlock(null);
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            onClick.RemoveListener(ToggleWater);
            base.OnDestroy();
        }
    }
}
