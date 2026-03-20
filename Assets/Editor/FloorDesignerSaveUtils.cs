using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Static utility class that converts FloorDesignerData to FloorSaveData,
/// writes and reads JSON floor files, and handles versioned save naming.
/// Does NOT display any UI — all output is via Debug.Log / Debug.LogError.
/// Does NOT load or unload scenes.
/// Placing this file outside an Editor folder would include it in Android builds
/// and cause compilation failure due to UnityEditor usages in the window class.
/// </summary>
public static class FloorDesignerSaveUtils
{
    /// <summary>
    /// Absolute path of the directory where floor JSON files are written.
    /// Uses Application.persistentDataPath so the folder survives Unity reimports.
    /// </summary>
    private static string saveFolderPath => Application.persistentDataPath + "/floors/";

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the given FloorDesignerData to FloorSaveData and writes it to JSON.
    /// When overwrite is true, writes to "floor_N.json" (replaces existing).
    /// When overwrite is false, finds the next free versioned suffix and writes
    /// "floor_N_vX.json" — never overwriting an existing file so the designer
    /// can keep multiple versions in parallel without losing previous work.
    /// Sets isDirty = false on the data if the write succeeds.
    /// </summary>
    /// <param name="designerData">The floor data to convert and save.</param>
    /// <param name="overwrite">True to replace the canonical floor_N.json; false to create a new version.</param>
    public static void SaveFloor(FloorDesignerData designerData, bool overwrite)
    {
        EnsureSaveFolderExists();

        FloorSaveData saveData = ConvertToSaveData(designerData);
        string filePath = BuildSavePath(designerData.floorIndex, overwrite);

        try
        {
            string json = JsonUtility.ToJson(saveData, prettyPrint: true);
            File.WriteAllText(filePath, json);
            designerData.isDirty = false;
            Debug.Log($"[FloorDesigner] Floor saved successfully → {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FloorDesigner] Failed to save floor {designerData.floorIndex}: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads floor_N.json from disk and converts it into a FloorDesignerData ready for editing.
    /// Returns null if no canonical save file exists for the given index.
    /// Allows the designer to resume editing a previously saved floor without losing the JSON.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor to load.</param>
    /// <returns>Populated FloorDesignerData, or null if the file does not exist.</returns>
    public static FloorDesignerData LoadFloorIntoDesigner(int floorIndex)
    {
        string filePath = saveFolderPath + $"floor_{floorIndex}.json";

        if (!File.Exists(filePath))
            return null;

        try
        {
            string json = File.ReadAllText(filePath);
            FloorSaveData saveData = JsonUtility.FromJson<FloorSaveData>(json);
            return ConvertToDesignerData(saveData);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FloorDesigner] Failed to load floor {floorIndex}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a new FloorDesignerData derived from an existing floor by applying
    /// percentage deltas to each profitability parameter.
    /// The rules list is intentionally left empty — the designer fills it manually.
    /// isDirty is set to true immediately because the generated floor has not been saved yet,
    /// and the designer must explicitly click Save to persist it.
    /// </summary>
    /// <param name="previousFloor">The floor whose parameters serve as the base.</param>
    /// <param name="failThresholdDeltaPct">Multiplicative delta applied to failThreshold (e.g. 0.08 = +8%).</param>
    /// <param name="dayDurationDeltaPct">Multiplicative delta applied to dayDuration.</param>
    /// <param name="decayRateDeltaPct">Multiplicative delta applied to decayRatePerSecond.</param>
    /// <returns>New FloorDesignerData with index = previousFloor.floorIndex + 1.</returns>
    public static FloorDesignerData GenerateFromPrevious(
        FloorDesignerData previousFloor,
        float failThresholdDeltaPct,
        float dayDurationDeltaPct,
        float decayRateDeltaPct)
    {
        int newIndex = previousFloor.floorIndex + 1;

        FloorDesignerData newFloor = new FloorDesignerData
        {
            floorIndex          = newIndex,
            displayName         = "Floor " + (newIndex + 1),
            failThreshold       = previousFloor.failThreshold       * (1f + failThresholdDeltaPct),
            dayDuration         = previousFloor.dayDuration         * (1f + dayDurationDeltaPct),
            decayRatePerSecond  = previousFloor.decayRatePerSecond  * (1f + decayRateDeltaPct),
            rulesPerBin         = previousFloor.rulesPerBin,
            numberOfBins        = previousFloor.numberOfBins,
            maxRuleComplexity   = previousFloor.maxRuleComplexity,
            // Generated floor has not been saved yet — warn designer before any accidental close.
            isDirty             = true
        };

        // Build 5 empty nights using the floor's numberOfBins as the starting bin count.
        // The designer fills rules manually — generated floor is not saved until they click Save.
        newFloor.InitializeNights();

        return newFloor;
    }

    /// <summary>
    /// Returns true when the canonical save file for the given floor index exists on disk.
    /// Used by the reset action to decide whether to reload from disk or rebuild from base values.
    /// </summary>
    /// <param name="floorIndex">Zero-based floor index to check.</param>
    /// <returns>True if floor_N.json exists; false otherwise.</returns>
    public static bool FloorExists(int floorIndex)
    {
        string filePath = saveFolderPath + $"floor_{floorIndex}.json";
        return File.Exists(filePath);
    }

    /// <summary>
    /// Scans the save folder for all files matching the pattern "floor_N*.json" and
    /// returns their file names. Used by the window so the designer can see which
    /// versions already exist before deciding whether to overwrite or create a new version.
    /// </summary>
    /// <param name="floorIndex">Zero-based floor index to scan versions for.</param>
    /// <returns>List of matching file names (without directory path).</returns>
    public static List<string> GetExistingSaveVersions(int floorIndex)
    {
        List<string> foundFiles = new List<string>();

        if (!Directory.Exists(saveFolderPath))
            return foundFiles;

        string pattern = $"floor_{floorIndex}*.json";
        string[] matchingPaths = Directory.GetFiles(saveFolderPath, pattern);

        foreach (string fullPath in matchingPaths)
            foundFiles.Add(Path.GetFileName(fullPath));

        return foundFiles;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts a FloorDesignerData to the serialisable FloorSaveData format.
    /// Maps all matching fields and converts each NightDesignerData to a NightSaveData.
    /// Rules are no longer manually authored — the rules list is intentionally left empty
    /// because RuleGenerator creates rules at runtime using the saved parameters.
    /// </summary>
    private static FloorSaveData ConvertToSaveData(FloorDesignerData source)
    {
        FloorSaveData target = new FloorSaveData
        {
            floorIndex         = source.floorIndex,
            failThreshold      = source.failThreshold,
            dayDuration        = source.dayDuration,
            decayRatePerSecond = source.decayRatePerSecond,
            numberOfBins       = source.numberOfBins,
            rulesPerBin        = source.rulesPerBin,
            maxRuleComplexity  = source.maxRuleComplexity,
            isCompleted        = false,
            wasGenerated       = true,
            floorSeed          = string.Empty,
            specificities      = new List<string>(),
            // Rules are generated at runtime by RuleGenerator — never stored manually here.
            rules              = new List<SavedRuleData>(),
            nights             = new List<NightSaveData>()
        };

        // Persist all 5 nights with their parameter sets.
        // nightIndex, numberOfBins, rulesPerBin, maxRuleComplexity, and wasManuallyEdited
        // are all saved so the tool can restore the exact designer state on reload.
        foreach (NightDesignerData night in source.nights)
        {
            NightSaveData nightSave = new NightSaveData
            {
                nightIndex        = night.nightIndex,
                numberOfBins      = night.numberOfBins,
                rulesPerBin       = night.rulesPerBin,
                maxRuleComplexity = night.maxRuleComplexity,
                wasManuallyEdited = night.wasManuallyEdited
            };

            target.nights.Add(nightSave);
        }

        return target;
    }

    /// <summary>
    /// Converts a FloorSaveData (loaded from JSON) back into a FloorDesignerData
    /// so the designer can continue editing a previously saved floor.
    /// If the save file contains 5 NightSaveData entries they are restored directly.
    /// If not (old save format without nights) InitializeNights() is called to build
    /// a valid starting state — backward compatibility with floors saved before this update.
    /// </summary>
    private static FloorDesignerData ConvertToDesignerData(FloorSaveData source)
    {
        FloorDesignerData target = new FloorDesignerData
        {
            floorIndex         = source.floorIndex,
            displayName        = "Floor " + (source.floorIndex + 1),
            failThreshold      = source.failThreshold,
            dayDuration        = source.dayDuration,
            decayRatePerSecond = source.decayRatePerSecond,
            numberOfBins       = source.numberOfBins,
            rulesPerBin        = source.rulesPerBin,
            maxRuleComplexity  = source.maxRuleComplexity,
            isCompleted        = source.isCompleted,
            isDirty            = false
        };

        // Restore nights from save file when 5 complete night entries are present.
        // The count check guards against partial saves or saves from older tool versions.
        if (source.nights != null && source.nights.Count == 5)
        {
            foreach (NightSaveData nightSave in source.nights)
            {
                NightDesignerData night = new NightDesignerData
                {
                    nightIndex        = nightSave.nightIndex,
                    numberOfBins      = nightSave.numberOfBins,
                    rulesPerBin       = nightSave.rulesPerBin,
                    maxRuleComplexity = nightSave.maxRuleComplexity,
                    wasManuallyEdited = nightSave.wasManuallyEdited
                };

                target.nights.Add(night);
            }
        }
        else
        {
            // Old save format has no nights — initialise a valid 5-night structure
            // so the designer can immediately start configuring per-night parameters.
            target.InitializeNights();
        }

        return target;
    }


    /// <summary>
    /// Builds the full file path for a save operation.
    /// When overwrite is true: "floor_N.json".
    /// When overwrite is false: finds the next unused suffix starting at 2,
    /// so saves never silently replace an existing versioned file.
    /// </summary>
    private static string BuildSavePath(int floorIndex, bool overwrite)
    {
        if (overwrite)
            return saveFolderPath + $"floor_{floorIndex}.json";

        // Find next available versioned suffix — never overwrite existing saves
        // when the designer explicitly wants to keep both versions on disk.
        int suffix = 2;
        string candidatePath;

        do
        {
            candidatePath = saveFolderPath + $"floor_{floorIndex}_v{suffix}.json";
            suffix++;
        }
        while (File.Exists(candidatePath));

        return candidatePath;
    }

    /// <summary>Creates the save folder if it does not already exist.</summary>
    private static void EnsureSaveFolderExists()
    {
        if (!Directory.Exists(saveFolderPath))
            Directory.CreateDirectory(saveFolderPath);
    }
}
