using System.Collections.Generic;
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

    /// <summary>Owns pointer dragging and captured hand rotation over the authored knob range.</summary>
    public sealed class RadioKnobControl : Selectable, IPointerDownHandler, IDragHandler,
        IScrollHandler, IInitializePotentialDragHandler
    {
        private const float HandCaptureDistance = .045f;
        private const float HandRotationGain = 1.5f;
        private static readonly List<RadioKnobControl> activeControls =
            new List<RadioKnobControl>();
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
        private PicoHandPoke handOwner;
        private Quaternion previousHandRotation;
        private int dragPointerId;
        private bool pointerAngleValid;
        private bool dragging;
        private bool initialized;

        public RadioKnobKind Kind => kind;
        public float Value => value;
        public float Highlight => highlight;
        public Transform KnobPivot => knobPivot;
        public Transform LinkedIndicator => linkedIndicator;
        public Vector3 IndicatorMinimumLocal => indicatorMinimumLocal;
        public Vector3 IndicatorMaximumLocal => indicatorMaximumLocal;
        internal bool IsHandTurning => handOwner != null;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!activeControls.Contains(this)) activeControls.Add(this);
        }

        internal static RadioKnobControl FindNearby(Vector3 position, int pointerId)
        {
            RadioKnobControl nearest = null;
            var bestDistance = HandCaptureDistance * HandCaptureDistance;
            foreach (var control in activeControls)
            {
                if (!control.gameObject.scene.IsValid() || !control.IsActive() ||
                    !control.IsInteractable() || control.handOwner != null ||
                    control.dragging && control.dragPointerId != pointerId)
                    continue;
                var offset = position - control.knobPivot.position;
                var distance = offset.sqrMagnitude;
                if (distance > bestDistance ||
                    Vector3.Dot(offset, control.transform.forward) < -.01f)
                    continue;
                if (Physics.Linecast(position, control.knobPivot.position, out var hit,
                        InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore) &&
                    !hit.collider.transform.IsChildOf(control.radio.transform))
                    continue;
                nearest = control;
                bestDistance = distance;
            }
            return nearest;
        }

        internal bool BeginHandTurn(PicoHandPoke owner, Quaternion rotation)
        {
            if (handOwner != null || !IsActive() || !IsInteractable()) return false;
            handOwner = owner;
            previousHandRotation = rotation;
            dragging = false;
            return true;
        }

        internal void UpdateHandTurn(PicoHandPoke owner, Quaternion rotation)
        {
            if (handOwner != owner) return;
            var turn = rotation * Quaternion.Inverse(previousHandRotation);
            previousHandRotation = rotation;
            // Extract twist about the physical shaft; wrist swing does not turn the knob.
            var axis = transform.forward;
            var projected = turn.x * axis.x + turn.y * axis.y + turn.z * axis.z;
            if (Mathf.Abs(projected) + Mathf.Abs(turn.w) < .0001f) return;
            var degrees = Mathf.DeltaAngle(0,
                2f * Mathf.Atan2(projected, turn.w) * Mathf.Rad2Deg);
            SetNormalizedValue(value + degrees * HandRotationGain / 270f);
        }

        internal void EndHandTurn(PicoHandPoke owner)
        {
            if (handOwner == owner) handOwner = null;
        }

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
            if (handOwner != null || dragging && dragPointerId != eventData.pointerId) return;
            base.OnPointerDown(eventData);
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            if (kind == RadioKnobKind.Volume)
            {
                if (!TryBeginVolumeDrag(eventData)) return;
            }
            else pointerAngleValid = TryGetPointerAngle(eventData, out previousPointerAngle);
            dragPointerId = eventData.pointerId;
            dragging = true;
#region debug-point D:radio-knob
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            ReportMemoryInteraction("begin");
#endif
#endregion
            eventData.Use();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            if (dragging && dragPointerId != eventData.pointerId) return;
            base.OnPointerUp(eventData);
#region debug-point D:radio-knob
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (dragging) ReportMemoryInteraction("end");
#endif
#endregion
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || dragPointerId != eventData.pointerId || !IsInteractable()) return;
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
            if (!TryGetPointerAngle(eventData, out var pointerAngle))
            {
                pointerAngleValid = false;
                return;
            }
            if (!pointerAngleValid)
            {
                previousPointerAngle = pointerAngle;
                pointerAngleValid = true;
            }
            var angularDelta = Mathf.DeltaAngle(previousPointerAngle, pointerAngle);
            previousPointerAngle = pointerAngle;
            ApplyAngularDelta(angularDelta);
            eventData.Use();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData is UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceEventData tracked &&
                tracked.interactor is PicoHandPoke)
                eventData.useDragThreshold = false;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (handOwner != null || dragging || !IsInteractable()) return;
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

#region debug-point D:radio-knob
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private void ReportMemoryInteraction(string state)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var payload =
                $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                $"\"hypothesisId\":\"D\",\"ts\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"RadioKnobControl\",\"msg\":\"[DEBUG] radio knob interaction\"," +
                $"\"data\":{{\"state\":\"{state}\",\"kind\":\"{kind}\"," +
                $"\"value\":{value.ToString("R", culture)}}}}}";
            var request = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7791/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
        }
#endif
#endregion

        private void Update()
        {
            if (!initialized) return;
            highlight = Mathf.MoveTowards(highlight,
                IsInteractable() && (IsHighlighted() || IsPressed() || IsHandTurning) ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            if (knobRenderer == null) return;
            properties.SetColor("_EmissionColor",
                new Color(.55f, .28f, .82f) * Mathf.Lerp(0, .25f, highlight));
            knobRenderer.SetPropertyBlock(properties);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            activeControls.Remove(this);
            handOwner = null;
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
