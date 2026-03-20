using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot Editor utility: duplicates SampleScene into GameScene and registers
/// TowerScene + GameScene in Build Settings in that exact order.
/// Run via: Tools → Setup GameScene
/// Delete this file immediately after running.
/// </summary>
public static class GameSceneSetup
{
    private const string SourceScenePath      = "Assets/Scenes/SampleScene.unity";
    private const string DestinationScenePath = "Assets/Scenes/GameScene.unity";
    private const string TowerScenePath       = "Assets/Scenes/TowerScene.unity";

    [MenuItem("Tools/Setup GameScene")]
    public static void Run()
    {
        // ----------------------------------------------------------------
        // Step 1 — Duplicate SampleScene → GameScene
        // AssetDatabase.CopyAsset preserves all serialised GUIDs and
        // component references that exist within the same scene file,
        // which a "create new scene" call would not.
        // ----------------------------------------------------------------
        if (AssetDatabase.LoadAssetAtPath<Object>(DestinationScenePath) != null)
        {
            Debug.Log("[GameSceneSetup] GameScene.unity already exists — skipping copy.");
        }
        else
        {
            bool copied = AssetDatabase.CopyAsset(SourceScenePath, DestinationScenePath);
            if (copied)
                Debug.Log("[GameSceneSetup] SampleScene duplicated to GameScene.unity successfully.");
            else
                Debug.LogError("[GameSceneSetup] Failed to copy SampleScene → GameScene. Aborting.");
        }

        // ----------------------------------------------------------------
        // Step 2 — Register scenes in Build Settings
        // Order: index 0 = TowerScene, index 1 = GameScene
        // SampleScene is intentionally omitted — it remains a local
        // playtest backup and must never appear in the production build.
        // ----------------------------------------------------------------
        EditorBuildSettingsScene[] scenes =
        {
            new EditorBuildSettingsScene(TowerScenePath,       true),
            new EditorBuildSettingsScene(DestinationScenePath, true),
        };

        EditorBuildSettings.scenes = scenes;
        Debug.Log("[GameSceneSetup] Build Settings updated: TowerScene (0), GameScene (1).");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[GameSceneSetup] Done. Delete Assets/Scripts/Editor/GameSceneSetup.cs now.");
    }
}
