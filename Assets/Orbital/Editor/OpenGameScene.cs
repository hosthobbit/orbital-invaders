using UnityEditor;
using UnityEditor.SceneManagement;

namespace Orbital.EditorTools
{
    /// <summary>Makes the editor open the game scene and always start it when Play is pressed.</summary>
    [InitializeOnLoad]
    static class OpenGameScene
    {
        const string ScenePath = "Assets/Orbital/Scenes/Orbital.unity";

        static OpenGameScene()
        {
            EditorApplication.delayCall += () =>
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                if (!scene) return;
                EditorSceneManager.playModeStartScene = scene;
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                var active = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }
    }
}
