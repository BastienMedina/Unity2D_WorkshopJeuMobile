using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor-only tool that generates the 57 document prefabs for Jour_1 under
/// Assets/Resources/Prefabs_Document/Jour_1/.
///
/// Each prefab is a standalone (non-variant) copy of "DocumentPrefab 1" with its Image
/// layers pre-configured:
///   Layer_Feuille   → feuille sprite
///   Layer_Symbol_1  → 1st symbol (pastille or tampon)
///   Layer_Symbol_2  → 2nd symbol (or null)
///   Layer_Symbol_3  → 3rd symbol (or null)
///   Layer_Texte     → texte sprite (null = layer disabled for text_vierge)
///
/// Run via Tools > Generate Jour 1 Document Prefabs.
/// </summary>
public static class PrefabDocumentGenerator
{
    // ─── Paths ────────────────────────────────────────────────────────────────

    private const string SourcePrefabPath      = "Assets/Prefabs/DocumentPrefab 1.prefab";
    private const string RootResourcesFolder   = "Assets/Resources";
    private const string PrefabsDocumentFolder = "Assets/Resources/Prefabs_Document";
    private const string OutputFolder          = "Assets/Resources/Prefabs_Document/Jour_1";

    // ─── Layer names (children of the root GO) ────────────────────────────────

    private const string LayerFeuille  = "Layer_Feuille";
    private const string LayerTexte    = "Layer_Texte";
    private const string LayerSymbol1  = "Layer_Symbol_1";
    private const string LayerSymbol2  = "Layer_Symbol_2";
    private const string LayerSymbol3  = "Layer_Symbol_3";

    // ─── Sprite GUIDs — Feuille ───────────────────────────────────────────────

    private const string Feuille_Base            = "2cd6ff90c077801449076d12d1fb1ef7";
    private const string Feuille_Brulee          = "2951b869b0196344a9078908f8af6491";
    private const string Feuille_Cafe            = "06d302ce0b944e54593fa1e748cb9af7";
    private const string Feuille_DechirureGrande = "3fc8e9c1f26c3994eae1561efccbbbb6";
    private const string Feuille_DechirurePetite = "52846cae2cb5e0e4fbfa08618c624b93";

    // ─── Sprite GUIDs — Tampon ────────────────────────────────────────────────

    private const string Tampon_Carre     = "8803ff86201cf6944ba1ef6a449d29d9";
    private const string Tampon_Double    = "5f722a6e22302364bad45835f4d3145f";
    private const string Tampon_Efface    = "68550ace37d47b540811f4bb578d1f06";
    private const string Tampon_Rectangle = "8b926852b34f8e84ab0faf3f989b83bb";
    private const string Tampon_Rond      = "b00e2c0ef45bb854a96c9ce291c63539";

    // ─── Sprite GUIDs — Texte ─────────────────────────────────────────────────

    private const string Texte_Dessin  = "62cd3ac4d87efe14d87e6e1a7900529a";
    private const string Texte_Machine = "6be561a55f02b8b4d8142c73fd30d588";
    private const string Texte_Main    = "ab26d1cda6cd1ec44bf1678852d00bde";
    private const string Texte_Tableau = "f8eb702076306174e87155ae9dcdfb57";
    private const string Texte_Vierge  = null; // null → Image disabled

    // ─── Sprite GUIDs — Signature ─────────────────────────────────────────────

    private const string Sig_Coloree = "e2383054ff8b6594bacbfdd6f204e9ec";
    private const string Sig_Effacee = "0e905d9b4812a1c4ab911b550c39295e";
    private const string Sig_Grande  = "9075dcfb69307704fbe45045e5260d74";
    private const string Sig_Main    = "46a0840ca0e02cd49941ce7ea708017d";
    private const string Sig_Petite  = "1aa6aa325a5b9d540989ff1ef4363bf2";

    // ─── Sprite GUIDs — Pastille ──────────────────────────────────────────────

