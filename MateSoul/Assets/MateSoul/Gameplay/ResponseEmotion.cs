namespace MateSoul.Gameplay
{
    public enum ResponseEmotion
    {
        Normal,
        Happy,
        Sad,
        Angry
    }

    public static class ResponseEmotionProtocol
    {
        public static bool TryParse(string value, out ResponseEmotion emotion)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "normal":
                case "neutral":
                case "calm":
                case "平静":
                case "普通":
                    emotion = ResponseEmotion.Normal;
                    return true;
                case "happy":
                case "joyful":
                case "excited":
                case "开心":
                case "高兴":
                    emotion = ResponseEmotion.Happy;
                    return true;
                case "sad":
                case "unhappy":
                case "难过":
                case "伤心":
                    emotion = ResponseEmotion.Sad;
                    return true;
                case "angry":
                case "mad":
                case "生气":
                case "愤怒":
                    emotion = ResponseEmotion.Angry;
                    return true;
                default:
                    emotion = default;
                    return false;
            }
        }

        // Emotion only tints the face and voice, so an unexpected label must never
        // invalidate an otherwise usable reply.
        public static ResponseEmotion ParseOrDefault(string value)
            => TryParse(value, out var emotion) ? emotion : ResponseEmotion.Normal;
    }
}
