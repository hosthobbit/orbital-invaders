using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Orbital
{
    /// <summary>Top-ten table with arcade initials, saved as JSON next to the player's prefs.</summary>
    public static class HighScores
    {
        public const int Size = 10;

        [Serializable]
        public class Entry
        {
            public string name;
            public int score;
            public int wave;
        }

        [Serializable]
        class Table
        {
            public List<Entry> entries = new List<Entry>();
        }

        static Table table;
        static string path;

        public static IReadOnlyList<Entry> Entries { get { Load(); return table.entries; } }
        public static int Top { get { Load(); return table.entries.Count > 0 ? table.entries[0].score : 0; } }

        /// <summary>Automated test runs keep their own table so they never touch the real one.</summary>
        public static void Init(bool testRun)
        {
            path = Path.Combine(Application.persistentDataPath, testRun ? "highscores-test.json" : "highscores.json");
            table = null;
            if (testRun && File.Exists(path)) File.Delete(path);
            Load();
        }

        static void Load()
        {
            if (table != null) return;
            if (path == null) path = Path.Combine(Application.persistentDataPath, "highscores.json");
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // in the browser, PlayerPrefs live in IndexedDB and survive reloads
                string json = PlayerPrefs.GetString("highscores", "");
                if (json.Length > 0) table = JsonUtility.FromJson<Table>(json);
#else
                if (File.Exists(path)) table = JsonUtility.FromJson<Table>(File.ReadAllText(path));
#endif
            }
            catch (Exception e) { Debug.LogWarning("High score table unreadable, starting fresh: " + e.Message); }
            if (table == null || table.entries == null || table.entries.Count == 0) table = Defaults();
            table.entries.Sort((a, b) => b.score.CompareTo(a.score));
        }

        static Table Defaults()
        {
            // something to beat on day one, attract-mode style
            string[] names = { "HHB", "ACE", "ZAP", "UFO", "ORB", "SKY", "PEW", "BOT", "CPU", "GG!" };
            int[] scores = { 10000, 8000, 6500, 5000, 4000, 3000, 2000, 1500, 1000, 500 };
            int[] waves = { 6, 5, 5, 4, 4, 3, 3, 2, 2, 1 };
            var t = new Table();
            for (int i = 0; i < names.Length; i++) t.entries.Add(new Entry { name = names[i], score = scores[i], wave = waves[i] });
            return t;
        }

        public static bool Qualifies(int score)
        {
            Load();
            return score > 0 && (table.entries.Count < Size || score > table.entries[table.entries.Count - 1].score);
        }

        /// <summary>1-based rank the score would take.</summary>
        public static int RankFor(int score)
        {
            Load();
            int r = 1;
            foreach (var e in table.entries) if (e.score >= score) r++;
            return r;
        }

        /// <summary>Inserts and saves; returns the 0-based row of the new entry (or -1).</summary>
        public static int Add(string name, int score, int wave)
        {
            Load();
            if (!Qualifies(score)) return -1;
            var entry = new Entry { name = name, score = score, wave = wave };
            int row = RankFor(score) - 1;
            table.entries.Insert(row, entry);
            while (table.entries.Count > Size) table.entries.RemoveAt(table.entries.Count - 1);
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                PlayerPrefs.SetString("highscores", JsonUtility.ToJson(table));
                PlayerPrefs.Save();
#else
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(table, true));
#endif
            }
            catch (Exception e) { Debug.LogWarning("Could not save high scores: " + e.Message); }
            return row;
        }
    }
}
