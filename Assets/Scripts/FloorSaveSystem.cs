using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MonoBehaviour singleton that saves and loads FloorSaveData as JSON files on disk.
/// One JSON file is written per floor, named "floor_N.json", inside the persistent data folder.
/// Does NOT generate floors, does NOT manage UI, and does NOT interpret save data in any way.
///
/// Editor priority: when running inside the Unity Editor, LoadFloor and FloorExists first check
/// Assets/Editor/FloorDesigns/ (written by the Floor Designer tool). This ensures the latest
/// designer-authored version is always used during Editor playtests without any manual copy step.
/// On device builds, only persistentDataPath is used.
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

    private const string SaveFolderName      = "/floors/";
    private const string FilePrefix          = "floor_";
    private const string FileExtension       = ".json";

    /// <summary>
    /// Project-relative path of the folder written by the Floor Designer tool.
    /// Used as a priority source when running in the Unity Editor so the designer
    /// never has to manually copy files to persistentDataPath after each save.
    /// </summary>
    private const string DesignerFolderRelative = "Assets/Editor/FloorDesigns";

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Absolute path to the folder where floor JSON files are written at runtime.
    /// Uses Application.persistentDataPath — the correct Android-compatible location
    /// for user-generated save data (Application.dataPath is read-only on Android).
    /// </summary>
    private string saveFolderPath;

    /// <summary>
    /// Absolute path to the Floor Designer output folder inside the project.
    /// Populated in Awake from the project's root directory so it works on any machine.
    /// Only consulted when Application.isEditor is true.
    /// </summary>
    private string designerFolderPath;

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

        // Resolve the designer folder relative to the project root (one level above Assets/).
        // Path.GetFullPath normalises separators so comparisons work on Windows and macOS alike.
        string projectRoot  = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        designerFolderPath  = Path.GetFullPath(Path.Combine(projectRoot, DesignerFolderRelative))
                              + Path.DirectorySeparatorChar;
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
    ///
    /// In the Unity Editor, the Designer folder (Assets/Editor/FloorDesigns/) is checked first
    /// so the latest designer-authored version is always used during playtesting without any
    /// manual copy step. Falls back to persistentDataPath when no designer file is found.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor to load.</param>
    /// <returns>The deserialised FloorSaveData, or null if the file does not exist.</returns>
    public FloorSaveData LoadFloor(int floorIndex)
    {
        string filePath = ResolveFilePath(floorIndex);

        // Null return is the clean signal that no save exists for this floor.
        if (filePath == null)
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
    /// Loads all floor_N.json files and returns them sorted by floorIndex ascending.
    /// Returns an empty list when no valid files exist in either folder.
    /// In the Unity Editor, files in the Designer folder take priority over identically-named
    /// files in persistentDataPath so the latest designer versions are always used.
    /// TowerManager needs all floors at startup to build the complete tower in one pass.
    /// </summary>
    /// <returns>A list of FloorSaveData sorted by floorIndex ascending.</returns>
    public List<FloorSaveData> LoadAllFloors()
    {
        List<FloorSaveData> allFloors = new List<FloorSaveData>();

        // Collect candidate file paths from both sources, with designer folder taking priority.
        // Using a Dictionary keyed by floorIndex ensures each floor appears only once.
        var indexToPath = new System.Collections.Generic.Dictionary<int, string>();

        // 1. Seed from persistentDataPath (lowest priority).
        if (Directory.Exists(saveFolderPath))
        {
            foreach (string filePath in Directory.GetFiles(saveFolderPath, FilePrefix + "*" + FileExtension))
            {
                if (TryParseFloorIndex(filePath, out int idx))
                    indexToPath[idx] = filePath;
            }
        }

#if UNITY_EDITOR
        // 2. Override with designer folder (highest priority in Editor).
        if (Directory.Exists(designerFolderPath))
        {
            foreach (string filePath in Directory.GetFiles(designerFolderPath, FilePrefix + "*" + FileExtension))
            {
                if (TryParseFloorIndex(filePath, out int idx))
                    indexToPath[idx] = filePath; // overwrites the persistent entry for the same index
            }
        }
#endif

        foreach (string filePath in indexToPath.Values)
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
    ///
    /// In the Unity Editor, the Designer folder is checked first so the tower reflects
    /// the latest floors saved from the Floor Designer tool immediately.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor to check.</param>
    /// <returns>True if the save file exists; false otherwise.</returns>
    public bool FloorExists(int floorIndex)
    {
        return ResolveFilePath(floorIndex) != null;
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
    /// Scans the save folder(s) and returns the highest N found across all floor_N.json files.
    /// Returns 0 when no saves exist so the caller always receives a valid non-negative index.
    /// In the Unity Editor, the Designer folder is also scanned so newly created floors appear
    /// in the tower without requiring a separate copy to persistentDataPath.
    /// TowerManager uses this to know how many floor blocks to display at startup.
    /// </summary>
    /// <returns>The highest saved floor index, or 0 if no saves exist.</returns>
    public int GetHighestSavedFloorIndex()
    {
        int highestIndex = 0;

        highestIndex = ScanFolderForHighestIndex(saveFolderPath, highestIndex);

#if UNITY_EDITOR
        highestIndex = ScanFolderForHighestIndex(designerFolderPath, highestIndex);
#endif

        return highestIndex;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the absolute path of the floor_N.json file to read for the given index.
    /// In the Unity Editor, the Designer folder (Assets/Editor/FloorDesigns/) is checked first;
    /// falls back to persistentDataPath when the designer file is absent.
    /// Returns null when neither location has a file for this floor index.
    /// </summary>
    /// <param name="floorIndex">Zero-based floor index.</param>
    /// <returns>Absolute path to the file, or null if not found anywhere.</returns>
    private string ResolveFilePath(int floorIndex)
    {
        string fileName = FilePrefix + floorIndex + FileExtension;

#if UNITY_EDITOR
        string designerPath = designerFolderPath + fileName;
        if (File.Exists(designerPath))
        {
            Debug.Log($"[FloorSaveSystem] Loading floor {floorIndex} from Designer folder: {designerPath}");
            return designerPath;
        }
#endif

        string persistentPath = saveFolderPath + fileName;
        return File.Exists(persistentPath) ? persistentPath : null;
    }

    /// <summary>
    /// Builds the runtime (persistentDataPath) file path for the given floor index.
    /// Used by SaveFloor to always write to the runtime location, keeping designer files
    /// as the authoritative source in Editor and runtime saves separate on device.
    /// </summary>
    /// <param name="floorIndex">Zero-based floor index.</param>
    /// <returns>Absolute path string for the floor's runtime JSON file.</returns>
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
