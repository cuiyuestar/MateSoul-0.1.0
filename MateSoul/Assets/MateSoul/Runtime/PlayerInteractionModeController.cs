using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    public enum PlayerInteractionMode { Controller, EyeHand, Gesture }

    /// <summary>Keeps controller-ray and gaze-pinch input mutually exclusive.</summary>
    public sealed class PlayerInteractionModeController : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionMode currentMode = PlayerInteractionMode.Controller;
        [SerializeField] private bool automatic = true;
        [SerializeField, Min(0)] private float controllerLossDelay = .75f;
        [SerializeField] private GameObject[] controllerRayRoots;
        [SerializeField] private GameObject gazePinchRoot;
        [SerializeField] private GameObject[] handRayRoots;
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private float controllersMissingSince = -1;
        public PlayerInteractionMode CurrentMode => currentMode;
        public bool Automatic => automatic;
        public event Action<PlayerInteractionMode> ModeChanged;
        public bool PlacementActive { get; set; }
        public Transform Gaze => gazePinchRoot == null ? null : gazePinchRoot.transform;

        public void Configure(GameObject[] controllerRoots, GameObject gazeRoot,
            bool useAutomaticSwitching = true, GameObject[] handRoots = null)
        {
            controllerRayRoots = controllerRoots;
            gazePinchRoot = gazeRoot;
            handRayRoots = handRoots;
            automatic = useAutomaticSwitching;
            Apply(currentMode, false);
        }

        private void Awake() => Apply(currentMode, false);

        private void Update()
        {
            if (!automatic) return;
            if (IsControllerTracked())
            {
                controllersMissingSince = -1;
                Apply(PlayerInteractionMode.Controller, true);
                return;
            }
            if (controllersMissingSince < 0) controllersMissingSince = Time.unscaledTime;
            if (Time.unscaledTime - controllersMissingSince >= controllerLossDelay)
            {
                var handsTracked = IsTracked(InputDeviceCharacteristics.HandTracking);
                var next = handsTracked
                    ? IsTracked(InputDeviceCharacteristics.EyeTracking)
                        ? PlayerInteractionMode.EyeHand
                        : PlayerInteractionMode.Gesture
                    : PlayerInteractionMode.Controller;
                Apply(next, true);
            }
        }

        private bool GazeTracked()
        {
            var controller = gazePinchRoot == null ? null : gazePinchRoot.GetComponent<ActionBasedController>();
            return controller != null && controller.isTrackedAction.action != null &&
                   controller.isTrackedAction.action.ReadValue<float>() > .5f;
        }

        public bool TryPlacementInput(out Pose hand, out Ray aim, out bool pressed)
        {
            hand = default; aim = default; pressed = false;
            var roots = currentMode == PlayerInteractionMode.Controller ? controllerRayRoots : handRayRoots;
            if (roots == null) return false;
            // Right hand is the default; a lone tracked left hand works identically.
            for (var index = roots.Length - 1; index >= 0; index--)
            {
                var controller = roots[index].GetComponent<ActionBasedController>();
                var tracking = controller.currentControllerState.inputTrackingState;
                if (!roots[index].activeInHierarchy || (tracking & InputTrackingState.Position) == 0 ||
                    (tracking & InputTrackingState.Rotation) == 0) continue;
                hand = new Pose(controller.transform.position, controller.transform.rotation);
                var origin = currentMode == PlayerInteractionMode.EyeHand ? Gaze : controller.transform;
                if (currentMode == PlayerInteractionMode.EyeHand && !GazeTracked()) return false;
                aim = new Ray(origin.position, origin.forward);
                pressed = currentMode == PlayerInteractionMode.Controller
                    ? controller.uiPressInteractionState.active : controller.selectInteractionState.active;
                return true;
            }
            return false;
        }

        public string PlacementHelp => currentMode == PlayerInteractionMode.Controller
            ? "手柄射线指向地面，按扳机键确认。"
            : currentMode == PlayerInteractionMode.EyeHand
                ? "注视地面目标，食指与拇指捏合后松开确认。"
                : "手部射线指向地面，食指与拇指捏合后松开确认。";

        public void SetMode(PlayerInteractionMode mode)
        {
            automatic = false;
            controllersMissingSince = -1;
            Apply(mode, true);
        }

        public void ResumeAutomaticSwitching()
        {
            automatic = true;
            controllersMissingSince = -1;
        }

        private bool IsControllerTracked()
        {
            return IsTracked(InputDeviceCharacteristics.Controller |
                             InputDeviceCharacteristics.HeldInHand);
        }

        private bool IsTracked(InputDeviceCharacteristics characteristics)
        {
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
            foreach (var device in devices)
            {
                if (device.isValid &&
                    device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && tracked)
                    return true;
            }
            return false;
        }

        private void Apply(PlayerInteractionMode mode, bool notify)
        {
            var changed = currentMode != mode;
            currentMode = mode;
            var controllersEnabled = mode == PlayerInteractionMode.Controller;
            if (controllerRayRoots != null)
                foreach (var root in controllerRayRoots)
                    if (root != null && root.activeSelf != controllersEnabled)
                        root.SetActive(controllersEnabled);
            var gazeEnabled = mode == PlayerInteractionMode.EyeHand;
            if (gazePinchRoot != null && gazePinchRoot.activeSelf != gazeEnabled)
                gazePinchRoot.SetActive(gazeEnabled);
            if (handRayRoots != null)
                foreach (var root in handRayRoots)
                    if (root != null && root.activeSelf == controllersEnabled)
                        root.SetActive(!controllersEnabled);
            if (changed && notify) ModeChanged?.Invoke(mode);
        }
    }
}
