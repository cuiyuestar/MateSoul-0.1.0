using System;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    public static class PicoDeviceEnvironment
    {
        private static bool? isEmulator;

        public static bool IsEmulator
        {
            get
            {
                if (!isEmulator.HasValue) isEmulator = ReadDeviceIdentity();
                return isEmulator.Value;
            }
        }

        private static bool ReadDeviceIdentity()
        {
            var model = SystemInfo.deviceModel;
            var product = string.Empty;
            var fingerprint = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var build = new AndroidJavaClass("android.os.Build"))
                {
                    model = build.GetStatic<string>("MODEL");
                    product = build.GetStatic<string>("PRODUCT");
                    fingerprint = build.GetStatic<string>("FINGERPRINT");
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    $"Could not read Android build identity; using Unity device identity: {error.Message}");
            }
#endif
            // Hardware names such as swan_arm64 can be shared with real headsets.
            var result = HasEmulatorMarker(model) || HasEmulatorMarker(product) ||
                         HasEmulatorMarker(fingerprint) ||
                         HasEmulatorMarker(SystemInfo.deviceModel) ||
                         HasEmulatorMarker(SystemInfo.deviceName);
            var kind = Application.isEditor ? "editor" : result ? "emulator" : "headset";
            Debug.Log($"MATESOUL_DEVICE kind={kind} model={model} product={product} " +
                      $"unityModel={SystemInfo.deviceModel} name={SystemInfo.deviceName} " +
                      $"fingerprint={fingerprint}");
            return result;
        }

        private static bool HasEmulatorMarker(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
