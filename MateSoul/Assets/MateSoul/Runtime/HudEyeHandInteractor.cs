using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MateSoul.Runtime
{
    /// <summary>Eye + pinch for HUD graphics only; never registered with the object interaction manager.</summary>
    public sealed class HudEyeHandInteractor : MonoBehaviour, IUIInteractor
    {
        public const string HudLayerName = "MateSoulHUD";
        private readonly List<Vector3> rayPoints = new List<Vector3>(2);
        private readonly List<Vector3> noRayPoints = new List<Vector3>(0);
        private readonly bool[] armed = new bool[2];
        private GameHud hud;
        private RectTransform canvasRoot;
        private PlayerInteractionModeController mode;
        private XRUIInputModule module;
        private int hudMask;
        private PicoTrackedHand pressingHand;
#region debug-point B-C:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextEyeTrace, debugNextEyePoseTrace;
        private bool debugEyeSelect;
#endif
#endregion

        public void Configure(GameHud owner, RectTransform root,
            PlayerInteractionModeController input, XRUIInputModule uiModule)
        {
            hud = owner;
            canvasRoot = root;
            mode = input;
            module = uiModule;
            var layer = LayerMask.NameToLayer(HudLayerName);
            if (layer < 0) throw new InvalidOperationException("MateSoulHUD layer is missing.");
            hudMask = 1 << layer;
            module.RegisterInteractor(this);
            Debug.Log("MATESOUL_INPUT hudEyePinch=true hudTouch=true worldDirectHandsOnly=true");
        }

        public void UpdateUIModel(ref TrackedDeviceModel model)
        {
            var valid = TryGetEyePose(out var eye);
            UpdateModel(ref model, eye, valid);
        }

        private void UpdateModel(ref TrackedDeviceModel model, Pose eye, bool eyeTracked)
        {
            var hands = mode.Hands;
            var available = isActiveAndEnabled && mode.DirectHandsEnabled && hud.IsVisible &&
                            canvasRoot.gameObject.activeInHierarchy && eyeTracked &&
                            !GameHud.IsTextInputFocused;
            var touchingHud = false;
            if (hands != null)
                foreach (var hand in hands)
                {
                    hand.Direct.RefreshProximity();
                    touchingHud |= hand.Poke != null && hand.Poke.IsTouching(canvasRoot);
                }
            available &= !touchingHud;
            // A canceled press must release over no graphic: XRI sends pointer-up, never click.
            var wasPressing = pressingHand != null;
            var canceled = pressingHand != null && (!available || !CanPinch(pressingHand));
            if (canceled || pressingHand != null && !pressingHand.NativePinching)
                pressingHand = null;
            if (hands != null)
                for (var i = 0; i < hands.Length; i++)
                {
                    var hand = hands[i];
                    var eligible = CanPinch(hand);
                    if (available && !wasPressing && pressingHand == null &&
                        eligible && armed[i] && hand.NativePinching)
                        pressingHand = hand;
                    // Match the SDK action used by the original HUD: native pinch release rearms.
                    armed[i] = available && eligible && !hand.NativePinching;
                }
            var cast = available && !canceled;
            // Detach before editing the reused points: XRI otherwise misses geometry changes
            // when the eye pose stays still, leaving stale hover/press state behind.
            model.raycastPoints = noRayPoints;
            rayPoints.Clear();
            if (cast)
            {
                rayPoints.Add(eye.position);
                rayPoints.Add(eye.position + eye.rotation * Vector3.forward * 3f);
                model.position = eye.position;
                model.orientation = eye.rotation;
            }
            model.raycastPoints = cast ? rayPoints : noRayPoints;
            model.raycastLayerMask = cast ? hudMask : 0;
            model.select = cast && pressingHand != null;
#region debug-point C:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (model.select != debugEyeSelect || Time.unscaledTime >= debugNextEyeTrace)
            {
                debugEyeSelect = model.select;
                debugNextEyeTrace = Time.unscaledTime + 1f;
                var left = hands != null && hands.Length > 0 ? hands[0] : null;
                var right = hands != null && hands.Length > 1 ? hands[1] : null;
                var leftPress = false;
                var rightPress = false;
                var leftTarget = "";
                var rightTarget = "";
                if (left?.Poke != null && left.Poke.TryGetUIModel(out var leftModel))
                {
                    leftPress = leftModel.select;
                    leftTarget = leftModel.currentRaycast.gameObject == null ? "" : leftModel.currentRaycast.gameObject.name;
                }
                if (right?.Poke != null && right.Poke.TryGetUIModel(out var rightModel))
                {
                    rightPress = rightModel.select;
                    rightTarget = rightModel.currentRaycast.gameObject == null ? "" : rightModel.currentRaycast.gameObject.name;
                }
                GameHud.ReportInputTrace($"{{\"sessionId\":\"eyehand-pointer-stall\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"HudEyeHandInteractor.UpdateModel\",\"msg\":\"[DEBUG] arbitration\"," +
                    $"\"data\":{{\"frame\":{Time.frameCount},\"eyeTracked\":{(eyeTracked ? 1 : 0)}," +
                    $"\"available\":{(available ? 1 : 0)},\"touchingHud\":{(touchingHud ? 1 : 0)}," +
                    $"\"canceled\":{(canceled ? 1 : 0)},\"eyeSelect\":{(model.select ? 1 : 0)}," +
                    $"\"owner\":{GameHud.InputTraceString(pressingHand == null ? "" : pressingHand.name)}," +
                    $"\"leftPokeSelect\":{(leftPress ? 1 : 0)},\"rightPokeSelect\":{(rightPress ? 1 : 0)}," +
                    $"\"leftPokeTarget\":{GameHud.InputTraceString(leftTarget)}," +
                    $"\"rightPokeTarget\":{GameHud.InputTraceString(rightTarget)}}}}}");
            }
#endif
#endregion
        }

        private static bool CanPinch(PicoTrackedHand hand)
            => hand.NativeTracked && !hand.Direct.hasSelection && !hand.Direct.HasNearbyTarget;

        private bool TryGetEyePose(out Pose pose)
        {
            pose = default;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            const uint required = (uint)(InputTrackingState.Position | InputTrackingState.Rotation);
#region debug-point B:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextEyePoseTrace)
            {
                debugNextEyePoseTrace = Time.unscaledTime + 1f;
                var hasStatus = device.TryGetFeatureValue(PXR_Usages.gazeStatus, out var debugStatus);
                var hasState = device.TryGetFeatureValue(PXR_Usages.gazeTrackingState, out var debugState);
                var nativeHmd = UnityEngine.InputSystem.InputSystem.GetDevice<ByteDance.PICO.XR.Input.PXR_HMD>();
                GameHud.ReportInputTrace($"{{\"sessionId\":\"eyehand-pointer-stall\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"HudEyeHandInteractor.TryGetEyePose\",\"msg\":\"[DEBUG] raw gaze validity\"," +
                    $"\"data\":{{\"frame\":{Time.frameCount},\"deviceValid\":{(device.isValid ? 1 : 0)}," +
                    $"\"hasStatus\":{(hasStatus ? 1 : 0)},\"status\":{(debugStatus ? 1 : 0)}," +
                    $"\"hasState\":{(hasState ? 1 : 0)},\"state\":{debugState}," +
                    $"\"nativeStatus\":{(nativeHmd != null && nativeHmd.eyeGazeIsTracked.isPressed ? 1 : 0)}," +
                    $"\"nativeState\":{(nativeHmd == null ? 0 : nativeHmd.eyeGazeTrackingState.ReadValue())}," +
                    $"\"focus\":{(Application.isFocused ? 1 : 0)}}}}}");
            }
#endif
#endregion
            if (!device.TryGetFeatureValue(PXR_Usages.gazeStatus, out var tracked) || !tracked ||
                !device.TryGetFeatureValue(PXR_Usages.gazeTrackingState, out var state) ||
                (state & required) != required ||
                !device.TryGetFeatureValue(PXR_Usages.gazePosition, out var position) ||
                !device.TryGetFeatureValue(PXR_Usages.gazeRotation, out var rotation))
                return false;
            var length = Quaternion.Dot(rotation, rotation);
            if (!PhysicsBody.IsFinite(position) || float.IsNaN(length) || float.IsInfinity(length) ||
                length < .5f || length > 1.5f) return false;
            var origin = mode.transform;
            pose = new Pose(origin.TransformPoint(position), origin.rotation * rotation.normalized);
            return true;
        }

        public bool TryGetUIModel(out TrackedDeviceModel model)
        {
            model = default;
            return module != null && module.GetTrackedDeviceModel(this, out model);
        }

        private void OnDisable()
        {
            pressingHand = null;
            Array.Clear(armed, 0, armed.Length);
            // Keep the model alive for one canceled release on the next UI pass.
        }

        private void OnDestroy()
        {
            if (module != null) module.UnregisterInteractor(this);
        }
    }
}
