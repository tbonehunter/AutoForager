// Framework/ForageCollector.cs - Core forage collection engine.
// Called at DayStarted to scan all eligible locations, harvest forageables,
// and deposit them into the Auto Forager machine's internal inventory.
// Queries game state directly to identify forageable items.
using System;
using System.Collections.Generic;
using System.Linq;
using AutoForager.Content;
using AutoForager.Models;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace AutoForager.Framework;

/// <summary>
/// Scans game locations and collects forageable items into machine inventories.
/// Handles perk application (Botanist, Gatherer), quality upgrades for Heavy
/// machines, and capacity limits.
/// </summary>
public class ForageCollector
{
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly ModConfig _config;

    // Profession IDs
    private const int GathererProfession = 13;
    private const int BotanistProfession = 16;

    // XP per forage item collected
    private const int ForageXPPerItem = 7;

    public ForageCollector(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        _helper  = helper;
        _monitor = monitor;
        _config  = config;
    }

    /// <summary>
    /// Runs the daily collection cycle for all placed Auto Forager machines.
    /// Returns the daily log of collected items.
    /// </summary>
    /// <param name="machines">All placed Auto Forager machines (normal and heavy).</param>
    public DailyForageLog CollectAll(List<MachineInfo> machines)
    {
        var log = new DailyForageLog
        {
            Day    = Game1.dayOfMonth,
            Season = Game1.currentSeason,
            Year   = Game1.year
        };

        if (machines.Count == 0)
            return log;

        bool enforceRestrictions = _config.EnforceProgressionRestrictions;

        // Determine which locations to scan based on config and restrictions
        var eligibleLocations = GetEligibleLocations(machines, enforceRestrictions);
        log.LocationsVisited = eligibleLocations.Count;

        _monitor.Log(
            _helper.Translation.Get("log.collecting", new { count = eligibleLocations.Count }),
            LogLevel.Debug);

        // Process each machine in placement order
        foreach (var machine in machines)
        {
            if (machine.RemainingSlots <= 0)
                continue;

            CollectForMachine(machine, eligibleLocations, log, enforceRestrictions);
        }

        log.TotalItems = log.Items.Sum(i => i.Stack);

        _monitor.Log(
            _helper.Translation.Get("log.collected",
                new { count = log.TotalItems, locations = log.LocationsVisited }),
            LogLevel.Info);

        return log;
    }

    // -------------------------------------------------------------------------
    // Collection for a single machine
    // -------------------------------------------------------------------------

    private void CollectForMachine(
        MachineInfo machine,
        List<GameLocation> locations,
        DailyForageLog log,
        bool enforceRestrictions)
    {
        var player = Game1.player;
        bool hasBotanist  = _config.ApplyBotanist && player.professions.Contains(BotanistProfession);
        bool hasGatherer  = _config.ApplyGatherer && player.professions.Contains(GathererProfession);
        bool isHeavy      = machine.IsHeavy;
        bool machineOnIsland = IsGingerIslandLocation(machine.HomeLocationName);

        foreach (var location in locations)
        {
            if (machine.RemainingSlots <= 0)
            {
                _monitor.Log(
                    _helper.Translation.Get("log.machine.full", new { count = machine.MaxSlots }),
                    LogLevel.Debug);
                break;
            }

            // Island machines collect only island locations; mainland machines skip island
            bool locationOnIsland = IsGingerIslandLocation(location.Name);
            if (machineOnIsland != locationOnIsland)
                continue;

            // Ground forageables
            if (_config.CollectGroundForage)
                CollectGroundForage(location, machine, log, hasBotanist, hasGatherer, isHeavy);

            // Forage crops
            if (_config.CollectForageCrops)
                CollectForageCrops(location, machine, log, hasBotanist, hasGatherer, isHeavy);

            // Harvestable bushes
            if (_config.CollectBushes)
                CollectBushes(location, machine, log, hasBotanist, hasGatherer, isHeavy);

            // Mussel stones (IslandWest beach only)
            if (_config.CollectGroundForage
                && location.Name.Equals("IslandWest", StringComparison.OrdinalIgnoreCase))
                CollectMusselStones(location, machine, log, hasBotanist, hasGatherer, isHeavy);
        }
    }

