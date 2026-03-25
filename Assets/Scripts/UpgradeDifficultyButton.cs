using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bouton de test pour déclencher manuellement un upgrade de complexité des règles.
/// Récupère les bins actifs via <see cref="BinLayoutManager"/>, invoque
/// <see cref="RuleComplexityUpgrader.TryUpgradeBins"/> puis reconstruit le pool
/// du spawner via <see cref="LibraryDocumentSpawner.RebuildPool"/>.
///
/// Destiné uniquement aux scènes de test — ne pas intégrer dans le build final.
/// </summary>
[RequireComponent(typeof(Button))]
public class UpgradeDifficultyButton : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [SerializeField] private RuleComplexityUpgrader ruleComplexityUpgrader;
    [SerializeField] private BinLayoutManager binLayoutManager;
    [SerializeField] private LibraryDocumentSpawner documentSpawner;

    /// <summary>Niveau de complexité cible pour cet upgrade.</summary>
    [SerializeField] private int targetComplexity = 2;

    /// <summary>Label du bouton mis à jour après l'upgrade pour indiquer l'état.</summary>
    [SerializeField] private TextMeshProUGUI buttonLabel;

    // ─── Constants ────────────────────────────────────────────────────────────────

    private const string DefaultLabel    = "Upgrade difficulté";
    private const string UpgradedLabel   = "Difficulté upgradée !";
    private const string NoUpgraderLabel = "Upgrader manquant";

    // ─── Unity lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnUpgradeClicked);

        if (buttonLabel != null)
            buttonLabel.text = DefaultLabel;
    }

    // ─── Handler ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Déclenché par le clic sur le bouton.
    /// Exécute l'upgrade de complexité sur tous les bins actifs,
    /// puis reconstruit le pool du spawner.
    /// </summary>
    private void OnUpgradeClicked()
    {
        if (ruleComplexityUpgrader == null)
        {
            Debug.LogWarning("[UpgradeDifficultyButton] RuleComplexityUpgrader non assigné.");
            if (buttonLabel != null) buttonLabel.text = NoUpgraderLabel;
            return;
        }

        if (binLayoutManager == null)
        {
            Debug.LogWarning("[UpgradeDifficultyButton] BinLayoutManager non assigné.");
            return;
        }

        List<SortingBin> activeBins = binLayoutManager.GetActiveBins();
        ruleComplexityUpgrader.TryUpgradeBins(activeBins, targetComplexity);

        // Reconstruit le pool du spawner avec les règles upgradées.
        if (documentSpawner != null)
        {
            List<RuleData> upgradedRules = new List<RuleData>();
            foreach (SortingBin bin in activeBins)
                upgradedRules.AddRange(bin.GetAssignedRules());

            documentSpawner.RebuildPool(upgradedRules);
        }

        if (buttonLabel != null)
            buttonLabel.text = UpgradedLabel;

        Debug.Log($"[UpgradeDifficultyButton] Upgrade déclenché — complexité cible : {targetComplexity}.");
    }
}
