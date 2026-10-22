using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public enum HeatingBaseButtonKind
    {
        Power,
        Decrease,
        Increase
    }

    public sealed class HeatingBaseButton : Button
    {
        [SerializeField] private HeatingBaseController controller;
        [SerializeField] private HeatingBaseButtonKind kind;
        [SerializeField] private Transform buttonPivot;
        [SerializeField] private Vector3 pressedOffset;

        private Vector3 releasedPosition;
        private float pressAmount;
        private float clickPulseRemaining;
        private bool pointerPressed;
        private bool initialized;
        private GameItem item;

        public HeatingBaseButtonKind Kind => kind;
        public Transform ButtonPivot => buttonPivot;
        public float PressAmount => pressAmount;

        public void Configure(HeatingBaseController owner,
            HeatingBaseButtonKind buttonKind, Transform pivot, Vector3 inwardOffset)
        {
            if (owner == null || pivot == null || inwardOffset.sqrMagnitude < .000001f)
                throw new System.ArgumentException(
                    "Heating button requires a controller, pivot and press offset.");
            controller = owner;
            kind = buttonKind;
            buttonPivot = pivot;
            pressedOffset = inwardOffset;
        }

        protected override void Awake()
        {
            base.Awake();
            if (!Application.isPlaying) return;
            if (controller == null || buttonPivot == null)
                throw new System.InvalidOperationException(
                    "Heating button prefab bindings are incomplete.");
            releasedPosition = buttonPivot.localPosition;
            onClick.AddListener(Activate);
            initialized = true;
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && controller != null &&
                   controller.isActiveAndEnabled &&
                   controller.CanUse(kind) &&
                   (item == null || item.CanPlayerUse);
        }

        private void Activate()
        {
            if (!IsInteractable()) return;
            clickPulseRemaining = .12f;
            controller.PlayButtonSound();
            switch (kind)
            {
                case HeatingBaseButtonKind.Power:
                    controller.TogglePower();
                    break;
                case HeatingBaseButtonKind.Decrease:
                    controller.AdjustTemperature(-1);
                    break;
                case HeatingBaseButtonKind.Increase:
                    controller.AdjustTemperature(1);
                    break;
            }
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

        private void Update() => AdvanceAnimation(Time.unscaledDeltaTime);

        public void AdvanceAnimationForTesting(float deltaSeconds)
            => AdvanceAnimation(deltaSeconds);

        private void AdvanceAnimation(float deltaSeconds)
        {
            if (!initialized) return;
            var delta = Mathf.Clamp(deltaSeconds, 0, .05f);
            clickPulseRemaining = Mathf.Max(
                0, clickPulseRemaining - delta);
            var pressed = pointerPressed || clickPulseRemaining > 0;
            pressAmount = Mathf.MoveTowards(
                pressAmount, pressed ? 1f : 0f,
                delta / (pressed ? .045f : .11f));
            buttonPivot.localPosition =
                releasedPosition + pressedOffset * pressAmount;
        }

        protected override void OnDisable()
        {
            pointerPressed = false;
            pressAmount = 0;
            clickPulseRemaining = 0;
            if (initialized && buttonPivot != null)
                buttonPivot.localPosition = releasedPosition;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            onClick.RemoveListener(Activate);
            base.OnDestroy();
        }
    }
}