    // -------------------------------------------------------------------------
    // Ground forageables
    // -------------------------------------------------------------------------

    private void CollectGroundForage(
        GameLocation location,
        MachineInfo machine,
        DailyForageLog log,
        bool hasBotanist,
        bool hasGatherer,
        bool isHeavy)
    {
        // Collect keys first to avoid modifying collection during iteration
        var forageKeys = new List<Vector2>();
        foreach (var kvp in location.Objects.Pairs)
        {
            if (kvp.Value is StardewValley.Object obj && IsForageable(obj))
                forageKeys.Add(kvp.Key);
        }

        foreach (var tile in forageKeys)
        {
            if (machine.RemainingSlots <= 0)
                break;

            if (!location.Objects.TryGetValue(tile, out var obj))
                continue;

            int quality = DetermineQuality(obj.Quality, hasBotanist, isHeavy);
            int stack = hasGatherer && Game1.random.NextDouble() < 0.2 ? 2 : 1;

            var item = CreateItem(obj.ItemId, quality, stack);
            if (item == null)
                continue;

            if (machine.TryAddItem(item))
            {
                location.Objects.Remove(tile);

                log.Items.Add(new CollectedForageItem
                {
                    QualifiedItemId = item.QualifiedItemId,
                    ItemId          = obj.ItemId,
                    DisplayName     = item.DisplayName,
                    Stack           = stack,
                    Quality         = quality,
                    LocationName    = location.Name,
                    SourceType      = "Ground"
                });

                if (_config.GrantForagingXP)
                    Game1.player.gainExperience(2, ForageXPPerItem);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Forage crops (Spring Onion, Ginger)
    // -------------------------------------------------------------------------

    private void CollectForageCrops(
        GameLocation location,
        MachineInfo machine,
        DailyForageLog log,
        bool hasBotanist,
        bool hasGatherer,
        bool isHeavy)
    {
        var cropKeys = new List<Vector2>();
        foreach (var kvp in location.terrainFeatures.Pairs)
        {
            if (kvp.Value is HoeDirt dirt
                && dirt.crop != null
                && dirt.crop.forageCrop.Value
                && (dirt.readyForHarvest()
                    || dirt.crop.currentPhase.Value >= dirt.crop.phaseDays.Count - 1))
            {
                cropKeys.Add(kvp.Key);
            }
        }

        foreach (var tile in cropKeys)
        {
            if (machine.RemainingSlots <= 0)
                break;

            if (location.terrainFeatures[tile] is not HoeDirt dirt || dirt.crop == null)
                continue;

            string itemId = dirt.crop.whichForageCrop.Value switch
            {
                "1" => "399", // Spring Onion
                "2" => "829", // Ginger
                _   => ""
            };
            if (string.IsNullOrEmpty(itemId))
                continue;

            int quality = DetermineQuality(0, hasBotanist, isHeavy);
            int stack = hasGatherer && Game1.random.NextDouble() < 0.2 ? 2 : 1;

            var item = CreateItem(itemId, quality, stack);
            if (item == null)
                continue;

            if (machine.TryAddItem(item))
            {
                // Kill the crop so it can regrow (for spring onion) or mark as harvested
                dirt.destroyCrop(false);

                log.Items.Add(new CollectedForageItem
                {
                    QualifiedItemId = item.QualifiedItemId,
                    ItemId          = itemId,
                    DisplayName     = item.DisplayName,
                    Stack           = stack,
                    Quality         = quality,
                    LocationName    = location.Name,
                    SourceType      = "ForageCrop"
                });

                if (_config.GrantForagingXP)
                    Game1.player.gainExperience(2, ForageXPPerItem);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Harvestable bushes
    // -------------------------------------------------------------------------

    private void CollectBushes(
        GameLocation location,
        MachineInfo machine,
        DailyForageLog log,
        bool hasBotanist,
        bool hasGatherer,
        bool isHeavy)
    {
        foreach (var feature in location.largeTerrainFeatures)
        {
            if (machine.RemainingSlots <= 0)
                break;

            if (feature is not Bush bush)
                continue;

            int bushType = bush.size.Value;
            bool canProduce = bushType is Bush.mediumBush
                                       or Bush.greenTeaBush
                                       or Bush.walnutBush;
            if (!canProduce || !bush.readyForHarvest())
                continue;

            string? produceId = bush.GetShakeOffItem();
            if (produceId == null)
                continue;

            int quality = DetermineQuality(0, hasBotanist, isHeavy);
            int stack = hasGatherer && Game1.random.NextDouble() < 0.2 ? 2 : 1;

            var item = CreateItem(produceId, quality, stack);
            if (item == null)
                continue;

            if (machine.TryAddItem(item))
            {
                // Reset the bush harvest timer so it stops showing berries.
                // Do NOT call performUseAction — that shakes the bush and
                // physically spawns item drops onto the ground.
                bush.tileSheetOffset.Value = 0;
                bush.setUpSourceRect();

                log.Items.Add(new CollectedForageItem
                {
                    QualifiedItemId = item.QualifiedItemId,
                    ItemId          = produceId,
                    DisplayName     = item.DisplayName,
                    Stack           = stack,
                    Quality         = quality,
                    LocationName    = location.Name,
                    SourceType      = "Bush"
                });

                if (_config.GrantForagingXP)
                    Game1.player.gainExperience(2, ForageXPPerItem);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Mussel stones (IslandWest beach — stone ID 25, drops Mussel 718)
    // -------------------------------------------------------------------------

    private void CollectMusselStones(
        GameLocation location,
        MachineInfo machine,
        DailyForageLog log,
        bool hasBotanist,
        bool hasGatherer,
        bool isHeavy)
    {
        var stoneKeys = new List<Vector2>();
        foreach (var kvp in location.Objects.Pairs)
        {
            if (kvp.Value is StardewValley.Object obj
                && obj.ItemId == "25"
                && obj.DisplayName == "Mussel Stone")
            {
                stoneKeys.Add(kvp.Key);
            }
        }

        foreach (var tile in stoneKeys)
        {
            if (machine.RemainingSlots <= 0)
                break;

            if (!location.Objects.ContainsKey(tile))
                continue;

            int quality = DetermineQuality(0, hasBotanist, isHeavy);
            int stack = hasGatherer && Game1.random.NextDouble() < 0.2 ? 2 : 1;

            var item = CreateItem("719", quality, stack); // Mussel
            if (item == null)
                continue;

            if (machine.TryAddItem(item))
            {
                location.Objects.Remove(tile);

                log.Items.Add(new CollectedForageItem
                {
                    QualifiedItemId = item.QualifiedItemId,
                    ItemId          = "719",
                    DisplayName     = item.DisplayName,
                    Stack           = stack,
                    Quality         = quality,
                    LocationName    = location.Name,
                    SourceType      = "MusselStone"
                });

                if (_config.GrantForagingXP)
                    Game1.player.gainExperience(2, ForageXPPerItem);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Location eligibility
    // -------------------------------------------------------------------------

    private List<GameLocation> GetEligibleLocations(List<MachineInfo> machines, bool enforceRestrictions)
    {
        var result = new List<GameLocation>();

        foreach (var location in Game1.locations)
        {
            if (location == null)
                continue;

            // Outdoors only, skip mines/skull cavern/volcano
            if (!location.IsOutdoors)
                continue;
            if (location is StardewValley.Locations.MineShaft)
                continue;
            if (location.Name.StartsWith("VolcanoDungeon", StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this location's region is enabled in config
            string? region = RegionMapper.GetRegion(location.Name);
            if (!RegionMapper.IsRegionEnabled(region, _config))
                continue;

            // Check progression restrictions
            if (enforceRestrictions && !LocationRestrictions.CanAccessLocation(location.Name, true))
                continue;

            result.Add(location);
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsForageable(StardewValley.Object obj)
    {
        return obj.Category == -81
            || obj.isForage()
            || (obj.GetContextTags()?.Contains("forage_item") == true);
    }

    private static bool IsGingerIslandLocation(string locationName)
    {
        return locationName.StartsWith("Island", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines final quality for an item:
    /// - Heavy machine: minimum gold (2). If Botanist active, iridium (4).
    /// - Normal machine: uses existing quality. If Botanist active, iridium (4).
    /// </summary>
    private static int DetermineQuality(int baseQuality, bool hasBotanist, bool isHeavy)
    {
        if (hasBotanist)
            return 4; // Iridium

        if (isHeavy)
            return Math.Max(baseQuality, 2); // Minimum gold

        return baseQuality;
    }

    private static StardewValley.Object? CreateItem(string itemId, int quality, int stack)
    {
        try
        {
            var item = ItemRegistry.Create(itemId) as StardewValley.Object;
            if (item == null)
                return null;
            item.Quality = quality;
            item.Stack = stack;
            return item;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Lightweight wrapper around a placed Auto Forager machine (Chest instance),
/// tracking its location, type (normal/heavy), and remaining inventory capacity.
/// </summary>
public class MachineInfo
{
    /// <summary>The location name where this machine is placed.</summary>
    public string HomeLocationName { get; set; } = "";

    /// <summary>The tile position where this machine is placed.</summary>
    public Vector2 HomeTile { get; set; }

    /// <summary>Whether this is a Heavy Auto Forager.</summary>
    public bool IsHeavy { get; set; }

    /// <summary>Maximum inventory slots (32 for normal, 72 for heavy).</summary>
    public int MaxSlots { get; set; }

    /// <summary>Current remaining slots available.</summary>
    public int RemainingSlots { get; set; }

    /// <summary>The actual collected items stored in this machine.</summary>
    public List<Item> Inventory { get; set; } = new();

    /// <summary>Reference to the placed Chest object in the world.</summary>
    public Chest? PlacedChest { get; set; }

    /// <summary>
    /// Tries to add an item to the machine's inventory (both the local list and
    /// the Chest's Items), stacking with existing items when possible.
    /// Returns true if successfully added.
    /// </summary>
    public bool TryAddItem(StardewValley.Object item)
    {
        // Try to stack with existing items in the Chest first
        if (PlacedChest != null)
        {
            foreach (var existing in PlacedChest.Items)
            {
                if (existing is StardewValley.Object existingObj
                    && existingObj.ItemId == item.ItemId
                    && existingObj.Quality == item.Quality
                    && existingObj.Stack < existingObj.maximumStackSize())
                {
                    int canAdd = existingObj.maximumStackSize() - existingObj.Stack;
                    int toAdd = Math.Min(item.Stack, canAdd);
                    existingObj.Stack += toAdd;
                    item.Stack -= toAdd;
                    if (item.Stack <= 0)
                        return true;
                }
            }

            // Need a new slot
            if (PlacedChest.Items.Count >= MaxSlots)
                return false;

            PlacedChest.Items.Add(item);
            RemainingSlots = MaxSlots - PlacedChest.Items.Count;
            return true;
        }

        // Fallback: local inventory only (shouldn't happen in normal operation)
        foreach (var existing in Inventory)
        {
            if (existing is StardewValley.Object existingObj
                && existingObj.ItemId == item.ItemId
                && existingObj.Quality == item.Quality
                && existingObj.Stack < existingObj.maximumStackSize())
            {
                int canAdd = existingObj.maximumStackSize() - existingObj.Stack;
                int toAdd = Math.Min(item.Stack, canAdd);
                existingObj.Stack += toAdd;
                item.Stack -= toAdd;
                if (item.Stack <= 0)
                    return true;
            }
        }

        if (Inventory.Count >= MaxSlots)
            return false;

        Inventory.Add(item);
        RemainingSlots = MaxSlots - Inventory.Count;
        return true;
    }
}
