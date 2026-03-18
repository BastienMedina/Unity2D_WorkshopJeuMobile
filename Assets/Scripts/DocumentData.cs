using System;
using System.Collections.Generic;

/// <summary>
/// Pure data container representing a single document that can be sorted into a bin.
/// Holds the document's unique identifier and the list of specificities that describe it.
/// Does NOT contain any game logic, validation, UI references, or MonoBehaviour lifecycle.
/// </summary>
[Serializable]
public class DocumentData
{
    /// <summary>Unique identifier for this document instance (e.g. "doc_001").</summary>
    public string documentID = string.Empty;

    /// <summary>
    /// All specificities that describe this document (e.g. "red", "fragile", "heavy").
    /// Used by SortingBin.ValidateDocument to check rule conditions.
    /// </summary>
    public List<string> specificities = new List<string>();
}
