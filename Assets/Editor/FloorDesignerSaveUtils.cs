using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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
    /// Stored inside the project's Assets folder so the files are tracked by source control
    /// and available on any machine that clones the repository.
    /// </summary>
    private static string saveFolderPath => Path.GetFullPath("Assets/Editor/FloorDesigns") + Path.DirectorySeparatorChar;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the given FloorDesignerData to FloorSaveData and writes it to JSON.
    /// On the first save (bins are empty), auto-generates rules from the difficulty parameters
    /// using FloorDesignerRuleGen, so each bin starts with a coherent rule set derived from the
    /// numberOfBins, rulesPerBin, maxRuleComplexity, and pinnedSpecificities set by the designer.
    /// On subsequent saves, persists whatever rules the designer has manually edited.
    /// When overwrite is true, writes to "floor_N.json" (replaces existing).
    /// When overwrite is false, finds the next free versioned suffix — never overwrites.
    /// Sets isDirty = false and isSaved = true on the data if the write succeeds.
    /// </summary>
    /// <param name="designerData">The floor data to convert and save.</param>
    /// <param name="overwrite">True to replace the canonical floor_N.json; false to create a new version.</param>
    /// <param name="database">The SpecificityDatabase used for auto-generation on first save. May be null (skips gen).</param>
    public static void SaveFloor(FloorDesignerData designerData, bool overwrite,
                                 SpecificityDatabase database = null,
                                 List<RuleLibraryEntry> libraryEntries = null)
    {
        EnsureSaveFolderExists();

        // Auto-generate rules into empty bins before the first save so the designer
        // immediately sees a populated rule set to review and edit in the rule editor panel.
        AutoGenerateEmptyNights(designerData, database, libraryEntries);

        FloorSaveData saveData = ConvertToSaveData(designerData);
        string filePath = BuildSavePath(designerData.floorIndex, overwrite);

        try
        {
            string json = JsonUtility.ToJson(saveData, prettyPrint: true);
            File.WriteAllText(filePath, json);
            designerData.isDirty = false;
            // Mark as saved so the window switches to the rule editor panel on next repaint.
            designerData.isSaved = true;
            // Ensure every night has the correct number of bin entries after the first save.
            designerData.SyncBinsToNights();
            // Notify the AssetDatabase so the file appears in the Project window immediately.
            AssetDatabase.Refresh();
            Debug.Log($"[FloorDesigner] Floor saved successfully → {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FloorDesigner] Failed to save floor {designerData.floorIndex}: {exception.Message}");
        }
    }

    /// <summary>
    /// For each night whose bins are empty, generates rules from the Rule Library when entries
    /// are provided, otherwise falls back to procedural generation from the SpecificityDatabase.
    /// Nights that already have at least one bin with rules are left untouched —
    /// this prevents a re-save from overwriting the designer's manual edits.
    /// Does nothing when both <paramref name="libraryEntries"/> and <paramref name="database"/> are null.
    /// </summary>
    private static void AutoGenerateEmptyNights(FloorDesignerData floor, SpecificityDatabase database,
                                                List<RuleLibraryEntry> libraryEntries = null)
    {
        bool hasLibrary = libraryEntries != null && libraryEntries.Count > 0;

        if (!hasLibrary && database == null)
            return;

        foreach (NightDesignerData night in floor.nights)
        {
            // Skip nights that already have rules — preserve manual edits on re-save.
            bool hasAnyRule = false;
            foreach (BinDesignerData b in night.bins)
            {
                if (b.rules.Count > 0)
                {
                    hasAnyRule = true;
                    break;
                }
            }

            if (hasAnyRule)
                continue;

            // Night has no rules yet — generate from the library if available,
            // otherwise fall back to procedural generation from the SpecificityDatabase.
            List<BinDesignerData> generatedBins;

            if (hasLibrary)
            {
                generatedBins = FloorDesignerRuleGen.GenerateBinsFromLibrary(
                    night.numberOfBins,
                    night.rulesPerBin,
                    night.maxRuleComplexity,
                    libraryEntries);

                Debug.Log($"[FloorDesigner] Library-based rules generated for night {night.nightIndex + 1}: " +
                          $"{generatedBins.Count} bins, complexity ≤ {night.maxRuleComplexity}.");
            }
            else
            {
                generatedBins = FloorDesignerRuleGen.GenerateBinsForNight(
                    night.numberOfBins,
                    night.rulesPerBin,
                    night.maxRuleComplexity,
                    night.pinnedSpecificities,
                    database);

                Debug.Log($"[FloorDesigner] Procedural rules generated for night {night.nightIndex + 1}: " +
                          $"{generatedBins.Count} bins, complexity ≤ {night.maxRuleComplexity}.");
            }

            // Replace the (empty) bin list with the generated one.
            night.bins = generatedBins;
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
    /// Persists all 5 nights with their difficulty parameters and per-bin rule assignments.
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
            // Designer-authored floors are story-mode floors, not procedurally generated.
            // wasGenerated = false ensures GameManager routes completion saves through
            // FloorSaveSystem (JSON) instead of PlayerPrefs (procedural mode).
            wasGenerated       = false,
            floorSeed          = string.Empty,
            specificities      = new List<string>(),
            rules              = new List<SavedRuleData>(),
            nights             = new List<NightSaveData>()
        };

        foreach (NightDesignerData night in source.nights)
        {
            NightSaveData nightSave = new NightSaveData
            {
                nightIndex          = night.nightIndex,
                numberOfBins        = night.numberOfBins,
                rulesPerBin         = night.rulesPerBin,
                maxRuleComplexity   = night.maxRuleComplexity,
                wasManuallyEdited   = night.wasManuallyEdited,
                pinnedSpecificities = new List<string>(night.pinnedSpecificities),
                bins                = new List<BinSaveData>()
            };

            // Persist each bin's explicit rule assignments authored by the designer.
            foreach (BinDesignerData bin in night.bins)
            {
                BinSaveData binSave = new BinSaveData
                {
                    binIndex = bin.binIndex,
                    binID    = bin.binID,
                    rules    = new List<SavedRuleData>()
                };

                foreach (DesignerRuleEntry rule in bin.rules)
                {
                    binSave.rules.Add(new SavedRuleData
                    {
                        ruleType    = rule.ruleTypeString,
                        conditionA  = rule.conditionA,
                        conditionB  = rule.conditionB,
                        targetBinID = rule.targetBinID,
                        displayText = rule.displayText,
                        complexity  = rule.complexity,
                        isComplement = rule.isComplement
                    });
                }

                nightSave.bins.Add(binSave);
            }

            target.nights.Add(nightSave);
        }

        return target;
    }

    /// <summary>
    /// Converts a FloorSaveData (loaded from JSON) back into a FloorDesignerData
    /// so the designer can continue editing a previously saved floor.
    /// Restores per-bin rule assignments when they are present in the save file.
    /// Falls back to an empty bin structure for saves created before per-bin editing was introduced.
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
            // Any floor loaded from disk is considered saved — enables the rule editor panel.
            isSaved            = true,
            isDirty            = false
        };

        if (source.nights != null && source.nights.Count == 5)
        {
            foreach (NightSaveData nightSave in source.nights)
            {
                NightDesignerData night = new NightDesignerData
                {
                    nightIndex          = nightSave.nightIndex,
                    numberOfBins        = nightSave.numberOfBins,
                    rulesPerBin         = nightSave.rulesPerBin,
                    maxRuleComplexity   = nightSave.maxRuleComplexity,
                    wasManuallyEdited   = nightSave.wasManuallyEdited,
                    pinnedSpecificities = nightSave.pinnedSpecificities != null
                        ? new List<string>(nightSave.pinnedSpecificities)
                        : new List<string>()
                };

                // Restore per-bin rule assignments when the save file contains them.
                if (nightSave.bins != null && nightSave.bins.Count > 0)
                {
                    foreach (BinSaveData binSave in nightSave.bins)
                    {
                        BinDesignerData bin = new BinDesignerData
                        {
                            binIndex = binSave.binIndex,
                            binID    = binSave.binID
                        };

                        if (binSave.rules != null)
                        {
                            foreach (SavedRuleData savedRule in binSave.rules)
                            {
                                bin.rules.Add(new DesignerRuleEntry
                                {
                                    ruleTypeString = savedRule.ruleType,
                                    conditionA     = savedRule.conditionA,
                                    conditionB     = savedRule.conditionB,
                                    targetBinID    = savedRule.targetBinID,
                                    displayText    = savedRule.displayText,
                                    complexity     = savedRule.complexity,
                                    isComplement   = savedRule.isComplement
                                });
                            }
                        }

                        night.bins.Add(bin);
                    }
                }
                else
                {
                    // Old save format has no bin assignments — build empty bins for the night.
                    night.SyncBins(night.numberOfBins);
                }

                target.nights.Add(night);
            }
        }
        else
        {
            // Old save format has no nights — initialise a valid 5-night structure.
            target.InitializeNights();
            target.SyncBinsToNights();
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
