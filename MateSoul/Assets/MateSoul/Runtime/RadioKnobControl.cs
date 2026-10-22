using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public enum RadioKnobKind
    {
        Tuning,
        Volume
    }

    /// <summary>Maps a world-space pointer drag around a knob to its authored 270-degree range.</summary>
    public sealed class RadioKnobControl : Selectable, IPointerDownHandler, IDragHandler,
        IScrollHandler
    {
        [SerializeField] private RadioController radio;
        [SerializeField] private RadioKnobKind kind;
        [SerializeField] private Transform knobPivot;
        [SerializeField] private Transform linkedIndicator;
        [SerializeField] private Renderer minimumLabel;
        [SerializeField] private Renderer maximumLabel;
        [SerializeField] private Vector3 indicatorMinimumLocal;
        [SerializeField] private Vector3 indicatorMaximumLocal;
        [SerializeField, Range(0, 1)] private float value = .5f;
        [SerializeField] private float pointerDirection = 1f;
        private Quaternion restRotation;
        private MaterialPropertyBlock properties;
        private Renderer knobRenderer;
        private GameItem item;
        private float highlight;
        private float previousPointerAngle;
        private Vector2 volumePointerAxis;
        private float previousVolumePointerPosition;
        private float volumePointerSpan;
        private bool dragging;
        private bool initialized;

        public RadioKnobKind Kind => kind;
        public float Value => value;
        public float Highlight => highlight;
        public Transform KnobPivot => knobPivot;
        public Transform LinkedIndicator => linkedIndicator;
        public Vector3 IndicatorMinimumLocal => indicatorMinimumLocal;
        public Vector3 IndicatorMaximumLocal => indicatorMaximumLocal;

        public void Configure(RadioController owner, RadioKnobKind controlKind,
            Transform visual, Transform indicator, Vector3 indicatorMinimum,
            Vector3 indicatorMaximum, float startingValue, float increasingPointerDirection,
            Renderer lowLabel, Renderer highLabel)
        {
            if (Mathf.Abs(increasingPointerDirection) < .5f)
                throw new System.ArgumentException(
                    "Radio knob pointer direction must be positive or negative.");
            radio = owner;
            kind = controlKind;
            knobPivot = visual;
            linkedIndicator = indicator;
            minimumLabel = lowLabel;
            maximumLabel = highLabel;
            indicatorMinimumLocal = indicatorMinimum;
            indicatorMaximumLocal = indicatorMaximum;
            value = Mathf.Clamp01(startingValue);
            pointerDirection = Mathf.Sign(increasingPointerDirection);
        }

        protected override void Awake()
        {
            base.Awake();
            if (!Application.isPlaying) return;
            if (radio == null || knobPivot == null)
                throw new System.InvalidOperationException("Radio knob bindings are incomplete.");
            restRotation = knobPivot.localRotation;
            if (linkedIndicator != null &&
                Vector3.Distance(indicatorMinimumLocal, indicatorMaximumLocal) < .001f)
                throw new System.InvalidOperationException(
                    "Radio tuning indicator requires a finite movement range.");
            knobRenderer = knobPivot.GetComponentInChildren<Renderer>();
            properties = new MaterialPropertyBlock();
            initialized = true;
            ApplyVisual();
        }

        public override bool IsInteractable()
        {
            if (item == null) item = GetComponentInParent<GameItem>();
            return base.IsInteractable() && radio != null && radio.isActiveAndEnabled &&
                   (item == null || item.CanPlayerUse);
        }

        public void SetNormalizedValue(float next, bool notify = true)
        {
            value = Mathf.Clamp01(next);
            if (initialized) ApplyVisual();
            if (!notify || radio == null) return;
            if (kind == RadioKnobKind.Tuning) radio.SetTuning(value);
            else radio.SetVolume(value);
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            if (kind == RadioKnobKind.Volume)
            {
                if (!TryBeginVolumeDrag(eventData)) return;
            }
            else if (!TryGetPointerAngle(eventData, out previousPointerAngle)) return;
            dragging = true;
            eventData.Use();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !IsInteractable()) return;
            if (kind == RadioKnobKind.Volume)
            {
                var pointerPosition = GetVolumePointerPosition(eventData);
                var linearDelta =
                    (pointerPosition - previousVolumePointerPosition) / volumePointerSpan;
                previousVolumePointerPosition = pointerPosition;
                SetNormalizedValue(value + linearDelta);
                eventData.Use();
                return;
            }
            if (!TryGetPointerAngle(eventData, out var pointerAngle)) return;
            var angularDelta = Mathf.DeltaAngle(previousPointerAngle, pointerAngle);
            previousPointerAngle = pointerAngle;
            ApplyAngularDelta(angularDelta);
            eventData.Use();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!IsInteractable()) return;
            SetNormalizedValue(value + Mathf.Sign(eventData.scrollDelta.y) * .04f);
            eventData.Use();
        }

        private bool TryGetPointerAngle(PointerEventData eventData, out float angle)
        {
            var rect = (RectTransform)transform;
            var camera = EventCamera(eventData);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, camera, out var point) ||
                point.sqrMagnitude < 16f)
            {
                angle = 0;
                return false;
            }
            angle = Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg;
            return true;
        }

        private bool TryBeginVolumeDrag(PointerEventData eventData)
        {
            volumePointerSpan = 0;
            if (minimumLabel == null || maximumLabel == null) return false;
            var camera = EventCamera(eventData);
            if (camera == null) return false;
            var minimum = camera.WorldToScreenPoint(minimumLabel.bounds.center);
            var maximum = camera.WorldToScreenPoint(maximumLabel.bounds.center);
            var offset = (Vector2)(maximum - minimum);
            volumePointerSpan = offset.magnitude;
            if (minimum.z <= 0 || maximum.z <= 0 || volumePointerSpan < .001f)
                return false;
            volumePointerAxis = offset / volumePointerSpan;
            previousVolumePointerPosition = GetVolumePointerPosition(eventData);
            return true;
        }

        private float GetVolumePointerPosition(PointerEventData eventData)
            => Vector2.Dot(eventData.position, volumePointerAxis);

        private Camera EventCamera(PointerEventData eventData)
        {
            var eventCamera = eventData.pressEventCamera ?? eventData.enterEventCamera;
            if (eventCamera != null) return eventCamera;
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.worldCamera != null
                ? canvas.worldCamera
                : Camera.main;
        }

        private void ApplyAngularDelta(float degrees)
        {
            SetNormalizedValue(value + degrees * pointerDirection / 270f);
        }

        private void ApplyVisual()
        {
            // The FBX stores both knob pivots at their authored -135 degree minimum.
            var degrees = value * 270f;
            var parentAxis = knobPivot.parent.InverseTransformDirection(transform.forward).normalized;
            knobPivot.localRotation =
                Quaternion.AngleAxis(degrees, parentAxis) * restRotation;
            if (linkedIndicator != null)
                linkedIndicator.localPosition =
                    Vector3.Lerp(indicatorMinimumLocal, indicatorMaximumLocal, value);
        }


        private void Update()
        {
            if (!initialized) return;
            highlight = Mathf.MoveTowards(highlight,
                IsInteractable() && (IsHighlighted() || IsPressed()) ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            if (knobRenderer == null) return;
            properties.SetColor("_EmissionColor",
                new Color(.55f, .28f, .82f) * Mathf.Lerp(0, .25f, highlight));
            knobRenderer.SetPropertyBlock(properties);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            highlight = 0;
            dragging = false;
            volumePointerSpan = 0;
            if (initialized) ApplyVisual();
            if (knobRenderer != null) knobRenderer.SetPropertyBlock(null);
        }

#if UNITY_EDITOR
        public bool IsDraggingForTesting => dragging;
#endif
    }
}
