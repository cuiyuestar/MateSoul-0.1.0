using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    public sealed class SystemNotificationPresenter : MonoBehaviour
    {
        private const float HintHeight = 155f;
        private const float HintGap = 14f;
        private const float EnterOffset = 38f;
        private const float EnterDuration = .42f;
        private const float ReflowDuration = .36f;
        private const float ExitDuration = .34f;

        private sealed class HintView
        {
            public RectTransform Panel;
            public CanvasGroup Group;
            public Vector2 MoveFrom;
            public Vector2 MoveTo;
            public float MoveElapsed;
            public float MoveDuration;
            public float EnterElapsed;
            public float FadeFrom;
            public float FadeElapsed;
            public bool Entering;
            public bool Exiting;
        }

        private SystemNotifications notifications;
        private Canvas canvas;
        private RectTransform root, resultPanel;
        private TMP_Text result;
        private TMP_FontAsset font;
        private Material overlayMaterial;
        private Texture2D texture;
        private Sprite rounded;
        private readonly Dictionary<long, HintView> hintViews = new Dictionary<long, HintView>();
        private readonly HashSet<long> activeHintIds = new HashSet<long>();
        private readonly List<long> removalBuffer = new List<long>();

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

        private HintView CreateHintView(SystemNotifications.HintMessage message, Vector2 target)
        {
            var panel = Panel($"系统提示/{message.Id}", new Vector2(0, 1),
                target + Vector2.up * EnterOffset, new Vector2(620, HintHeight),
                out var label, 27);
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            label.text = message.Text;
            label.ForceMeshUpdate(true, true);
            AlwaysOnTopUi.Apply(panel, overlayMaterial);
            return new HintView
            {
                Panel = panel,
                Group = group,
                MoveFrom = panel.anchoredPosition,
                MoveTo = target,
                MoveDuration = EnterDuration,
                Entering = true
            };
        }

        private void Refresh()
        {
            activeHintIds.Clear();
            for (var index = 0; index < notifications.Hints.Count; index++)
            {
                var message = notifications.Hints[index];
                activeHintIds.Add(message.Id);
                var target = new Vector2(32, -32 - index * (HintHeight + HintGap));
                if (!hintViews.TryGetValue(message.Id, out var view))
                {
                    view = CreateHintView(message, target);
                    hintViews.Add(message.Id, view);
                }
                else
                {
                    MoveTo(view, target);
                }
            }

            foreach (var pair in hintViews)
            {
                var view = pair.Value;
                if (activeHintIds.Contains(pair.Key) || view.Exiting) continue;
                view.Exiting = true;
                view.FadeFrom = view.Group.alpha;
                view.FadeElapsed = 0;
            }

            result.text = notifications.Result ?? "";
            resultPanel.gameObject.SetActive(!string.IsNullOrEmpty(result.text));
            if (resultPanel.gameObject.activeSelf) result.ForceMeshUpdate(true, true);
        }

        private static void MoveTo(HintView view, Vector2 target)
        {
            if ((view.MoveTo - target).sqrMagnitude < .01f) return;
            view.MoveFrom = view.Panel.anchoredPosition;
            view.MoveTo = target;
            view.MoveElapsed = 0;
            view.MoveDuration = ReflowDuration;
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            removalBuffer.Clear();
            foreach (var pair in hintViews)
            {
                var view = pair.Value;
                if (view.Exiting)
                {
                    view.FadeElapsed += deltaTime;
                    var progress = Mathf.Clamp01(view.FadeElapsed / ExitDuration);
                    view.Group.alpha = Mathf.Lerp(view.FadeFrom, 0, SmoothStep(progress));
                    if (progress >= 1) removalBuffer.Add(pair.Key);
                    continue;
                }

                view.MoveElapsed += deltaTime;
                var moveProgress = Mathf.Clamp01(view.MoveElapsed / view.MoveDuration);
                view.Panel.anchoredPosition = Vector2.LerpUnclamped(
                    view.MoveFrom, view.MoveTo, EaseOutCubic(moveProgress));
                if (view.Entering)
                {
                    view.EnterElapsed += deltaTime;
                    var enterProgress = Mathf.Clamp01(view.EnterElapsed / EnterDuration);
                    view.Group.alpha = SmoothStep(enterProgress);
                    if (enterProgress >= 1) view.Entering = false;
                }
            }

            foreach (var id in removalBuffer)
            {
                var view = hintViews[id];
                hintViews.Remove(id);
                Destroy(view.Panel.gameObject);
            }
        }

        private static float SmoothStep(float value) => value * value * (3f - 2f * value);

        private static float EaseOutCubic(float value)
        {
            var inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
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
