/// <summary>
/// Static holder for the floor data selected in TowerScene before transitioning to GameScene.
/// Survives scene transitions via static fields without requiring DontDestroyOnLoad complexity.
/// Does NOT save to disk, does NOT manage UI, and does NOT contain any Unity lifecycle logic.
/// </summary>
public static class FloorSessionData
{
    /// <summary>
    /// The floor data chosen by the player in TowerScene.
    /// Set by TowerManager immediately before loading GameScene.
    /// Read by GameManager in Start() — cleared immediately after reading to prevent
    /// stale data from affecting any future session without a fresh selection.
    /// Static field survives scene load, providing a lightweight bridge between scenes.
    /// </summary>
    public static FloorSaveData SelectedFloor { get; set; }

    /// <summary>
    /// True when the player tapped a floor block that is already marked as completed.
    /// GameManager reads this flag to decide whether to call RestoreFloorFromSave()
    /// (replay) or InitializeDay() (fresh progression).
    /// Cleared alongside SelectedFloor so GameManager does not misread it on subsequent starts.
    /// </summary>
    public static bool IsReplayingFloor { get; set; }

    /// <summary>
    /// Clears both SelectedFloor and IsReplayingFloor.
    /// Called by GameManager at the very start of Start() after reading both values,
    /// ensuring no stale state leaks into future scene loads.
    /// </summary>
    public static void Clear()
    {
        SelectedFloor    = null;
        IsReplayingFloor = false;
    }
}
