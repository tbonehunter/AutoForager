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
/// Handles perk application (Botanist, Gatherer), quality scaling by Foraging level,
/// and fair round-robin distribution in multiplayer per-player mode.
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

    // =========================================================================
    // Public entry point
    // =========================================================================

    /// <summary>
    /// Runs the daily collection cycle.
    /// In shared mode (or single-player), all machines share one collection pass.
    /// In per-player mode, forage is gathered into a pool and distributed
    /// round-robin across players who own machines, with quality/perks rolled
    /// per-player.
    /// </summary>
    /// <param name="machinesByPlayer">
    /// Machines grouped by owner. Key = Farmer (owner), Value = list of that
    /// player's machines. In shared mode, all machines are under the host farmer.
    /// </param>
    /// <param name="isSharedMode">Whether machines are shared (single pool) or per-player.</param>
    public DailyForageLog CollectAll(
        Dictionary<Farmer, List<MachineInfo>> machinesByPlayer,
        bool isSharedMode)
    {
        var log = new DailyForageLog
        {
            Day    = Game1.dayOfMonth,
            Season = Game1.currentSeason,
            Year   = Game1.year
        };

        if (machinesByPlayer.Count == 0)
            return log;

        bool enforceRestrictions = _config.EnforceProgressionRestrictions;
        var allMachines = machinesByPlayer.Values.SelectMany(m => m).ToList();

        // Determine which locations to scan based on config and restrictions
        var eligibleLocations = GetEligibleLocations(allMachines, enforceRestrictions);
        log.LocationsVisited = eligibleLocations.Count;

        _monitor.Log(
            _helper.Translation.Get("log.collecting", new { count = eligibleLocations.Count }),
            LogLevel.Debug);

        if (isSharedMode || machinesByPlayer.Count == 1)
        {
            // Shared mode or single player: collect directly into machines
            CollectShared(allMachines, eligibleLocations, log);
        }
        else
        {
            // Per-player mode: gather pool, then distribute round-robin
            CollectPerPlayer(machinesByPlayer, eligibleLocations, log);
        }

        log.TotalItems = log.Items.Sum(i => i.Stack);

        _monitor.Log(
            _helper.Translation.Get("log.collected",
                new { count = log.TotalItems, locations = log.LocationsVisited }),
            LogLevel.Info);

        return log;
    }

    // =========================================================================
    // Shared mode — original behavior, one pass for all machines
    // =========================================================================

    private void CollectShared(
        List<MachineInfo> machines,
        List<GameLocation> eligibleLocations,
        DailyForageLog log)
    {
        foreach (var machine in machines)
        {
            if (machine.RemainingSlots <= 0)
                continue;

            CollectForMachine(machine, eligibleLocations, log, Game1.player);
        }
    }

    // =========================================================================
    // Per-player mode — gather pool, then distribute round-robin
    // =========================================================================

    /// <summary>
    /// Represents a raw forage entry harvested from the world, before
    /// player-specific quality/perk rolls are applied.
    /// </summary>
    private class RawForageEntry
    {
        public string ItemId { get; set; } = "";
        public int BaseQuality { get; set; }
        public string LocationName { get; set; } = "";
        public string SourceType { get; set; } = "";
        public bool IsIsland { get; set; }
    }

    private void CollectPerPlayer(
        Dictionary<Farmer, List<MachineInfo>> machinesByPlayer,
        List<GameLocation> eligibleLocations,
        DailyForageLog log)
    {
        // Phase 1: Gather all forage into a raw pool, removing from the world
        var mainlandPool = new List<RawForageEntry>();
        var islandPool   = new List<RawForageEntry>();

        foreach (var location in eligibleLocations)
        {
            bool locOnIsland = IsGingerIslandLocation(location.Name);
            var targetPool = locOnIsland ? islandPool : mainlandPool;

            if (_config.CollectGroundForage)
                GatherGroundForage(location, targetPool);

            if (_config.CollectForageCrops)
                GatherForageCrops(location, targetPool);

            if (_config.CollectBushes)
                GatherBushes(location, targetPool);

            if (_config.CollectGroundForage
                && location.Name.Equals("IslandWest", StringComparison.OrdinalIgnoreCase))
                GatherMusselStones(location, targetPool);
        }

        // Phase 2: Build list of participants per geographic zone
        var mainlandParticipants = new List<(Farmer farmer, List<MachineInfo> machines)>();
        var islandParticipants   = new List<(Farmer farmer, List<MachineInfo> machines)>();

        foreach (var kvp in machinesByPlayer)
        {
            var mainlandMachines = kvp.Value.Where(m => !IsGingerIslandLocation(m.HomeLocationName)).ToList();
            var islandMachines   = kvp.Value.Where(m =>  IsGingerIslandLocation(m.HomeLocationName)).ToList();

            if (mainlandMachines.Count > 0)
                mainlandParticipants.Add((kvp.Key, mainlandMachines));
            if (islandMachines.Count > 0)
                islandParticipants.Add((kvp.Key, islandMachines));
        }

        // Phase 3: Distribute round-robin
        DistributePool(mainlandPool, mainlandParticipants, log);
        DistributePool(islandPool,   islandParticipants,   log);
    }

    /// <summary>
    /// Distributes a pool of raw forage entries round-robin across participants.
    /// Quality and Gatherer double-harvest are rolled per-player.
    /// </summary>
    private void DistributePool(
        List<RawForageEntry> pool,
        List<(Farmer farmer, List<MachineInfo> machines)> participants,
        DailyForageLog log)
    {
        if (pool.Count == 0 || participants.Count == 0)
            return;

        int playerIndex = 0;

        foreach (var entry in pool)
        {
            // Find next participant with available machine capacity
            bool assigned = false;
            for (int attempts = 0; attempts < participants.Count; attempts++)
            {
                int idx = (playerIndex + attempts) % participants.Count;
                var (farmer, machines) = participants[idx];

                // Find a machine with room
                var machine = machines.FirstOrDefault(m => m.RemainingSlots > 0);
                if (machine == null)
                    continue;

                // Roll quality and perks for THIS player
                bool hasBotanist  = _config.ApplyBotanist && farmer.professions.Contains(BotanistProfession);
                bool hasGatherer  = _config.ApplyGatherer && farmer.professions.Contains(GathererProfession);
                int  foragingLevel = farmer.ForagingLevel;

                int quality = DetermineQuality(entry.BaseQuality, hasBotanist, foragingLevel);
                int stack = hasGatherer && Game1.random.NextDouble() < 0.2 ? 2 : 1;

                var item = CreateItem(entry.ItemId, quality, stack);
                if (item == null)
                    break;

                if (machine.TryAddItem(item))
                {
                    log.Items.Add(new CollectedForageItem
                    {
                        QualifiedItemId = item.QualifiedItemId,
                        ItemId          = entry.ItemId,
                        DisplayName     = item.DisplayName,
                        Stack           = stack,
                        Quality         = quality,
                        LocationName    = entry.LocationName,
                        SourceType      = entry.SourceType
                    });

                    if (_config.GrantForagingXP)
                        farmer.gainExperience(2, ForageXPPerItem);

                    assigned = true;
                }

                break;
            }

            // Advance to next player regardless of success (fairness)
            playerIndex = (playerIndex + 1) % participants.Count;

            if (!assigned)
            {
                _monitor.Log($"Could not assign {entry.ItemId} from {entry.LocationName} — all machines full.", LogLevel.Trace);
            }
        }
    }

    // =========================================================================
    // Gather methods — remove from world, add to raw pool (no quality roll yet)
    // =========================================================================

    private void GatherGroundForage(GameLocation location, List<RawForageEntry> pool)
    {
        var keys = new List<Vector2>();
        foreach (var kvp in location.Objects.Pairs)
        {
            if (kvp.Value is StardewValley.Object obj && IsForageable(obj))
                keys.Add(kvp.Key);
        }

        foreach (var tile in keys)
        {
            if (!location.Objects.TryGetValue(tile, out var obj))
                continue;

            pool.Add(new RawForageEntry
            {
                ItemId       = obj.ItemId,
                BaseQuality  = obj.Quality,
                LocationName = location.Name,
                SourceType   = "Ground",
                IsIsland     = IsGingerIslandLocation(location.Name)
            });

            location.Objects.Remove(tile);
        }
    }

    private void GatherForageCrops(GameLocation location, List<RawForageEntry> pool)
    {
        var keys = new List<Vector2>();
        foreach (var kvp in location.terrainFeatures.Pairs)
        {
            if (kvp.Value is HoeDirt dirt
                && dirt.crop != null
                && dirt.crop.forageCrop.Value
                && (dirt.readyForHarvest()
                    || dirt.crop.currentPhase.Value >= dirt.crop.phaseDays.Count - 1))
            {
                keys.Add(kvp.Key);
            }
        }

        foreach (var tile in keys)
        {
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

            pool.Add(new RawForageEntry
            {
                ItemId       = itemId,
                BaseQuality  = 0,
                LocationName = location.Name,
                SourceType   = "ForageCrop",
                IsIsland     = IsGingerIslandLocation(location.Name)
            });

            dirt.destroyCrop(false);
        }
    }

    private void GatherBushes(GameLocation location, List<RawForageEntry> pool)
    {
        foreach (var feature in location.largeTerrainFeatures)
        {
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

            pool.Add(new RawForageEntry
            {
                ItemId       = produceId,
                BaseQuality  = 0,
                LocationName = location.Name,
                SourceType   = "Bush",
                IsIsland     = IsGingerIslandLocation(location.Name)
            });

            bush.tileSheetOffset.Value = 0;
            bush.setUpSourceRect();
        }
    }

    private void GatherMusselStones(GameLocation location, List<RawForageEntry> pool)
    {
        var keys = new List<Vector2>();
        foreach (var kvp in location.Objects.Pairs)
        {
            if (kvp.Value is StardewValley.Object obj
                && obj.ItemId == "25"
                && obj.DisplayName == "Mussel Stone")
            {
                keys.Add(kvp.Key);
            }
        }

        foreach (var tile in keys)
        {
            if (!location.Objects.ContainsKey(tile))
                continue;

            pool.Add(new RawForageEntry
            {
                ItemId       = "719",
                BaseQuality  = 0,
                LocationName = location.Name,
                SourceType   = "MusselStone",
                IsIsland     = true
            });

            location.Objects.Remove(tile);
        }
    }

    // =========================================================================
    // Shared-mode collection for a single machine (original direct approach)
    // =========================================================================

    private void CollectForMachine(
        MachineInfo machine,
        List<GameLocation> locations,
        DailyForageLog log,
        Farmer player)
    {
        bool hasBotanist  = _config.ApplyBotanist && player.professions.Contains(BotanistProfession);
        bool hasGatherer  = _config.ApplyGatherer && player.professions.Contains(GathererProfession);
        int  foragingLevel = player.ForagingLevel;
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
                CollectGroundForage(location, machine, log, hasBotanist, hasGatherer, foragingLevel);

            // Forage crops
            if (_config.CollectForageCrops)
                CollectForageCrops(location, machine, log, hasBotanist, hasGatherer, foragingLevel);

            // Harvestable bushes
            if (_config.CollectBushes)
                CollectBushes(location, machine, log, hasBotanist, hasGatherer, foragingLevel);

            // Mussel stones (IslandWest beach only)
            if (_config.CollectGroundForage
                && location.Name.Equals("IslandWest", StringComparison.OrdinalIgnoreCase))
                CollectMusselStones(location, machine, log, hasBotanist, hasGatherer, foragingLevel);
        }
    }

    // -------------------------------------------------------------------------
    // Ground forageables (shared mode)
    // -------------------------------------------------------------------------

    private void CollectGroundForage(
        GameLocation location,
        MachineInfo machine,
        DailyForageLog log,
        bool hasBotanist,
        bool hasGatherer,
        int foragingLevel)
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

            int quality = DetermineQuality(obj.Quality, hasBotanist, foragingLevel);
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
        int foragingLevel)
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

            int quality = DetermineQuality(0, hasBotanist, foragingLevel);
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
        int foragingLevel)
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

            int quality = DetermineQuality(0, hasBotanist, foragingLevel);
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
        int foragingLevel)
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

            int quality = DetermineQuality(0, hasBotanist, foragingLevel);
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
        return obj.IsSpawnedObject
            || obj.Category == -81
            || obj.isForage()
            || (obj.GetContextTags()?.Contains("forage_item") == true);
    }

    private static bool IsGingerIslandLocation(string locationName)
    {
        return locationName.StartsWith("Island", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines final quality for an item, mirroring the vanilla forage quality formula:
    /// - Botanist perk: always Iridium (4)
    /// - Otherwise: random roll based on Foraging level
    ///   - Gold chance: foragingLevel / 30
    ///   - Silver chance: foragingLevel / 15
    ///   - Normal otherwise
    /// Quality is always at least the item's base quality.
    /// </summary>
    private static int DetermineQuality(int baseQuality, bool hasBotanist, int foragingLevel)
    {
        if (hasBotanist)
            return 4; // Iridium

        // Vanilla formula from GameLocation.checkForBuriedItem / Object forage logic
        double roll = Game1.random.NextDouble();
        int quality;

        if (roll < foragingLevel / 30.0)
            quality = 2; // Gold
        else if (roll < foragingLevel / 15.0)
            quality = 1; // Silver
        else
            quality = 0; // Normal

        return Math.Max(baseQuality, quality);
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

    /// <summary>Maximum inventory slots.</summary>
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
