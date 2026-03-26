// Framework/RegionMapper.cs - Maps game location names to forage region keys.
// Used by the forage collector to determine which locations belong to which
// GMCM-configurable region toggle.
using System;
using System.Collections.Generic;

namespace AutoForager.Framework;

/// <summary>
/// Maps Stardew Valley location names to Auto Forager region keys.
/// Region keys correspond to ModConfig region toggles and GMCM settings.
/// </summary>
internal static class RegionMapper
{
    /// <summary>Known region assignments for vanilla outdoor locations.</summary>
    private static readonly Dictionary<string, string> LocationRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Farm & surroundings
        ["Farm"]          = "Farm",
        ["FarmCave"]      = "Farm",
        ["Greenhouse"]    = "Farm",
        ["FarmHouse"]     = "Farm",

        // Pelican Town
        ["Town"]          = "Town",

        // Forest
        ["Forest"]        = "Forest",
        ["Backwoods"]     = "Forest",

        // Secret Woods (separate toggle, sub-gated)
        ["Woods"]         = "Woods",

        // Beach
        ["Beach"]         = "Beach",

        // Mountain & Lake
        ["Mountain"]      = "Mountain",

        // Railroad & Bathhouse
        ["Railroad"]      = "Railroad",

        // Desert
        ["Desert"]        = "Desert",

        // Sewer
        ["Sewer"]         = "Sewer",

        // Ginger Island outdoor locations
        ["IslandSouth"]      = "Island",
        ["IslandSouthEast"]  = "Island",
        ["IslandEast"]       = "Island",
        ["IslandWest"]       = "Island",
        ["IslandNorth"]      = "Island",
        ["IslandFarmHouse"]  = "Island",
        ["IslandFarmCave"]   = "Island",
        ["IslandShrine"]     = "Island",
    };

    /// <summary>
    /// Returns the region key for the given location name.
    /// Unknown locations return null (they will be skipped unless a region match can be inferred).
    /// </summary>
    public static string? GetRegion(string locationName)
    {
        if (LocationRegions.TryGetValue(locationName, out string? region))
            return region;

        // Infer Ginger Island for any location starting with "Island"
        if (locationName.StartsWith("Island", StringComparison.OrdinalIgnoreCase))
            return "Island";

        // Unknown locations from content mods — allow them through (they'll be
        // treated as unrestricted and toggled on by default).
        return null;
    }

    /// <summary>
    /// Checks if a region is enabled in the given config.
    /// Unknown regions (null) are always enabled so modded locations are collected.
    /// </summary>
    public static bool IsRegionEnabled(string? regionKey, ModConfig config)
    {
        if (regionKey == null)
            return true;

        return regionKey switch
        {
            "Farm"     => config.RegionFarm,
            "Town"     => config.RegionTown,
            "Forest"   => config.RegionForest,
            "Beach"    => config.RegionBeach,
            "Mountain" => config.RegionMountain,
            "Railroad" => config.RegionRailroad,
            "Desert"   => config.RegionDesert,
            "Island"   => config.RegionIsland,
            "Sewer"    => config.RegionSewer,
            "Woods"    => config.RegionWoods,
            _          => true
        };
    }
}
