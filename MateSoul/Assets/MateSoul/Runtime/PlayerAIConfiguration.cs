using System;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    [Serializable]
    public sealed class PlayerAIConfiguration
    {
        public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";
        public int version;
        public string endpoint = DefaultEndpoint;
        public string model = "deepseek-v4-pro";
        public string apiKey = "";
        private const string PreferencesKey = "matesoul.ai.connection";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey) &&
                                    !string.IsNullOrWhiteSpace(endpoint);

        public static PlayerAIConfiguration Load(ModelConnectionSettings defaults)
        {
            var result = new PlayerAIConfiguration();
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var storage = new AndroidJavaClass("com.matesoul.player.AISecretStore"))
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    var json = storage.CallStatic<string>("load", activity);
                    if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, result);
                }
#else
                var json = PlayerPrefs.GetString(PreferencesKey, "");
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, result);
#endif
            }
            catch (Exception)
            {
                // An unreadable encrypted record must require fresh credentials.
                Debug.LogWarning("AI 配置无法读取，请重新配置 AI。");
                result = new PlayerAIConfiguration();
            }
            result.Migrate(defaults?.Endpoint);
            return result;
        }

        public void Migrate(string defaultAddress = null)
        {
            if (version < 2 || string.IsNullOrWhiteSpace(endpoint))
                endpoint = string.IsNullOrWhiteSpace(defaultAddress) ? DefaultEndpoint : defaultAddress;
            version = 2;
        }

        public static PlayerAIConfiguration Validate(string address, string modelName,
            string key, bool allowLocalHttp = false)
        {
            address = (address ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !(uri.Scheme == Uri.UriSchemeHttps || allowLocalHttp &&
                    (Application.isEditor || Debug.isDebugBuild) &&
                    uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                throw new ArgumentException("请填写供应商的 HTTPS API 地址。");
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path == "" || path == "/v1") path += "/chat/completions";
            else if (!path.EndsWith("/chat/completions", StringComparison.Ordinal))
                throw new ArgumentException("地址应为供应商 API 根地址或 /chat/completions 接口。");
            modelName = (modelName ?? "").Trim();
            key = (key ?? "").Trim();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("请填写 API Key。");
            if (string.IsNullOrEmpty(modelName) || modelName.Length > 100)
                throw new ArgumentException("请填写有效的模型名称。");
            ValidateHeader(key, "API Key");
            ValidateHeader(modelName, "模型名称");
            var builder = new UriBuilder(uri) { Path = path };
            return new PlayerAIConfiguration
            {
                version = 2, endpoint = builder.Uri.AbsoluteUri, model = modelName, apiKey = key
            };
        }

        private static void ValidateHeader(string value, string label)
        {
            if (value.Length > 4096) throw new ArgumentException($"{label}过长。");
            foreach (var c in value)
                if (c < 33 || c > 126)
                    throw new ArgumentException($"{label}不能包含空格、换行或非英文字符。");
        }

        public void Save()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var storage = new AndroidJavaClass("com.matesoul.player.AISecretStore"))
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                storage.CallStatic("save", activity, JsonUtility.ToJson(this));
#else
            // Desktop Editor credentials last only for this play session.
            var publicFields = new PlayerAIConfiguration { version = 2, endpoint = endpoint, model = model };
            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(publicFields));
            PlayerPrefs.Save();
#endif
        }

        public void ApplyHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
        }
    }
}
