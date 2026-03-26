using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MonoBehaviour singleton that saves and loads FloorSaveData as JSON files on disk.
/// One JSON file is written per floor, named "floor_N.json", inside the persistent data folder.
/// Does NOT generate floors, does NOT manage UI, and does NOT interpret save data in any way.
///
/// Designer floors originate from StreamingAssets/FloorDesigns/ but are copied to
/// persistentDataPath/floors/ at startup by <see cref="StreamingAssetsInstaller"/> using
/// UnityWebRequest — the only API that can read StreamingAssets on Android (files inside APK).
/// All reads and writes in this class therefore target persistentDataPath exclusively,
/// keeping the I/O layer simple and cross-platform.
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
    /// Absolute path to the folder where floor JSON files are read and written at runtime.
    /// Uses Application.persistentDataPath — writable on all platforms including Android.
    /// StreamingAssetsInstaller populates this folder from StreamingAssets at startup.
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
    /// Returns null when the file does not exist.
    /// </summary>
    public FloorSaveData LoadFloor(int floorIndex)
    {
        string filePath = BuildFilePath(floorIndex);

        if (!File.Exists(filePath))
            return null;

        try
        {
            string jsonString     = File.ReadAllText(filePath);
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
    /// Loads all floor_N.json files from persistentDataPath and returns them sorted by floorIndex.
    /// Returns an empty list when no valid files exist.
    /// StreamingAssetsInstaller must have completed before this is called — FloorButtonSpawner
    /// ensures this by waiting for <see cref="StreamingAssetsInstaller.IsReady"/>.
    /// </summary>
    /// <returns>A list of FloorSaveData sorted by floorIndex ascending.</returns>
    public List<FloorSaveData> LoadAllFloors()
    {
        List<FloorSaveData> allFloors = new List<FloorSaveData>();

        if (!Directory.Exists(saveFolderPath))
            return allFloors;

        foreach (string filePath in Directory.GetFiles(saveFolderPath, FilePrefix + "*" + FileExtension))
        {
            try
            {
                string jsonString     = File.ReadAllText(filePath);
                FloorSaveData floorData = JsonUtility.FromJson<FloorSaveData>(jsonString);

                if (floorData != null)
                    allFloors.Add(floorData);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FloorSaveSystem] Skipping corrupt save file '{filePath}': {exception.Message}");
            }
        }

        allFloors.Sort((a, b) => a.floorIndex.CompareTo(b.floorIndex));
        return allFloors;
    }

    /// <summary>
    /// Returns true when the JSON file for the given floor index exists in persistentDataPath.
    /// <see cref="StreamingAssetsInstaller"/> must have completed before this is meaningful.
    /// </summary>
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
    /// Scans persistentDataPath/floors/ and returns the highest floor index found.
    /// Returns 0 when no saves exist so the caller always receives a valid non-negative index.
    /// </summary>
    public int GetHighestSavedFloorIndex()
    {
        return ScanFolderForHighestIndex(saveFolderPath, 0);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the absolute path of the floor_N.json file in persistentDataPath.
    /// Does not check whether the file exists — use <see cref="FloorExists"/> for that.
    /// </summary>
    private string BuildFilePath(int floorIndex)
    {
        return saveFolderPath + FilePrefix + floorIndex + FileExtension;
    }

    /// <summary>
    /// Scans a folder for floor_N.json files and returns the highest index found,
    /// or <paramref name="currentHighest"/> if no higher index is found.
    /// Silently skips files whose names cannot be parsed as floor indices.
    /// </summary>
    private int ScanFolderForHighestIndex(string folderPath, int currentHighest)
    {
        if (!Directory.Exists(folderPath))
            return currentHighest;

        foreach (string filePath in Directory.GetFiles(folderPath, FilePrefix + "*" + FileExtension))
        {
            if (TryParseFloorIndex(filePath, out int parsedIndex) && parsedIndex > currentHighest)
                currentHighest = parsedIndex;
        }

        return currentHighest;
    }

    /// <summary>
    /// Attempts to parse the floor index from a file path of the form "…/floor_N.json".
    /// Returns false and sets <paramref name="floorIndex"/> to -1 for unrecognised file names.
    /// </summary>
    private bool TryParseFloorIndex(string filePath, out int floorIndex)
    {
        string fileName    = Path.GetFileNameWithoutExtension(filePath);
        string indexString = fileName.Replace(FilePrefix, string.Empty);
        return int.TryParse(indexString, out floorIndex);
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
