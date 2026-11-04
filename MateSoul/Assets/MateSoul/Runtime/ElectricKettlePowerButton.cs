using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public sealed class ElectricKettlePowerButton : PhysicalUiButton
    {
        [SerializeField] private ElectricKettleController kettle;
        [SerializeField] private Transform switchPivot;
        [SerializeField] private Vector3 pressedOffset;

        private Vector3 releasedPosition;
        private float pressAmount;
        private float clickPulseRemaining;
        private bool pointerPressed;
        private bool initialized;
        private GameItem item;

        public float PressAmount => pressAmount;
        public Transform SwitchPivot => switchPivot;

        public void Configure(ElectricKettleController owner, Transform pivot,
            Vector3 inwardOffset)
        {
            if (owner == null || pivot == null ||
                inwardOffset.sqrMagnitude < .000001f)
                throw new System.ArgumentException(
                    "Electric kettle switch requires a controller, pivot and press offset.");
            kettle = owner;
            switchPivot = pivot;
            pressedOffset = inwardOffset;
        }

        protected override void Awake()
        {
            base.Awake();
            if (!Application.isPlaying) return;
            if (kettle == null || switchPivot == null)
                throw new System.InvalidOperationException(
                    "Electric kettle switch bindings are incomplete.");
            releasedPosition = switchPivot.localPosition;
            onClick.AddListener(Activate);
            initialized = true;
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && kettle != null &&
                   kettle.isActiveAndEnabled &&
                   (item == null || item.CanPlayerUse);
        }

        private void Activate()
        {
            if (!IsInteractable()) return;
            clickPulseRemaining = .12f;
            kettle.PlayButtonSound();
            kettle.TogglePower();
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
            var delta = Mathf.Clamp(Time.unscaledDeltaTime, 0, .05f);
            clickPulseRemaining = Mathf.Max(0, clickPulseRemaining - delta);
            var pressed = kettle.IsPoweredOn || pointerPressed ||
                          clickPulseRemaining > 0;
            pressAmount = Mathf.MoveTowards(
                pressAmount, pressed ? 1f : 0f,
                delta / (pressed ? .045f : .11f));
            switchPivot.localPosition =
                releasedPosition + pressedOffset * pressAmount;
        }

        protected override void OnDisable()
        {
            pointerPressed = false;
            pressAmount = 0;
            clickPulseRemaining = 0;
            if (initialized && switchPivot != null)
                switchPivot.localPosition = releasedPosition;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            onClick.RemoveListener(Activate);
            base.OnDestroy();
        }
    }
}
