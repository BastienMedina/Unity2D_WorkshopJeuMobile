using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MonoBehaviour singleton that saves and loads FloorSaveData as JSON files on disk.
/// One JSON file is written per floor, named "floor_N.json", inside the persistent data folder.
/// Does NOT generate floors, does NOT manage UI, and does NOT interpret save data in any way.
/// </summary>
public class FloorSaveSystem : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    /// <summary>Shared instance. Assigned on Awake; used by TowerManager and GameManager.</summary>
    public static FloorSaveSystem Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string SaveFolderName = "/floors/";
    private const string FilePrefix     = "floor_";
    private const string FileExtension  = ".json";

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Absolute path to the folder where floor JSON files are written.
    /// Uses Application.persistentDataPath because it is the correct Android-compatible
    /// location for user-generated save data — Application.dataPath is read-only on Android.
    /// </summary>
    private string saveFolderPath;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Enforce singleton so both scenes share one save system instance.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // persistentDataPath is not available at class-field initialisation time —
        // it must be read inside a Unity lifecycle callback where the engine is ready.
        saveFolderPath = Application.persistentDataPath + SaveFolderName;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serialises the given FloorSaveData to a JSON file named "floor_N.json".
    /// Creates the save folder if it does not yet exist.
    /// One file per floor allows individual floor loading without reading all saves at once.
    /// </summary>
    /// <param name="floorData">The floor data to persist. Must not be null.</param>
    public void SaveFloor(FloorSaveData floorData)
    {
        if (floorData == null)
        {
            Debug.LogError("[FloorSaveSystem] SaveFloor called with null floorData.");
            return;
        }

        try
        {
            // Ensure the save directory exists before writing — Directory.CreateDirectory
            // is a no-op when the folder already exists, so it is always safe to call.
            Directory.CreateDirectory(saveFolderPath);

            string filePath  = BuildFilePath(floorData.floorIndex);
            string jsonString = JsonUtility.ToJson(floorData, prettyPrint: true);

            // File I/O can fail on Android if storage permissions are missing or if the
            // disk is full — wrapping in try/catch prevents an unhandled exception crash.
            File.WriteAllText(filePath, jsonString);

            Debug.Log($"[FloorSaveSystem] Saved floor {floorData.floorIndex} → {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FloorSaveSystem] Failed to save floor {floorData.floorIndex}: {exception.Message}");
        }
    }

    /// <summary>
    /// Loads the JSON file for the given floor index and returns the deserialised FloorSaveData.
    /// Returns null when the file does not exist, signalling the caller that this floor
    /// has never been played or its save was deleted.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor to load.</param>
    /// <returns>The deserialised FloorSaveData, or null if the file does not exist.</returns>
    public FloorSaveData LoadFloor(int floorIndex)
    {
        string filePath = BuildFilePath(floorIndex);

        // Null return is the clean signal that no save exists — the caller (TowerManager
        // or GameManager) decides how to handle a missing floor without throwing an exception.
        if (!File.Exists(filePath))
            return null;

        try
        {
            string jsonString = File.ReadAllText(filePath);
            FloorSaveData floorData = JsonUtility.FromJson<FloorSaveData>(jsonString);
            return floorData;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FloorSaveSystem] Failed to load floor {floorIndex}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads all floor_N.json files from the save folder and returns them sorted by floorIndex ascending.
    /// Returns an empty list when the folder does not exist or contains no valid files.
    /// TowerManager needs all floors at startup to build the complete tower in one pass.
    /// </summary>
    /// <returns>A list of FloorSaveData sorted by floorIndex ascending.</returns>
    public List<FloorSaveData> LoadAllFloors()
    {
        List<FloorSaveData> allFloors = new List<FloorSaveData>();

        // Return empty immediately — no folder means no saves have ever been written.
        if (!Directory.Exists(saveFolderPath))
            return allFloors;

        string[] filePaths = Directory.GetFiles(saveFolderPath, FilePrefix + "*" + FileExtension);

        foreach (string filePath in filePaths)
        {
            try
            {
                string jsonString    = File.ReadAllText(filePath);
                FloorSaveData floorData = JsonUtility.FromJson<FloorSaveData>(jsonString);

                if (floorData != null)
                    allFloors.Add(floorData);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FloorSaveSystem] Skipping corrupt save file '{filePath}': {exception.Message}");
            }
        }

        // Sort ascending by floorIndex so TowerManager can build blocks bottom-to-top
        // without assuming the file system returns files in any particular order.
        allFloors.Sort((firstFloor, secondFloor) => firstFloor.floorIndex.CompareTo(secondFloor.floorIndex));

        return allFloors;
    }

    /// <summary>
    /// Returns true when the JSON file for the given floor index exists on disk.
    /// TowerManager uses this to decide whether a block should be shown and whether
    /// it should be rendered as completed, current, or locked.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor to check.</param>
    /// <returns>True if the save file exists; false otherwise.</returns>
    public bool FloorExists(int floorIndex)
    {
        return File.Exists(BuildFilePath(floorIndex));
    }

    /// <summary>
    /// Deletes every file inside the save folder, effectively resetting all progress.
    /// Useful for testing and for a player-facing "Reset Progress" feature.
    /// </summary>
    public void DeleteAllSaves()
    {
        if (!Directory.Exists(saveFolderPath))
            return;

        string[] filePaths = Directory.GetFiles(saveFolderPath);

        foreach (string filePath in filePaths)
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FloorSaveSystem] Could not delete '{filePath}': {exception.Message}");
            }
        }

        Debug.Log("[FloorSaveSystem] All saves deleted.");
    }

    /// <summary>
    /// Scans the save folder and returns the highest N found across all floor_N.json files.
    /// Returns 0 when no saves exist so the caller always receives a valid non-negative index.
    /// TowerManager uses this to know how many floor blocks to display at startup.
    /// </summary>
    /// <returns>The highest saved floor index, or 0 if no saves exist.</returns>
    public int GetHighestSavedFloorIndex()
    {
        if (!Directory.Exists(saveFolderPath))
            return 0;

        string[] filePaths = Directory.GetFiles(saveFolderPath, FilePrefix + "*" + FileExtension);

        int highestIndex = 0;

        foreach (string filePath in filePaths)
        {
            string fileName    = Path.GetFileNameWithoutExtension(filePath);
            string indexString = fileName.Replace(FilePrefix, string.Empty);

            // ParsedIndex defaults to -1 so a corrupt or non-numeric filename
            // is silently ignored rather than producing a misleading highest value.
            if (!int.TryParse(indexString, out int parsedIndex))
                continue;

            if (parsedIndex > highestIndex)
                highestIndex = parsedIndex;
        }

        return highestIndex;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the full absolute file path for the given floor index.
    /// </summary>
    /// <param name="floorIndex">Zero-based floor index.</param>
    /// <returns>Absolute path string for the floor's JSON file.</returns>
    private string BuildFilePath(int floorIndex)
    {
        return saveFolderPath + FilePrefix + floorIndex + FileExtension;
    }

    /// <summary>
    /// Loads and returns the save data for the highest completed floor.
    /// Returns null when no completed floor save exists yet (first session, or all saves deleted).
    /// GameManager uses this to feed the last completed floor into FloorDifficultyProgression
    /// so the next floor's parameters are always derived from accurate, persisted data.
    /// </summary>
    /// <returns>The FloorSaveData for the highest completed floor, or null if none exists.</returns>
    public FloorSaveData GetLatestCompletedFloor()
    {
        int highestIndex = GetHighestSavedFloorIndex();

        // No saves at all — first session, return null to signal fresh start.
        if (highestIndex == 0 && !FloorExists(0))
            return null;

        FloorSaveData latestFloor = LoadFloor(highestIndex);

        // The highest-index file might exist but be incomplete (e.g. interrupted write).
        // Return null in that case so the caller falls back to base parameters safely.
        if (latestFloor == null || !latestFloor.isCompleted)
            return null;

        return latestFloor;
    }
}
