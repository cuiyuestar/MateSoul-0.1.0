using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;

namespace MateSoul.RoomNavigation.Editor
{
    public static class RoomNavigationSceneBuilder
    {
        public static XROrigin CreateCameraRig()
        {
            var xrOriginObject = new GameObject("XR Origin (XR Rig)");
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginObject.transform, false);

            var xrCameraObject = new GameObject("XR Camera");
            xrCameraObject.transform.SetParent(cameraOffset.transform, false);
            var xrCamera = xrCameraObject.AddComponent<Camera>();
            xrCamera.nearClipPlane = 0.05f;
            xrCamera.allowHDR = false;
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            xrCameraObject.AddComponent<AudioListener>();

            var positionAction = new InputAction(
                "HMD Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
            var rotationAction = new InputAction(
                "HMD Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            var poseDriver = xrCameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(positionAction);
            poseDriver.rotationInput = new InputActionProperty(rotationAction);
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            var xrOrigin = xrOriginObject.AddComponent<XROrigin>();
            xrOrigin.Camera = xrCamera;
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var manager = xrOriginObject.AddComponent<PXR_Manager>();
            manager.openMRC = false;
            manager.eyeTracking = true;
            manager.sharpeningMode = SharpeningMode.Quality;
            manager.sharpeningEnhance = SharpeningEnhance.SelfAdaptive;
            manager.useRecommendedAntiAliasingLevel = true;

            var editorCameraObject = new GameObject("Editor Overview Camera");
            editorCameraObject.transform.position = new Vector3(-0.6f, 6.5f, -5.5f);
            editorCameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(-0.6f, 0.5f, 2.4f) - editorCameraObject.transform.position,
                Vector3.up);
            var editorCamera = editorCameraObject.AddComponent<Camera>();
            editorCamera.fieldOfView = 52f;
            editorCamera.nearClipPlane = 0.05f;
            editorCamera.clearFlags = CameraClearFlags.SolidColor;
            editorCamera.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 1f);
            editorCameraObject.AddComponent<AudioListener>();

            var selector = xrOriginObject.AddComponent<RoomNavigationCameraSelector>();
            selector.Configure(xrCameraObject, editorCameraObject);
            return xrOrigin;
        }

        public static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.17f, 0.2f);
        }
    }
}
