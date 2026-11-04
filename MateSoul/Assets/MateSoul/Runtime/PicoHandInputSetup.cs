using UnityEngine;
using UnityEngine.InputSystem;

namespace MateSoul.Runtime
{
    internal static class PicoHandInputSetup
    {
        // The SDK queues bool deltas (0/1) into BYTE axes normalized to 0..1.
        // Restore the boolean range without changing the SDK's one-byte event format.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            InputSystem.RegisterLayoutOverride(
                @"{
                    ""name"": ""MateSoulPicoHandBooleanControls"",
                    ""extend"": ""PicoHandInteraction"",
                    ""controls"": [
                        { ""name"": ""isTracked"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""pinchTouched"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""pinchReady"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""pointerActivated"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""pointerActivateReady"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""graspFirm"", ""processors"": ""scale(factor=255)"" },
                        { ""name"": ""graspReady"", ""processors"": ""scale(factor=255)"" }
                    ]
                }");
        }
    }
}
