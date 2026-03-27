using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores the visual identity of a document prefab.
/// Placed on each prefab in Prefabs_Document/Jour_X/ to describe its symbols,
/// feuille state, and writing type without relying on runtime generation.
/// </summary>
public class DocumentPrefabData : MonoBehaviour
{
    [Header("Identity")]
    public string prefabID;
    public int targetTube;

    [Header("Specificities")]
    public List<string> specificities = new List<string>();
}
