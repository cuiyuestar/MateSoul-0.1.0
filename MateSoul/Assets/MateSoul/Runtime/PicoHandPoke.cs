using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MateSoul.Runtime
{
    /// <summary>Fingertip taps, continuous control drags and captured rotary hand input.</summary>
    public sealed class PicoHandPoke : MonoBehaviour, IUIInteractor
    {
        private const float TouchDistance = .005f;
        private const float ReleaseDistance = .015f;
        private const float MaximumPenetration = .025f;
        private readonly List<Vector3> rayPoints = new List<Vector3>(2);
        private readonly List<Vector3> noRayPoints = new List<Vector3>(0);
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private PicoTrackedHand hand;
        private XRUIInputModule module;
        private EventSystem events;
        private TrackedDeviceEventData probe;
        private RectTransform pressedSurface;
        private GameObject pressedTarget;
        private Transform contact;
        private RadioKnobControl handKnob;
        private bool pressIsDrag;
        private bool handTurnArmed = true;
        private bool pressing, armed = true;
        public bool HasControlCapture => handKnob != null && handKnob.IsHandTurning ||
                                         pressing && pressIsDrag;

        public void Configure(PicoTrackedHand source) => hand = source;

        public void Bind(XRUIInputModule uiModule)
        {
            module = uiModule;
            events = module.GetComponent<EventSystem>();
            probe = new TrackedDeviceEventData(events);
            module.RegisterInteractor(this);
            Debug.Log($"MATESOUL_INPUT touchHand={hand.name} " +
                      $"distance={TouchDistance:F3} release={ReleaseDistance:F3} tapOnContact=true");
        }

        public bool IsTouching(Transform root)
            => contact != null && contact.IsChildOf(root);

        public void UpdateUIModel(ref TrackedDeviceModel model)
        {
            // Do not Reset the model: its previous select state is needed to deliver pointer-up.
            // Detach the reused list first, so XRI also processes moving canvases with a still hand.
            model.raycastPoints = noRayPoints;
            rayPoints.Clear();
            contact = null;
            if (UpdateHandTurn(model.pointerId))
            {
                pressing = armed = false;
                pressedSurface = null;
                pressedTarget = null;
                model.select = false;
                return;
            }
            if (GameHud.IsTextInputFocused || !isActiveAndEnabled ||
                !hand.TipTracked || hand.Direct.hasSelection ||
                (!pressing && !hand.Direct.CanPoke))
            {
                pressing = armed = false;
                pressedSurface = null;
                pressedTarget = null;
                model.select = false;
                return;
            }

            var tip = hand.Tip;
            var direction = tip.rotation * Vector3.forward;
            var origin = tip.position;
            var wasPressing = pressing;
            if (wasPressing && pressedSurface != null && pressedSurface.gameObject.activeInHierarchy)
            {
                // Buttons release over their original target. Drags keep the live fingertip
                // projected along the captured surface normal instead of jumping to its centre.
                direction = pressedSurface.forward;
                if (!pressIsDrag)
                    origin = pressedSurface.TransformPoint(pressedSurface.rect.center);
            }
            rayPoints.Add(origin - direction * .04f);
            rayPoints.Add(origin + direction * .03f);
            probe.Reset();
            probe.pointerId = model.pointerId;
            probe.position = new Vector2(float.MinValue, float.MinValue);
            probe.rayPoints = rayPoints;
            probe.layerMask = Physics.DefaultRaycastLayers;
            events.RaycastAll(probe, hits);
            var hit = hits.Count > 0 ? hits[0] : default;
            var target = hit.gameObject == null ? null :
                ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject);
            // A held pinch uses rotary ownership; rejection or cancellation must not
            // silently start a second fingertip drag on this or another knob.
            if (hand.NativePinching && target != null &&
                target.GetComponent<RadioKnobControl>() != null)
                target = null;
            var selectable = target == null ? null : target.GetComponent<Selectable>();
            if (selectable != null && (!selectable.IsActive() || !selectable.IsInteractable()))
                target = null;
            var distance = target == null ? float.PositiveInfinity :
                Vector3.Dot(tip.position - hit.worldPosition, hit.worldNormal);
            var touching = target != null && distance <= ReleaseDistance &&
                           distance >= -MaximumPenetration;
            if (wasPressing)
            {
                pressing = pressIsDrag && target == pressedTarget && touching;
                if (pressIsDrag && !pressing) rayPoints.Clear();
            }
            else if (armed && touching && distance <= TouchDistance)
            {
                pressing = true;
                pressedTarget = target;
                pressedSurface = target.GetComponent<RectTransform>();
                pressIsDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target) != null;
                armed = false;
            }
            if (target == null || distance > ReleaseDistance) armed = true;
            if (target != null && (target == pressedTarget || touching &&
                (pressing || distance <= TouchDistance)))
                contact = target.transform;
            model.position = tip.position;
            model.orientation = tip.rotation;
            model.raycastLayerMask = Physics.DefaultRaycastLayers;
            model.raycastPoints = rayPoints;
            model.select = pressing;
            if (!pressing)
            {
                pressedSurface = null;
                pressedTarget = null;
            }
        }

        private bool UpdateHandTurn(int pointerId)
        {
            var available = !GameHud.IsTextInputFocused && isActiveAndEnabled &&
                            hand.Tracked && !hand.Direct.hasSelection;
            if (!available)
            {
                EndHandTurn();
                handTurnArmed = false;
                return false;
            }
            if (!hand.NativePinching)
            {
                EndHandTurn();
                handTurnArmed = true;
                return false;
            }
            if (!hand.TryGetGraspPose(true, out var grasp) ||
                !hand.TryGetStableGripPose(out var frame))
            {
                EndHandTurn();
                handTurnArmed = false;
                return false;
            }
            if (handKnob == null && handTurnArmed)
            {
                var nearby = RadioKnobControl.FindNearby(grasp.position, pointerId);
                if (nearby != null && nearby.BeginHandTurn(this, frame.rotation))
                {
                    handKnob = nearby;
                    handTurnArmed = false;
                }
            }
            if (handKnob == null) return false;
            if (!handKnob.IsHandTurning || !handKnob.IsActive() || !handKnob.IsInteractable() ||
                Vector3.Distance(grasp.position, handKnob.KnobPivot.position) > .09f)
            {
                EndHandTurn();
                return false;
            }
            handKnob.UpdateHandTurn(this, frame.rotation);
            contact = handKnob.transform;
            return true;
        }

        private void EndHandTurn()
        {
            if (handKnob != null) handKnob.EndHandTurn(this);
            handKnob = null;
        }

        public bool TryGetUIModel(out TrackedDeviceModel model)
        {
            model = default;
            return module != null && module.GetTrackedDeviceModel(this, out model);
        }

        private void OnDisable()
        {
            EndHandTurn();
            handTurnArmed = false;
            pressing = armed = false;
            pressedSurface = null;
            pressedTarget = null;
            contact = null;
            // Stay registered while disabled so the next UI pass delivers a canceled release.
        }

        private void OnDestroy()
        {
            EndHandTurn();
            if (module != null) module.UnregisterInteractor(this);
        }
    }
}
