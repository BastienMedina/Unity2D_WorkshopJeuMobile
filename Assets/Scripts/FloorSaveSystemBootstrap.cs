using UnityEngine;

/// <summary>
/// Ensures a <see cref="FloorSaveSystem"/> singleton is available in any scene that needs
/// to read floor save files — typically Menu_Principal and any scene that hosts <see cref="FloorButtonSpawner"/>.
///
/// Place this component on a dedicated GameObject in Menu_Principal alongside <see cref="FloorSaveSystem"/>.
/// It calls <c>DontDestroyOnLoad</c> so the singleton persists into GameScene when the player transitions,
/// giving <see cref="GameManager"/> access to <see cref="FloorSaveSystem"/> for story-mode saving
/// without requiring a second instance in GameScene.
///
/// If a <see cref="FloorSaveSystem"/> singleton already exists when this scene loads (e.g. a hot-reload),
/// this bootstrap destroys its own GameObject to prevent duplication.
/// </summary>
[RequireComponent(typeof(FloorSaveSystem))]
public class FloorSaveSystemBootstrap : MonoBehaviour
{
    private void Awake()
    {
        // FloorSaveSystem enforces its own singleton in its Awake — if Instance is already set
        // to a different object, that object destroys us there. Guard here as a belt-and-suspenders
        // measure so the bootstrap never leaves a stale GameObject in the hierarchy.
        if (FloorSaveSystem.Instance != null && FloorSaveSystem.Instance.gameObject != gameObject)
        {
            Destroy(gameObject);
            return;
        }

        // Keep the save system alive across scene loads so GameManager in GameScene can use it.
        DontDestroyOnLoad(gameObject);
    }
}
