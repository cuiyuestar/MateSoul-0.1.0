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
            switch (value)
            {
                case "normal":
                    emotion = ResponseEmotion.Normal;
                    return true;
                case "happy":
                    emotion = ResponseEmotion.Happy;
                    return true;
                case "sad":
                    emotion = ResponseEmotion.Sad;
                    return true;
                case "angry":
                    emotion = ResponseEmotion.Angry;
                    return true;
                default:
                    emotion = default;
                    return false;
            }
        }
    }
}