    private const string Past_Bleu   = "4a589a230e8715845a45c3aed51dc6ac";
    private const string Past_Jaune  = "3489f830d31b93f4cb57a1baeaa216c3";
    private const string Past_Rouge  = "c1278904c02f1474e9964a1084f5e699";
    private const string Past_Vert   = "31a141f23786a4d4a99c1f674df68bdf";
    private const string Past_Violet = "2670db8758d98c447aaec2cbe9dc1199";

    // ─── Document config ──────────────────────────────────────────────────────

    /// <summary>
    /// Visual config for one document prefab.
    /// sym1/sym2/sym3 map to Layer_Symbol_1/2/3 — null hides the layer.
    /// texte null disables Layer_Texte (text_vierge).
    /// </summary>
    private struct DocConfig
    {
        public string feuille;
        public string sym1;
        public string sym2;
        public string sym3;
        public string texte;

        public DocConfig(string feuille, string sym1, string sym2, string sym3, string texte)
        {
            this.feuille = feuille;
            this.sym1    = sym1;
            this.sym2    = sym2;
            this.sym3    = sym3;
            this.texte   = texte;
        }
    }

    // ─── Entry point ──────────────────────────────────────────────────────────

    [MenuItem("Tools/Generate Jour 1 Document Prefabs")]
    public static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(RootResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(PrefabsDocumentFolder))
            AssetDatabase.CreateFolder(RootResourcesFolder, "Prefabs_Document");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder(PrefabsDocumentFolder, "Jour_1");

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (sourcePrefab == null)
        {
            Debug.LogError($"[PrefabDocumentGenerator] Source prefab not found at '{SourcePrefabPath}'.");
            return;
        }

        Dictionary<string, DocConfig> configs = BuildConfigs();

        // Pre-load ALL sprites before any batch editing so AssetDatabase isn't blocked.
        Dictionary<string, Sprite> spriteCache = PreloadSprites(configs);

