using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MateSoul.Runtime
{
    /// <summary>
    /// HUD 内置拼音输入的字词候选表。数据随包放在 Resources/MateSoulUI/PinyinImeData.txt，
    /// 由 mozillazg/pinyin-data、mozillazg/phrase-pinyin-data、fxsjy/jieba 与
    /// hermitdave/FrequencyWords 生成（均为 MIT）。
    /// </summary>
    public static class PinyinIme
    {
        private const string ResourcePath = "MateSoulUI/PinyinImeData";
        private const int MaxWordsPerPrefix = 24;

        private static bool initialized;
        private static Dictionary<string, string> characters;
        private static Dictionary<string, List<string>> words;

        public static bool IsLetter(char value) => value >= 'a' && value <= 'z';

        public static void Fill(string pinyin, List<string> candidates)
        {
            if (candidates == null) return;
            candidates.Clear();
            if (string.IsNullOrEmpty(pinyin)) return;
            EnsureLoaded();
            if (characters == null) return;

            var buffer = pinyin.ToLowerInvariant();
            if (characters.TryGetValue(buffer, out var single))
            {
                // 单音节先给常用单字，再补以该拼音开头的词。
                AddCharacters(candidates, single, 4);
                AddWords(candidates, buffer, 6);
                return;
            }
            AddWords(candidates, buffer, 4);
            var suffix = FindSuffix(buffer);
            if (suffix != null && characters.TryGetValue(suffix, out single))
                AddCharacters(candidates, single, 6);
        }

        private static string FindSuffix(string buffer)
        {
            for (var length = Math.Min(buffer.Length, 6); length >= 1; length--)
            {
                var suffix = buffer.Substring(buffer.Length - length, length);
                if (characters.ContainsKey(suffix)) return suffix;
            }
            return null;
        }

        private static void AddCharacters(List<string> candidates, string value, int max)
        {
            for (var index = 0; index < value.Length && candidates.Count < max; index++)
            {
                var candidate = value[index].ToString();
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }
        }

        private static void AddWords(List<string> candidates, string pinyin, int max)
        {
            if (!words.TryGetValue(pinyin, out var matches)) return;
            foreach (var candidate in matches)
            {
                if (candidates.Count >= max) return;
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }
        }

        private static void EnsureLoaded()
        {
            if (initialized) return;
            initialized = true;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("Pinyin IME data is missing: " + ResourcePath);
                return;
            }
            var charTable = new Dictionary<string, string>(StringComparer.Ordinal);
            var wordTable = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var inWords = false;
            using (var reader = new StringReader(asset.text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (line[0] == '#')
                    {
                        if (line.StartsWith("# words", StringComparison.Ordinal)) inWords = true;
                        continue;
                    }
                    var separator = line.IndexOf('|');
                    if (separator <= 0) continue;
                    var pinyin = line.Substring(0, separator);
                    var value = line.Substring(separator + 1);
                    if (!inWords)
                    {
                        charTable[pinyin] = value;
                        continue;
                    }
                    for (var length = pinyin.Length; length >= 2; length--)
                    {
                        var prefix = pinyin.Substring(0, length);
                        if (!wordTable.TryGetValue(prefix, out var list))
                        {
                            list = new List<string>(4);
                            wordTable[prefix] = list;
                        }
                        if (list.Count < MaxWordsPerPrefix && !list.Contains(value))
                            list.Add(value);
                    }
                }
            }
            characters = charTable;
            words = wordTable;
        }
    }
}
