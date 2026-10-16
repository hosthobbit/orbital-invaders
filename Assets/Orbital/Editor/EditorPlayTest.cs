using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbital.EditorTools
{
    /// <summary>Opens the game scene, plays it for 25s in the real editor and exits with the game-error count.</summary>
    [InitializeOnLoad]
    public static class EditorPlayTest
    {
        const string Key = "Orbital.PlayTest";

        static EditorPlayTest()
        {
            if (!SessionState.GetBool(Key, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetInt(Key + ".errors", 0);
            SessionState.SetFloat(Key + ".start", 0f);
            Application.logMessageReceived += OnLog;
            EditorSceneManager.OpenScene("Assets/Orbital/Scenes/Orbital.unity");
            EditorApplication.EnterPlaymode();
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // Unity's own editor-window layout glitch is not a game error.
            if (stack != null && stack.Contains("UnityEditor.DockArea")) return;
            SessionState.SetInt(Key + ".errors", SessionState.GetInt(Key + ".errors", 0) + 1);
            System.Console.WriteLine("PLAYTEST_ERROR " + msg + "\n" + stack);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (SessionState.GetFloat(Key + ".start", 0f) <= 0f) SessionState.SetFloat(Key + ".start", (float)EditorApplication.timeSinceStartup);
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".start", 0f) < 30f) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool(Key, false);
            int errors = SessionState.GetInt(Key + ".errors", 0);
            Debug.Log($"PLAYTEST_DONE errors={errors} score={Orbital.Game.I?.Score} wave={Orbital.Game.I?.Wave} state={Orbital.Game.I?.State}");
            System.IO.File.WriteAllText("Logs/playtest-result.txt", $"errors={errors} wave={Orbital.Game.I?.Wave} state={Orbital.Game.I?.State}\n");
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
