using System;
using System.Collections.Generic;
using System.IO;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class JsonGameSaveStore : IGameSaveStore
    {
        private readonly string path;
        public JsonGameSaveStore(string path) { this.path = path; }
        public GameSaveData Load()
        {
            if (!File.Exists(path)) return new GameSaveData();
            var data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(path));
            if (data != null && data.personality == null)
                data.personality = new PersonalitySelectionData();
            if (data != null && data.inventory == null)
                data.inventory = Array.Empty<ItemInventorySaveData>();
            Validate(data);
            return data;
        }

        public void Save(GameSaveData data)
        {
            Validate(data);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void Validate(GameSaveData data)
        {
            if (data == null || data.version != 1)
                throw new InvalidDataException("Unsupported or invalid MateSoul save version.");
            if (data.personality == null || data.personality.selections == null ||
                data.mood == null || data.mood.meters == null || data.items == null ||
                data.inventory == null)
                throw new InvalidDataException("Save is missing its personality, mood, or item collections.");
            var ids = new HashSet<string>();
            foreach (var item in data.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.instanceId) ||
                    string.IsNullOrWhiteSpace(item.definitionId) || !ids.Add(item.instanceId) ||
                    !Finite(item.x) || !Finite(item.y) || !Finite(item.z) ||
                    !Finite(item.qx) || !Finite(item.qy) || !Finite(item.qz) || !Finite(item.qw) ||
                    Mathf.Abs(item.qx * item.qx + item.qy * item.qy + item.qz * item.qz + item.qw * item.qw - 1) > .01f)
                    throw new InvalidDataException("Save contains an invalid item ID or pose.");
                if (item.cutState?.planes == null ||
                    item.cutState.planes.Length == 0) continue;
                if (item.cutState.version != 1 ||
                    item.cutState.planes.Length > 4)
                    throw new InvalidDataException("Save contains an unsupported cut history.");
                foreach (var plane in item.cutState.planes)
                {
                    if (plane == null || !Finite(plane.nx) || !Finite(plane.ny) ||
                        !Finite(plane.nz) || !Finite(plane.distance))
                        throw new InvalidDataException("Save contains an invalid cut plane.");
                    var length = plane.nx * plane.nx + plane.ny * plane.ny +
                                 plane.nz * plane.nz;
                    if (Mathf.Abs(length - 1f) > .002f)
                        throw new InvalidDataException(
                            "Save contains a non-normalized cut plane.");
                }
            }
            ids.Clear();
            foreach (var stock in data.inventory)
                if (stock == null || string.IsNullOrWhiteSpace(stock.definitionId) ||
                    stock.remaining < 0 || !ids.Add(stock.definitionId))
                    throw new InvalidDataException("Save contains invalid or duplicate item inventory.");
            if (data.items.Length > 0 && string.IsNullOrWhiteSpace(data.roomAnchorId))
                throw new InvalidDataException("Saved items need a room anchor.");
            if (data.hasRoomAnchorPose &&
                (!Finite(data.roomX) || !Finite(data.roomY) || !Finite(data.roomZ) ||
                 !Finite(data.roomQx) || !Finite(data.roomQy) || !Finite(data.roomQz) ||
                 !Finite(data.roomQw) ||
                 Mathf.Abs(data.roomQx * data.roomQx + data.roomQy * data.roomQy +
                           data.roomQz * data.roomQz + data.roomQw * data.roomQw - 1) > .01f))
                throw new InvalidDataException("Save contains an invalid room anchor pose.");
            if (!Finite(data.mood.remainingSeconds) || data.mood.remainingSeconds < -1 ||
                !Enum.IsDefined(typeof(MoodLevel), data.mood.level))
                throw new InvalidDataException("Save contains an invalid mood timer.");
            ids.Clear();
            foreach (var meter in data.mood.meters)
                if (meter == null || string.IsNullOrEmpty(meter.id) || !ids.Add(meter.id) ||
                    !Finite(meter.value) || meter.value < 0 || meter.value > 100)
                    throw new InvalidDataException("Save contains an invalid mood meter.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
