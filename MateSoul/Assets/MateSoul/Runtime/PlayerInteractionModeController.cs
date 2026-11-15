using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MateSoul.Runtime
{
    // Keep serialized enum values compatible with existing scenes and AI context.
    public enum PlayerInteractionMode { Controller, EyeHand, Gesture }

    /// <summary>World interactions use direct hands; HUD eye + pinch is owned by its UI interactor.</summary>
    public sealed class PlayerInteractionModeController : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionMode currentMode = PlayerInteractionMode.Gesture;
        [SerializeField] private GameObject[] controllerRayRoots;
        [SerializeField] private GameObject gazePinchRoot;
        [SerializeField] private GameObject[] handRayRoots;
        // 真机手部追踪会以 80ms 级间隔短暂丢失，放置期间保持上一帧有效姿态。
        private const float PlacementTrackingGrace = .25f;
        private PicoTrackedHand[] hands;
        private PicoTrackedHand placementHand;
        private Pose placementPose;
        private float placementPoseTime = -1f;
        private bool placementPoseHeld;
        private bool placementPosePressed;
        private bool placementTrackingFresh;
        private bool placementActive;
        public PlayerInteractionMode CurrentMode => currentMode;
        public bool Automatic => false;
        public bool DirectHandsEnabled => currentMode == PlayerInteractionMode.Gesture;
        public PicoTrackedHand[] Hands => hands;
        /// <summary>True when the placement hand was tracked on the current frame.</summary>
        public bool PlacementTrackingFresh => placementTrackingFresh;
        public event Action<PlayerInteractionMode> ModeChanged;
        public bool PlacementActive
        {
            get => placementActive;
            set
            {
                if (placementActive == value) return;
                placementActive = value;
                placementHand = null;
                placementPoseHeld = false;
                placementPosePressed = false;
                placementTrackingFresh = false;
            }
        }
        public Transform Gaze => gazePinchRoot == null ? null : gazePinchRoot.transform;
        public bool CanToggleHandMenu
        {
            get
            {
                if (placementActive || hands == null) return false;
                foreach (var hand in hands)
                    if (!hand.Tracked || hand.Direct.hasSelection || hand.Direct.HasNearbyTarget)
                        return false;
                return true;
            }
        }

        public void Configure(GameObject[] controllerRoots, GameObject gazeRoot,
            bool useAutomaticSwitching = false, GameObject[] handRoots = null)
        {
            controllerRayRoots = controllerRoots;
            gazePinchRoot = gazeRoot;
            handRayRoots = handRoots;
            Apply(Application.isEditor ? PlayerInteractionMode.Controller : PlayerInteractionMode.Gesture);
        }

        private void Awake()
        {
            Apply(Application.isEditor ? PlayerInteractionMode.Controller : PlayerInteractionMode.Gesture);
            InstallHands();
        }

        private void InstallHands()
        {
            if (!Application.isPlaying || hands != null) return;
            var manager = GetComponentInChildren<XRInteractionManager>(true);
            if (manager == null) throw new InvalidOperationException("XR Interaction Manager is missing.");
            hands = new PicoTrackedHand[2];
            for (var index = 0; index < hands.Length; index++)
            {
                var root = new GameObject(index == 0 ? "Left Direct Hand" : "Right Direct Hand");
                root.transform.SetParent(transform, false);
                hands[index] = root.AddComponent<PicoTrackedHand>();
                hands[index].Configure(index == 0, manager, this);
                root.SetActive(DirectHandsEnabled);
            }
            Debug.Log("MATESOUL_INPUT directHandsInstalled=true officialVisuals=true reach=0.09 physicalSegments=17");
        }

        public bool TryGetPlacementHand(out Pose pose, out bool pressed)
        {
            pose = default;
            pressed = false;
            placementTrackingFresh = false;
            if (!DirectHandsEnabled || hands == null) return false;
            // Lock the initiating hand for the whole preview: tracking loss never swaps hands.
            if (placementHand == null)
                for (var index = hands.Length - 1; index >= 0; index--)
                    if (hands[index].Tracked && !hands[index].Direct.hasSelection)
                    {
                        placementHand = hands[index];
                        break;
                    }
            if (placementHand == null) return false;
            if (placementHand.Tracked && !placementHand.Direct.hasSelection)
            {
                placementPose = placementHand.Palm;
                placementPosePressed = placementHand.Closed;
                placementPoseTime = Time.unscaledTime;
                placementPoseHeld = true;
                placementTrackingFresh = true;
                pose = placementPose;
                pressed = placementPosePressed;
                return true;
            }
            // 追踪短暂丢失时继续返回上一次有效姿态，避免预览与确认手势被逐帧清零；
            // 调用方通过 PlacementTrackingFresh 判断是否为当帧真实姿态。
            if (!placementPoseHeld ||
                Time.unscaledTime - placementPoseTime > PlacementTrackingGrace)
                return false;
            pose = placementPose;
            pressed = placementPosePressed;
            return true;
        }

        public bool TryPlacementInput(out Pose hand, out Ray aim, out bool pressed)
        {
            aim = default;
            if (DirectHandsEnabled)
            {
                if (!TryGetPlacementHand(out hand, out pressed)) return false;
                aim = new Ray(hand.position, Vector3.down);
                return true;
            }
            hand = default;
            pressed = false;
            if (controllerRayRoots == null) return false;
            for (var index = controllerRayRoots.Length - 1; index >= 0; index--)
            {
                var controller = controllerRayRoots[index].GetComponent<ActionBasedController>();
                var required = InputTrackingState.Position | InputTrackingState.Rotation;
                if (!controllerRayRoots[index].activeInHierarchy ||
                    (controller.currentControllerState.inputTrackingState & required) != required) continue;
                hand = new Pose(controller.transform.position, controller.transform.rotation);
                aim = new Ray(hand.position, hand.rotation * Vector3.forward);
                pressed = controller.uiPressInteractionState.active;
                return true;
            }
            return false;
        }

        public string PlacementHelp => DirectHandsEnabled
            ? "移动手掌选择正下方位置，先张手，再握紧并松开确认。"
            : "模拟器射线指向地面，按扳机键确认。";

        public void SetMode(PlayerInteractionMode mode)
            => Apply(Application.isEditor && mode == PlayerInteractionMode.Controller
                ? PlayerInteractionMode.Controller : PlayerInteractionMode.Gesture);

        public void ResumeAutomaticSwitching() => SetMode(PlayerInteractionMode.Gesture);

        private void Apply(PlayerInteractionMode mode)
        {
            var changed = currentMode != mode;
            currentMode = mode;
            SetRoots(controllerRayRoots, Application.isEditor && mode == PlayerInteractionMode.Controller);
            SetRoots(handRayRoots, false);
            if (gazePinchRoot != null) gazePinchRoot.SetActive(false);
            if (hands != null)
                foreach (var hand in hands) hand.gameObject.SetActive(DirectHandsEnabled);
            if (!changed) return;
            Debug.Log($"MATESOUL_INPUT mode={mode} directHands={DirectHandsEnabled}");
            ModeChanged?.Invoke(mode);
        }

        private static void SetRoots(GameObject[] roots, bool active)
        {
            if (roots == null) return;
            foreach (var root in roots)
                if (root != null) root.SetActive(active);
        }
    }
}
