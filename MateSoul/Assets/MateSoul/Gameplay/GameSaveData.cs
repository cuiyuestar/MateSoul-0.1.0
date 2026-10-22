using System;

namespace MateSoul.Gameplay
{
    [Serializable]
    public sealed class CutPlaneSaveData
    {
        public float nx, ny, nz, distance;
        public bool positiveSide;
    }

    [Serializable]
    public sealed class CutStateSaveData
    {
        public int version = 1;
        public CutPlaneSaveData[] planes = Array.Empty<CutPlaneSaveData>();
    }

    [Serializable]
    public sealed class ItemSaveData
    {
        public string instanceId;
        public string definitionId;
        public string inventoryDefinitionId;
        public string inventoryUnitId;
        public string stateId;
        public float x, y, z;
        public float qx, qy, qz, qw = 1;
        public bool needsGravitySettling;
        public CutStateSaveData cutState;
    }

    [Serializable]
    public sealed class ItemInventorySaveData
    {
        public string definitionId;
        public int remaining;
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
        public ItemInventorySaveData[] inventory = Array.Empty<ItemInventorySaveData>();
        public string dailyInventoryDate;
    }

    public interface IGameSaveStore
    {
        GameSaveData Load();
        void Save(GameSaveData data);
    }
}
