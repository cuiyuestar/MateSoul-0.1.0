using System;

namespace MateSoul.Gameplay
{
    [Serializable]
    public sealed class ItemSaveData
    {
        public string instanceId;
        public string definitionId;
        public string stateId;
        public float x, y, z;
        public float qx, qy, qz, qw = 1;
        public bool needsGravitySettling;
    }

    [Serializable]
    public sealed class GameSaveData
    {
        public int version = 1;
        public string outfitId;
        public bool subtitles = true;
        public string roomAnchorId;
        public bool hasRoomAnchorPose;
        public float roomX, roomY, roomZ;
        public float roomQx, roomQy, roomQz, roomQw = 1;
        public PersonalitySelectionData personality = new PersonalitySelectionData();
        public MoodSaveData mood = new MoodSaveData();
        public ItemSaveData[] items = Array.Empty<ItemSaveData>();
    }

    public interface IGameSaveStore
    {
        GameSaveData Load();
        void Save(GameSaveData data);
    }
}
