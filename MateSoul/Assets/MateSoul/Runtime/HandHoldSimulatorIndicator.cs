using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class HandHoldSimulatorIndicator : MonoBehaviour
    {
        [SerializeField] private ControllerItemRay ray;
        [SerializeField] private Renderer marker;
        [SerializeField] private Color idleColor = Color.cyan;
        private MaterialPropertyBlock properties;
        private HandHoldInteractable[] sockets;
        private Color displayedColor = new Color(float.NaN, 0, 0, 0);

        public void Configure(ControllerItemRay source, Renderer target, Color idle)
        {
            ray = source;
            marker = target;
            idleColor = idle;
        }

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            sockets = FindObjectsOfType<HandHoldInteractable>(true);
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (ray == null || marker == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            var ready = false;
            var held = false;
            if (sockets != null)
            {
                foreach (var socket in sockets)
                {
                    if (socket == null || !socket.gameObject.activeInHierarchy) continue;
                    held |= socket.IsHeldBy(ray);
                    ready |= socket.IsWithinHoldRange(ray);
                }
            }

            var color = held
                ? new Color(1f, .72f, .08f, 1f)
                : ready ? new Color(.08f, .95f, .3f, 1f) : idleColor;
            if (color == displayedColor) return;
            displayedColor = color;
            marker.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            marker.SetPropertyBlock(properties);
        }
    }
}
