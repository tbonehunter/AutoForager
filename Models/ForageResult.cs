// Models/ForageResult.cs - Data models for Auto Forager collection results.
// Tracks what was collected, from where, and stores the daily log.
using System.Collections.Generic;

namespace AutoForager.Models;

/// <summary>
/// A single foraged item collected by the Auto Forager machine.
/// </summary>
public class CollectedForageItem
{
    /// <summary>The qualified item ID (e.g., "(O)18" for Daffodil).</summary>
    public string QualifiedItemId { get; set; } = "";

    /// <summary>The unqualified item ID.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Display name of the item.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Stack count (1, or 2 if Gatherer perk doubled).</summary>
    public int Stack { get; set; } = 1;

    /// <summary>Item quality (0=Normal, 1=Silver, 2=Gold, 4=Iridium).</summary>
    public int Quality { get; set; }

    /// <summary>The location the item was collected from.</summary>
    public string LocationName { get; set; } = "";

    /// <summary>Source type: "Ground", "ForageCrop", or "Bush".</summary>
    public string SourceType { get; set; } = "";
}

/// <summary>
/// Summary of a single day's forage collection, used for the daily log popup.
/// </summary>
public class DailyForageLog
{
    /// <summary>Game day this log represents.</summary>
    public int Day { get; set; }

    /// <summary>Season of this log.</summary>
    public string Season { get; set; } = "";

    /// <summary>Year of this log.</summary>
    public int Year { get; set; }

    /// <summary>Total items collected.</summary>
    public int TotalItems { get; set; }

    /// <summary>Number of locations visited.</summary>
    public int LocationsVisited { get; set; }

    /// <summary>Itemized list of what was collected.</summary>
    public List<CollectedForageItem> Items { get; set; } = new();
}
