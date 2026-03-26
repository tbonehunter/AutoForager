// ModConfig.cs - Configuration model for Auto Forager.
// Holds forage category toggles, location region toggles, perk settings,
// multiplayer options, and hotkey bindings. Registered with GMCM in ModEntry.cs.
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace AutoForager;

/// <summary>
/// Mod configuration model. All properties map directly to GMCM controls.
/// </summary>
public class ModConfig
{
    // -------------------------------------------------------------------------
    // Forage type toggles
    // -------------------------------------------------------------------------

    /// <summary>Whether to collect ground forageables (Daffodils, Leeks, Clams, etc.)</summary>
    public bool CollectGroundForage { get; set; } = true;

    /// <summary>Whether to collect forage crops (Spring Onion, Ginger).</summary>
    public bool CollectForageCrops { get; set; } = true;

    /// <summary>Whether to collect from harvestable bushes (Salmonberries, Blackberries, Green Tea).</summary>
    public bool CollectBushes { get; set; } = true;

    // -------------------------------------------------------------------------
    // Perk integration
    // -------------------------------------------------------------------------

    /// <summary>Whether the player earns Foraging XP for auto-collected items.</summary>
    public bool GrantForagingXP { get; set; } = true;

    /// <summary>Whether the Botanist perk applies to auto-collected forage.</summary>
    public bool ApplyBotanist { get; set; } = true;

    /// <summary>Whether the Gatherer perk (double-harvest chance) applies.</summary>
    public bool ApplyGatherer { get; set; } = true;

    // -------------------------------------------------------------------------
    // Location region toggles
    // -------------------------------------------------------------------------

    public bool RegionFarm { get; set; } = true;
    public bool RegionTown { get; set; } = true;
    public bool RegionForest { get; set; } = true;
    public bool RegionBeach { get; set; } = true;
    public bool RegionMountain { get; set; } = true;
    public bool RegionRailroad { get; set; } = true;
    public bool RegionDesert { get; set; } = true;
    public bool RegionIsland { get; set; } = true;
    public bool RegionSewer { get; set; } = true;
    public bool RegionWoods { get; set; } = true;

    // -------------------------------------------------------------------------
    // Progression restrictions
    // -------------------------------------------------------------------------

    /// <summary>Enforce location progression restrictions (player must have unlocked the area).</summary>
    public bool EnforceProgressionRestrictions { get; set; } = true;

    // -------------------------------------------------------------------------
    // Multiplayer
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, all players share one forage pool (items collected once for all).
    /// When false, each player's machine collects independently.
    /// Default follows the shared money setting.
    /// </summary>
    public bool? SharedMachines { get; set; } = null; // null = follow money setting

    // -------------------------------------------------------------------------
    // Hotkeys
    // -------------------------------------------------------------------------

    /// <summary>Hotkey to display the last day's forage log.</summary>
    public KeybindList HotkeyViewLog { get; set; } = new KeybindList(SButton.F10);
}