        int created = 0;
        int skipped = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (KeyValuePair<string, DocConfig> entry in configs)
            {
                string    prefabName = entry.Key;
                DocConfig cfg        = entry.Value;
                string    destPath   = $"{OutputFolder}/{prefabName}.prefab";

                // Use Object.Instantiate (not PrefabUtility) to get a plain GO with no
                // prefab connection — SaveAsPrefabAsset will then produce a standalone prefab.
                GameObject instance = Object.Instantiate(sourcePrefab);
                instance.name = prefabName;

                ApplySprites(instance, cfg, spriteCache);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(instance, destPath, out success);
                Object.DestroyImmediate(instance);

                if (success) created++;
                else
                {
                    skipped++;
                    Debug.LogWarning($"[PrefabDocumentGenerator] Failed to save '{prefabName}.prefab'.");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[PrefabDocumentGenerator] Done. Created: {created} — Skipped/Failed: {skipped}.");
        EditorUtility.DisplayDialog(
            "Prefab Generation Complete",
            $"Created {created} prefabs in:\n{OutputFolder}\n\nSkipped/Failed: {skipped}",
            "OK");
    }

    // ─── Sprite pre-loading ───────────────────────────────────────────────────

    /// <summary>
    /// Loads every distinct GUID referenced across all configs into a dictionary
    /// BEFORE StartAssetEditing is called, so the cache is fully populated when
    /// the batch write loop runs.
    /// </summary>
    private static Dictionary<string, Sprite> PreloadSprites(Dictionary<string, DocConfig> configs)
    {
        HashSet<string> guids = new HashSet<string>();
        foreach (DocConfig cfg in configs.Values)
        {
            if (cfg.feuille != null) guids.Add(cfg.feuille);
            if (cfg.sym1    != null) guids.Add(cfg.sym1);
            if (cfg.sym2    != null) guids.Add(cfg.sym2);
            if (cfg.sym3    != null) guids.Add(cfg.sym3);
            if (cfg.texte   != null) guids.Add(cfg.texte);
        }

        Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        foreach (string guid in guids)
        {
            Sprite sprite = LoadSpriteByGuid(guid);
            if (sprite != null)
                cache[guid] = sprite;
            else
                Debug.LogWarning($"[PrefabDocumentGenerator] Could not load sprite for GUID '{guid}'.");
        }

        return cache;
    }

    /// <summary>
    /// Loads a Sprite by GUID. Tries direct load first, then iterates sub-assets.
    /// </summary>
    private static Sprite LoadSpriteByGuid(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;

        Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (direct != null) return direct;

        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            if (obj is Sprite s) return s;

        return null;
    }

    // ─── Sprite application ───────────────────────────────────────────────────

    private static void ApplySprites(GameObject root, DocConfig cfg, Dictionary<string, Sprite> cache)
    {
        SetLayer(root, LayerFeuille,  cfg.feuille, cache);
        SetLayer(root, LayerTexte,    cfg.texte,   cache);
        SetLayer(root, LayerSymbol1,  cfg.sym1,    cache);
        SetLayer(root, LayerSymbol2,  cfg.sym2,    cache);
        SetLayer(root, LayerSymbol3,  cfg.sym3,    cache);
    }

    private static void SetLayer(GameObject root, string childName, string guid, Dictionary<string, Sprite> cache)
    {
        Transform child = root.transform.Find(childName);
        if (child == null)
        {
            Debug.LogWarning($"[PrefabDocumentGenerator] Child '{childName}' not found on '{root.name}'.");
            return;
        }

        Image image = child.GetComponent<Image>();
        if (image == null)
        {
            Debug.LogWarning($"[PrefabDocumentGenerator] No Image on '{childName}' in '{root.name}'.");
            return;
        }

        if (string.IsNullOrEmpty(guid))
        {
            image.sprite  = null;
            image.enabled = false;
            return;
        }

        if (cache.TryGetValue(guid, out Sprite sprite))
        {
            image.sprite  = sprite;
            image.enabled = true;
        }
        else
        {
            Debug.LogWarning($"[PrefabDocumentGenerator] Sprite GUID '{guid}' missing from cache on '{root.name}/{childName}'.");
        }
    }

    // ─── Config table ─────────────────────────────────────────────────────────

    /// <summary>
    /// Full visual spec for the 57 Jour_1 document prefabs.
    ///
    /// Symbol slots → Layer mapping:
    ///   sym1 = Layer_Symbol_1 (always the primary pastille or tampon)
    ///   sym2 = Layer_Symbol_2 (second symbol if any)
    ///   sym3 = Layer_Symbol_3 (third symbol — signature always last)
    ///
    /// "Tampon bleu" = tampon_double per project spec.
    /// </summary>
    private static Dictionary<string, DocConfig> BuildConfigs()
    {
        // DocConfig(feuille, sym1, sym2, sym3, texte)
        return new Dictionary<string, DocConfig>
        {
            // ── Tuyau 1 ──────────────────────────────────────────────────────
            ["E1J5_T1_01"] = new DocConfig(Feuille_Base,            Past_Vert,   null,        null,        Texte_Main),
            ["E1J5_T1_02"] = new DocConfig(Feuille_Base,            Past_Vert,   Sig_Grande,  null,        Texte_Machine),
            ["E1J5_T1_03"] = new DocConfig(Feuille_DechirurePetite, Past_Vert,   Sig_Petite,  null,        Texte_Main),
            ["E1J5_T1_04"] = new DocConfig(Feuille_Brulee,          Past_Vert,   Sig_Main,    null,        Texte_Dessin),
            ["E1J5_T1_05"] = new DocConfig(Feuille_Cafe,            Past_Vert,   Sig_Coloree, null,        Texte_Tableau),
            ["E1J5_T1_06"] = new DocConfig(Feuille_DechirureGrande, Past_Vert,   Sig_Effacee, null,        Texte_Vierge),
            ["E1J5_T1_07"] = new DocConfig(Feuille_Base,            Past_Vert,   Past_Vert,   null,        Texte_Machine),
            ["E1J5_T1_08"] = new DocConfig(Feuille_Base,            Past_Vert,   Past_Bleu,   null,        Texte_Main),
            ["E1J5_T1_09"] = new DocConfig(Feuille_Brulee,          Past_Vert,   Past_Violet, null,        Texte_Dessin),
            ["E1J5_T1_10"] = new DocConfig(Feuille_Cafe,            Past_Vert,   Past_Rouge,  null,        Texte_Machine),
            ["E1J5_T1_11"] = new DocConfig(Feuille_DechirurePetite, Past_Vert,   Past_Jaune,  null,        Texte_Tableau),
            ["E1J5_T1_12"] = new DocConfig(Feuille_Base,            Past_Vert,   Past_Bleu,   Sig_Grande,  Texte_Main),
            ["E1J5_T1_13"] = new DocConfig(Feuille_DechirureGrande, Past_Vert,   Past_Violet, Sig_Petite,  Texte_Machine),
            ["E1J5_T1_14"] = new DocConfig(Feuille_Cafe,            Past_Vert,   Past_Rouge,  Sig_Main,    Texte_Dessin),

            // ── Tuyau 2 ──────────────────────────────────────────────────────
            ["E1J5_T2_01"] = new DocConfig(Feuille_Base,            Past_Vert,   Tampon_Carre,     null,        Texte_Machine),
            ["E1J5_T2_02"] = new DocConfig(Feuille_Base,            Past_Vert,   Tampon_Rond,      Sig_Grande,  Texte_Main),
            ["E1J5_T2_03"] = new DocConfig(Feuille_DechirurePetite, Past_Vert,   Tampon_Rectangle, Sig_Petite,  Texte_Dessin),
            ["E1J5_T2_04"] = new DocConfig(Feuille_Brulee,          Past_Vert,   Tampon_Efface,    Sig_Coloree, Texte_Machine),
            ["E1J5_T2_05"] = new DocConfig(Feuille_Cafe,            Past_Vert,   Tampon_Double,    Sig_Effacee, Texte_Tableau),
            ["E1J5_T2_06"] = new DocConfig(Feuille_Base,            Past_Vert,   Tampon_Carre,     Past_Bleu,   Texte_Main),
            ["E1J5_T2_07"] = new DocConfig(Feuille_DechirureGrande, Past_Vert,   Tampon_Rond,      Past_Violet, Texte_Vierge),
            ["E1J5_T2_08"] = new DocConfig(Feuille_Brulee,          Past_Vert,   Tampon_Rectangle, Past_Rouge,  Texte_Machine),
            ["E1J5_T2_09"] = new DocConfig(Feuille_Base,            Past_Bleu,   null,             null,        Texte_Main),
            ["E1J5_T2_10"] = new DocConfig(Feuille_Base,            Past_Bleu,   Sig_Grande,       null,        Texte_Machine),
            ["E1J5_T2_11"] = new DocConfig(Feuille_DechirurePetite, Past_Bleu,   Sig_Petite,       null,        Texte_Dessin),
            ["E1J5_T2_12"] = new DocConfig(Feuille_Brulee,          Past_Bleu,   Sig_Main,         null,        Texte_Main),
            ["E1J5_T2_13"] = new DocConfig(Feuille_Cafe,            Past_Bleu,   Sig_Coloree,      null,        Texte_Tableau),
            ["E1J5_T2_14"] = new DocConfig(Feuille_Base,            Past_Bleu,   Past_Violet,      null,        Texte_Machine),
            ["E1J5_T2_15"] = new DocConfig(Feuille_DechirureGrande, Past_Bleu,   Past_Rouge,       Sig_Grande,  Texte_Vierge),
            ["E1J5_T2_16"] = new DocConfig(Feuille_Base,            Past_Bleu,   Tampon_Carre,     null,        Texte_Main),
            ["E1J5_T2_17"] = new DocConfig(Feuille_Cafe,            Past_Bleu,   Tampon_Rond,      Sig_Petite,  Texte_Dessin),

            // ── Tuyau 3 ──────────────────────────────────────────────────────
            ["E1J5_T3_01"] = new DocConfig(Feuille_Base,            Tampon_Double, null,        null,        Texte_Main),
            ["E1J5_T3_02"] = new DocConfig(Feuille_Base,            Tampon_Double, Past_Vert,   null,        Texte_Machine),
            ["E1J5_T3_03"] = new DocConfig(Feuille_DechirurePetite, Tampon_Double, Past_Bleu,   null,        Texte_Dessin),
            ["E1J5_T3_04"] = new DocConfig(Feuille_Brulee,          Tampon_Double, Past_Rouge,  null,        Texte_Main),
            ["E1J5_T3_05"] = new DocConfig(Feuille_Cafe,            Tampon_Double, Past_Violet, null,        Texte_Machine),
            ["E1J5_T3_06"] = new DocConfig(Feuille_Base,            Tampon_Double, Sig_Grande,  null,        Texte_Tableau),
            ["E1J5_T3_07"] = new DocConfig(Feuille_DechirureGrande, Tampon_Double, Past_Vert,   Sig_Grande,  Texte_Vierge),
            ["E1J5_T3_08"] = new DocConfig(Feuille_Base,            Tampon_Double, Past_Bleu,   Sig_Petite,  Texte_Main),
            ["E1J5_T3_09"] = new DocConfig(Feuille_Brulee,          Tampon_Double, Past_Rouge,  Sig_Main,    Texte_Machine),
            ["E1J5_T3_10"] = new DocConfig(Feuille_Base,            Past_Violet,   null,        null,        Texte_Main),
            ["E1J5_T3_11"] = new DocConfig(Feuille_Base,            Past_Violet,   Sig_Grande,  null,        Texte_Machine),
            ["E1J5_T3_12"] = new DocConfig(Feuille_DechirurePetite, Past_Violet,   Sig_Petite,  null,        Texte_Dessin),
            ["E1J5_T3_13"] = new DocConfig(Feuille_Brulee,          Past_Violet,   Sig_Main,    null,        Texte_Main),
            ["E1J5_T3_14"] = new DocConfig(Feuille_Cafe,            Past_Violet,   Sig_Coloree, null,        Texte_Tableau),
            ["E1J5_T3_15"] = new DocConfig(Feuille_Base,            Past_Violet,   Past_Rouge,  null,        Texte_Vierge),
            ["E1J5_T3_16"] = new DocConfig(Feuille_DechirureGrande, Past_Violet,   Past_Jaune,  Sig_Grande,  Texte_Machine),

            // ── Poubelle ─────────────────────────────────────────────────────
            ["E1J5_P_01"] = new DocConfig(Feuille_Base,            Past_Rouge,  null,        null,        Texte_Main),
            ["E1J5_P_02"] = new DocConfig(Feuille_DechirurePetite, Past_Rouge,  Sig_Grande,  null,        Texte_Machine),
            ["E1J5_P_03"] = new DocConfig(Feuille_Brulee,          Past_Rouge,  Past_Jaune,  null,        Texte_Dessin),
            ["E1J5_P_04"] = new DocConfig(Feuille_Cafe,            Past_Jaune,  null,        null,        Texte_Main),
            ["E1J5_P_05"] = new DocConfig(Feuille_Base,            Past_Jaune,  Sig_Petite,  null,        Texte_Tableau),
            ["E1J5_P_06"] = new DocConfig(Feuille_DechirureGrande, Past_Jaune,  Past_Rouge,  null,        Texte_Machine),
            ["E1J5_P_07"] = new DocConfig(Feuille_Base,            null,        null,        null,        Texte_Main),
            ["E1J5_P_08"] = new DocConfig(Feuille_Brulee,          Sig_Grande,  null,        null,        Texte_Dessin),
            ["E1J5_P_09"] = new DocConfig(Feuille_Cafe,            Tampon_Carre,Past_Rouge,  Sig_Grande,  Texte_Machine),
            ["E1J5_P_10"] = new DocConfig(Feuille_Base,            Tampon_Rond, Past_Jaune,  Sig_Petite,  Texte_Vierge),
        };
    }
}
