using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using MateSoul.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class PlayerAIHudTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject host;

        private static void Invoke(GameHud hud, string name, params object[] values)
            => typeof(GameHud).GetMethod(name, Private).Invoke(hud, values);
        private static T Field<T>(GameHud hud, string name)
            => (T)typeof(GameHud).GetField(name, Private).GetValue(hud);
        private static void Page(GameHud hud, string name)
        {
            var type = typeof(GameHud).GetNestedType("HudPage", BindingFlags.NonPublic);
            Invoke(hud, "SetActivePage", Enum.Parse(type, name));
        }

        [UnityTest]
        public IEnumerator MissingKeyConfigurationTutorialAndPlacementPreserveHudState()
        {
            yield return new EnterPlayMode();
            host = new GameObject("AI HUD acceptance");
            var bootstrap = host.AddComponent<GameBootstrap>();
            bootstrap.enabled = false;
            bootstrap.Configure(null, null, Array.Empty<ItemDefinition>());
            typeof(GameBootstrap).GetField("aiConfiguration", Private)
                .SetValue(bootstrap, new PlayerAIConfiguration());
            var hud = host.AddComponent<GameHud>();
            hud.enabled = false;
            hud.Configure(bootstrap);
            Invoke(hud, "Start");
            Invoke(hud, "SetDialogueOpen", true);
            yield return new WaitForSecondsRealtime(.75f);

            var hint = Field<RectTransform>(hud, "aiRequiredHint");
            Assert.That(hint.gameObject.activeInHierarchy, Is.True);
            Assert.That(hint.GetComponentsInChildren<TMP_Text>().Any(
                text => text.text == "先配置ai才能激活对话功能"), Is.True);
            Assert.That(Field<Button>(hud, "sendButton").interactable, Is.False);
            var dialogueInput = Field<TMP_InputField>(hud, "input");
            dialogueInput.text = "保留这段草稿";
            var configure = hint.GetComponentInChildren<Button>();
            Assert.That(configure.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("点此配置ai"));
            configure.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(Field<RectTransform>(hud, "contentRoot")
                .GetComponentsInChildren<TMP_Text>().Any(text => text.text == "AI配置"), Is.True);
            Assert.That(Field<TMP_InputField>(hud, "aiKey").contentType,
                Is.EqualTo(TMP_InputField.ContentType.Password));
            var tutorial = host.GetComponentsInChildren<Button>(true).Single(
                button => button.GetComponentInChildren<TMP_Text>(true).text == "如何配置AI");
            tutorial.onClick.Invoke();
            Assert.That(Field<RectTransform>(hud, "contentRoot")
                .GetComponentsInChildren<TMP_Text>().Any(
                    text => text.text.Contains("platform.deepseek.com")), Is.True);
            Page(hud, "AIConfiguration");
            Field<TMP_InputField>(hud, "aiAddress").text = "https://api.deepseek.com";
            Field<TMP_InputField>(hud, "aiModel").text = "deepseek-v4-pro";
            Field<TMP_InputField>(hud, "aiKey").text = "test-player-key";
            Field<Button>(hud, "aiSaveButton").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(bootstrap.AIConfiguration.IsConfigured, Is.True);
            Assert.That(bootstrap.AIConfiguration.endpoint,
                Is.EqualTo("https://api.deepseek.com/chat/completions"));
            Assert.That(hud.IsDialogueOpen, Is.True);
            Assert.That(hint.gameObject.activeSelf, Is.False);
            Assert.That(dialogueInput.text, Is.EqualTo("保留这段草稿"));

            Invoke(hud, "SetDialogueOpen", false);
            yield return new WaitForSecondsRealtime(.7f);
            Page(hud, "Settings");
            var settingsButton = host.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "设置/AI配置");
            settingsButton.onClick.Invoke();
            Assert.That(Field<TMP_InputField>(hud, "aiKey").text, Is.EqualTo("test-player-key"));
            Page(hud, "Items");
            var content = Field<RectTransform>(hud, "contentRoot");
            var preview = bootstrap.GetComponent<ItemPlacementPreviewController>();
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/WaterDispenser.asset");
            Assert.That(item.PlacementPreview, Is.True);
            preview.Begin(item);
            Assert.That(hud.IsVisible, Is.False);
            Assert.That(Field<RectTransform>(hud, "hudRoot").gameObject.activeSelf, Is.False);
            Assert.That(content.gameObject.activeSelf, Is.True);
            preview.Cancel();
            Assert.That(hud.IsVisible, Is.True);
            Assert.That(content.GetComponentsInChildren<TMP_Text>().Any(
                text => text.text.Contains("家具")), Is.True);
            Assert.That(dialogueInput.text, Is.EqualTo("保留这段草稿"));
            Invoke(hud, "SetHudVisible", false);
            preview.Begin(item);
            preview.Cancel();
            Assert.That(hud.IsVisible, Is.False, "Placement must preserve manual visibility.");
        }

        [TestCase("http://gateway.example")]
        [TestCase("https://user:secret@gateway.example")]
        [TestCase("https://gateway.example?key=secret")]
        [TestCase("https://gateway.example/v1/conversations/reply")]
        public void ConfigurationRejectsUnsafeOrLegacyEndpoints(string address)
            => Assert.Throws<ArgumentException>(() =>
                PlayerAIConfiguration.Validate(address, "deepseek-v4-pro", "test-key", false));

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (host != null) Object.Destroy(host);
            PlayerPrefs.DeleteKey("matesoul.ai.connection");
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
