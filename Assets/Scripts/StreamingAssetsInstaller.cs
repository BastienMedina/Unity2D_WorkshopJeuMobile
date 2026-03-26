using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Copies floor_N.json files from StreamingAssets/FloorDesigns/ to
/// Application.persistentDataPath/floors/ at startup using UnityWebRequest.
///
/// This step is mandatory on Android because StreamingAssets are packed inside the APK
/// (a ZIP archive) and cannot be read by System.IO (File.ReadAllText, Directory.GetFiles).
/// UnityWebRequest is the only cross-platform API that can access them on all platforms.
///
/// On other platforms (Editor, iOS, Standalone) the copy is still performed to keep
/// the code path identical and avoid per-platform branches in FloorSaveSystem.
///
/// Usage: add this component to the same GameObject as FloorSaveSystemBootstrap in
/// Menu_Principal. FloorButtonSpawner must wait for IsReady before calling SpawnButtons().
/// </summary>
public class StreamingAssetsInstaller : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------

    /// <summary>
    /// True once all StreamingAssets floors have been copied (or skipped because they
    /// already exist) and FloorSaveSystem is safe to read from persistentDataPath.
    /// FloorButtonSpawner polls this before calling SpawnButtons().
    /// </summary>
    public static bool IsReady { get; private set; }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string StreamingSubFolder = "FloorDesigns";
    private const string DestSubFolder      = "/floors/";
    private const string FilePrefix         = "floor_";
    private const string FileExtension      = ".json";

    /// <summary>
    /// Maximum floor index to probe. We stop at the first missing index so this only
    /// needs to be an upper bound, not the exact count.
    /// </summary>
    private const int MaxFloorIndex = 99;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        IsReady = false;
    }

    private void Start()
    {
        StartCoroutine(CopyStreamingAssetsFloors());
    }

    // -------------------------------------------------------------------------
    // Core routine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Iterates floor_0.json … floor_N.json inside StreamingAssets/FloorDesigns/,
    /// stopping at the first index that does not exist. Each file is fetched via
    /// UnityWebRequest and written to persistentDataPath/floors/ only when the
    /// destination does not already contain a newer (or identical) file.
    ///
    /// Always copies the StreamingAssets version so designer edits in a new build
    /// overwrite the cached copy in persistentDataPath.
    /// Sets <see cref="IsReady"/> to true when finished regardless of success/failure
    /// so the game is never permanently blocked.
    /// </summary>
    private IEnumerator CopyStreamingAssetsFloors()
    {
        string destFolder = Application.persistentDataPath + DestSubFolder;

        // Ensure destination folder exists (no-op if already present).
        Directory.CreateDirectory(destFolder);

        for (int i = 0; i <= MaxFloorIndex; i++)
        {
            string fileName   = FilePrefix + i + FileExtension;
            string sourceUrl  = Path.Combine(Application.streamingAssetsPath, StreamingSubFolder, fileName);

            // UnityWebRequest requires a URI. On Android streamingAssetsPath already starts with
            // "jar:file://" so no prefix is needed. On other platforms we add "file://".
#if !UNITY_ANDROID || UNITY_EDITOR
            if (!sourceUrl.StartsWith("http") && !sourceUrl.StartsWith("jar:"))
                sourceUrl = "file://" + sourceUrl;
#endif

            using UnityWebRequest request = UnityWebRequest.Get(sourceUrl);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 404 / connection error on the first missing file means we've reached the end.
                Debug.Log($"[StreamingAssetsInstaller] No file at index {i} — stopping scan. ({request.error})");
                break;
            }

            string destPath = destFolder + fileName;
            string json     = request.downloadHandler.text;

            // Always overwrite so a new build with updated designer floors replaces the old cache.
            File.WriteAllText(destPath, json);
            Debug.Log($"[StreamingAssetsInstaller] Copied {fileName} → {destPath}");
        }

        Debug.Log("[StreamingAssetsInstaller] Done. FloorSaveSystem is now ready.");
        IsReady = true;
    }
}
