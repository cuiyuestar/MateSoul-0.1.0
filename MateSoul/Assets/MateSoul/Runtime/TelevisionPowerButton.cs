using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    /// <summary>Uses the existing mouse/XR UI press channel; grip still moves the TV.</summary>
    public sealed class TelevisionPowerButton : Button
    {
        [SerializeField] private TelevisionController television;
        [SerializeField] private Transform buttonVisual;
        [SerializeField] private Renderer buttonRenderer;
        [SerializeField] private Vector3 pressedOffset;
        private readonly HashSet<int> hoveringPointers = new HashSet<int>();
        private readonly Dictionary<int, float> physicalPresses = new Dictionary<int, float>();
        private readonly HashSet<int> latchedPhysicalPresses = new HashSet<int>();
        private MaterialPropertyBlock properties;
        private Vector3 restPosition;
        private GameItem item;
        private float pressAmount, highlight, pulseUntil;
        private int? pressingPointer;
        private bool initialized;
        public float PressAmount => pressAmount;
        public float Highlight => highlight;

        public void Configure(TelevisionController owner, Transform visual, Vector3 offset)
        {
            television = owner;
            buttonVisual = visual;
            buttonRenderer = visual.GetComponent<Renderer>();
            pressedOffset = offset;
        }

        protected override void Awake()
        {
            base.Awake();
            // Selectable executes in Edit Mode; prefab construction must not move its mesh.
            if (!Application.isPlaying) return;
            properties = new MaterialPropertyBlock();
            restPosition = buttonVisual.localPosition;
            onClick.AddListener(Click);
            initialized = true;
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && television != null && television.isActiveAndEnabled &&
                   (item == null || !item.IsHeld);
        }

        private void Click()
        {
            pulseUntil = Time.unscaledTime + .08f;
            television.TogglePower();
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            hoveringPointers.Add(eventData.pointerId);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            hoveringPointers.Remove(eventData.pointerId);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable())
                pressingPointer = eventData.pointerId;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (pressingPointer == eventData.pointerId) pressingPointer = null;
        }

        public void SetPhysicalPress(int sourceId, float amount)
        {
            if (!initialized || !IsInteractable()) return;
            amount = Mathf.Clamp01(amount);
            if (amount >= .4f && latchedPhysicalPresses.Add(sourceId))
                onClick.Invoke();
            else if (amount <= .2f)
                latchedPhysicalPresses.Remove(sourceId);
            physicalPresses[sourceId] = latchedPhysicalPresses.Contains(sourceId) ? 1f : amount;
        }

        public void EndPhysicalPress(int sourceId)
        {
            physicalPresses.Remove(sourceId);
            latchedPhysicalPresses.Remove(sourceId);
        }

        private void Update()
        {
            if (!initialized) return;
            var available = IsInteractable();
            var physicalAmount = 0f;
            foreach (var amount in physicalPresses.Values)
                physicalAmount = Mathf.Max(physicalAmount, amount);
            var pointerPressed = pressingPointer.HasValue &&
                                 hoveringPointers.Contains(pressingPointer.Value);
            var targetPress = available
                ? Mathf.Max(pointerPressed || Time.unscaledTime < pulseUntil ? 1 : 0, physicalAmount)
                : 0;
            pressAmount = Mathf.MoveTowards(pressAmount, targetPress,
                Time.unscaledDeltaTime / (targetPress > pressAmount ? .055f : .12f));
            highlight = Mathf.MoveTowards(highlight,
                available && (hoveringPointers.Count > 0 || physicalPresses.Count > 0) ? 1 : 0,
                Time.unscaledDeltaTime / .12f);
            buttonVisual.localPosition = restPosition + pressedOffset * pressAmount;
            properties.SetColor("_EmissionColor",
                new Color(.8f, .36f, .54f) * Mathf.Lerp(.06f, .85f, highlight));
            buttonRenderer.SetPropertyBlock(properties);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!initialized) return;
            hoveringPointers.Clear();
            physicalPresses.Clear();
            latchedPhysicalPresses.Clear();
            pressingPointer = null;
            pressAmount = highlight = pulseUntil = 0;
            if (buttonVisual != null) buttonVisual.localPosition = restPosition;
            if (buttonRenderer != null) buttonRenderer.SetPropertyBlock(null);
        }

        protected override void OnDestroy()
        {
            onClick.RemoveListener(Click);
            base.OnDestroy();
        }
    }
}
