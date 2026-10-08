using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public sealed class SystemNotificationPresenter : MonoBehaviour
    {
        private SystemNotifications notifications;
        private Canvas canvas;
        private RectTransform root, hintPanel, resultPanel;
        private TMP_Text hint, result;
        private TMP_FontAsset font;
        private Material overlayMaterial;
        private Texture2D texture;
        private Sprite rounded;

        public void Bind(SystemNotifications source)
        {
            notifications = source;
            font = AlwaysOnTopUi.TextFont;
            overlayMaterial = AlwaysOnTopUi.CreateMaterial();
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[64 * 64];
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var dx = x - Mathf.Clamp(x, 14, 49);
                    var dy = y - Mathf.Clamp(y, 14, 49);
                    pixels[y * 64 + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(14.5f - Mathf.Sqrt(dx * dx + dy * dy)) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            rounded = Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * .5f,
                100, 0, SpriteMeshType.FullRect, Vector4.one * 16);
            root = new GameObject("System Notifications", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(transform, false);
            canvas = root.gameObject.AddComponent<Canvas>();
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            AlwaysOnTopUi.ConfigureScaler(scaler);
            if (Application.isEditor)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace;
                root.sizeDelta = new Vector2(1600, 900);
                root.localScale = Vector3.one * .001f;
            }
            AlwaysOnTopUi.Configure(canvas);
            hintPanel = Panel("左上角系统提示", new Vector2(0, 1), new Vector2(32, -32),
                new Vector2(620, 155), out hint, 27);
            resultPanel = Panel("回合结果", new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(850, 250), out result, 46);
            AlwaysOnTopUi.Apply(root, overlayMaterial);
            notifications.Changed += Refresh;
            Refresh();
            Follow();
        }

        private RectTransform Panel(string title, Vector2 anchor, Vector2 offset,
            Vector2 size, out TMP_Text label, int textSize)
        {
            var panel = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
            panel.anchoredPosition = offset;
            panel.sizeDelta = size;
            var background = panel.gameObject.AddComponent<Image>();
            background.sprite = rounded;
            background.type = Image.Type.Sliced;
            background.color = new Color(.65f, .51f, .79f, .96f);
            background.raycastTarget = false;
            var textRoot = new GameObject("Message", typeof(RectTransform)).GetComponent<RectTransform>();
            textRoot.SetParent(panel, false);
            textRoot.anchorMin = Vector2.zero;
            textRoot.anchorMax = Vector2.one;
            textRoot.offsetMin = new Vector2(26, 18);
            textRoot.offsetMax = new Vector2(-26, -18);
            label = AlwaysOnTopUi.ConfigureText(textRoot.gameObject.AddComponent<TextMeshProUGUI>());
            label.fontStyle = FontStyles.Bold;
            label.fontSize = textSize;
            label.color = Color.white;
            label.alignment = anchor.x == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return panel;
        }

        private void Refresh()
        {
            hint.text = notifications.Hint ?? "";
            result.text = notifications.Result ?? "";
            hintPanel.gameObject.SetActive(!string.IsNullOrEmpty(hint.text));
            resultPanel.gameObject.SetActive(!string.IsNullOrEmpty(result.text));
            if (hintPanel.gameObject.activeSelf) hint.ForceMeshUpdate(true, true);
            if (resultPanel.gameObject.activeSelf) result.ForceMeshUpdate(true, true);
        }

        private void LateUpdate() => Follow();
        private void Follow()
        {
            if (canvas == null || root == null || Application.isEditor) return;
            var camera = Camera.main;
            if (camera == null) return;
            // Separate from the bottom dock: notification coordinates cover the central view.
            root.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 1.5f,
                camera.transform.rotation);
            canvas.worldCamera = camera;
        }

        private void OnDestroy()
        {
            if (notifications != null) notifications.Changed -= Refresh;
            if (overlayMaterial != null) Destroy(overlayMaterial);
            if (rounded != null) Destroy(rounded);
            if (texture != null) Destroy(texture);
        }
    }
}
