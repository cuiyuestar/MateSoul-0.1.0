using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    /// <summary>XR UI power switch; grip remains reserved for moving the whole radio.</summary>
    public sealed class RadioPowerButton : Button
    {
        [SerializeField] private RadioController radio;
        [SerializeField] private Transform switchPivot;
        [SerializeField] private Vector3 onOffset;
        private readonly HashSet<int> hoveringPointers = new HashSet<int>();
        private MaterialPropertyBlock properties;
        private Renderer switchRenderer;
        private Vector3 offPosition;
        private float switchAmount;
        private float highlight;
        private GameItem item;
        private bool initialized;

        public float SwitchAmount => switchAmount;
        public float Highlight => highlight;
        public Transform SwitchPivot => switchPivot;
        public Vector3 OffPosition => offPosition;
        public Vector3 OnPosition => offPosition + onOffset;

        public void Configure(RadioController owner, Transform visual, Vector3 poweredOffset)
        {
            if (poweredOffset.sqrMagnitude < .000001f)
                throw new System.ArgumentException(
                    "Radio power switch requires a non-zero powered offset.");
            radio = owner;
            switchPivot = visual;
            onOffset = poweredOffset;
        }

        protected override void Awake()
        {
            base.Awake();
            if (!Application.isPlaying) return;
            if (radio == null || switchPivot == null)
                throw new System.InvalidOperationException("Radio power switch bindings are incomplete.");
            offPosition = switchPivot.localPosition;
            switchRenderer = switchPivot.GetComponentInChildren<Renderer>();
            properties = new MaterialPropertyBlock();
            onClick.AddListener(Toggle);
            initialized = true;
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && radio != null && radio.isActiveAndEnabled &&
                   (item == null || item.CanPlayerUse);
        }

        private void Toggle() { if (IsInteractable()) radio.TogglePower(); }

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

        private void Update()
        {
            if (!initialized) return;
            switchAmount = Mathf.MoveTowards(switchAmount,
                radio.IsPoweredOn ? 1f : 0f, Time.unscaledDeltaTime / .1f);
            highlight = Mathf.MoveTowards(highlight,
                IsInteractable() && hoveringPointers.Count > 0 ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            switchPivot.localPosition = offPosition + onOffset * switchAmount;
            if (switchRenderer == null) return;
            properties.SetColor("_EmissionColor",
                new Color(.95f, .3f, .5f) * Mathf.Lerp(0, .3f, highlight));
            switchRenderer.SetPropertyBlock(properties);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            hoveringPointers.Clear();
            switchAmount = highlight = 0;
            if (initialized && switchPivot != null) switchPivot.localPosition = offPosition;
            if (switchRenderer != null) switchRenderer.SetPropertyBlock(null);
        }

        protected override void OnDestroy()
        {
            onClick.RemoveListener(Toggle);
            base.OnDestroy();
        }
    }
}
