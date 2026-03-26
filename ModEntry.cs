// ModEntry.cs - Entry point for Auto Forager.
// Responsibilities: SMAPI event hooks, GMCM registration, machine discovery,
// and overnight forage collection. Machine stays in-world at all times.
// Machines are Chest instances for native inventory and Chests Anywhere compat.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoForager.Content;
using AutoForager.Framework;
using AutoForager.Models;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace AutoForager;

public class ModEntry : Mod
{
    private const string ModVersion = "1.0.0";

    private ModConfig _config = null!;
    private ContentInjector _contentInjector = null!;
    private ForageCollector _collector = null!;

    /// <summary>The last day's forage log for the daily log popup.</summary>
    private DailyForageLog? _lastDayLog;

    // -------------------------------------------------------------------------
    // SMAPI entry
    // -------------------------------------------------------------------------

    public override void Entry(IModHelper helper)
    {
        _config = helper.ReadConfig<ModConfig>();

        _contentInjector = new ContentInjector(helper);
        _contentInjector.Register();

        _collector = new ForageCollector(helper, Monitor, _config);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded   += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted   += OnDayStarted;
        helper.Events.World.ObjectListChanged += OnObjectListChanged;
        helper.Events.Input.ButtonsChanged  += OnButtonsChanged;
    }

    // -------------------------------------------------------------------------
    // Save loaded
    // -------------------------------------------------------------------------

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Monitor.Log(
            Helper.Translation.Get("log.startup", new { version = ModVersion }),
            LogLevel.Info);

        // Migrate any plain Object machines from older saves to Chest instances
        MigrateExistingMachines();
    }

    /// <summary>
    /// On save load, find any Auto Forager objects that are not Chest instances
    /// and replace them with Chest instances. This handles saves from before
    /// the Chest conversion, and ensures Chests Anywhere compatibility.
    /// </summary>
    private void MigrateExistingMachines()
    {
        foreach (var location in Game1.locations)
        {
            if (location == null)
                continue;

            var tilesToSwap = new List<Vector2>();
            foreach (var kvp in location.Objects.Pairs)
            {
                if (kvp.Value is Chest)
                    continue; // already a Chest, no migration needed

                if (kvp.Value.ItemId == ContentInjector.AutoForagerId)
                {
                    tilesToSwap.Add(kvp.Key);
                }
            }

            foreach (var tile in tilesToSwap)
            {
                if (!location.Objects.TryGetValue(tile, out var obj))
                    continue;

                var chest = CreateForagerChest(obj.ItemId, tile);

                // Migrate any inventory stored in modData
                if (obj.modData.TryGetValue("tbonehunter.AutoForager/Inventory", out string? data)
                    && !string.IsNullOrEmpty(data))
                {
                    MigrateModDataInventory(chest, data);
                }

                // Copy owner tag if present
                if (obj.modData.TryGetValue("tbonehunter.AutoForager/Owner", out string? owner))
                    chest.modData["tbonehunter.AutoForager/Owner"] = owner;

                location.Objects[tile] = chest;
                Monitor.Log($"Migrated Auto Forager at {location.Name} ({tile}) to Chest.", LogLevel.Debug);
            }
        }
    }

    /// <summary>
    /// Migrates pipe-delimited modData inventory string into a Chest's Items list.
    /// </summary>
    private static void MigrateModDataInventory(Chest chest, string data)
    {
        foreach (var entry in data.Split(';'))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3)
                continue;

            string itemId = parts[0];
            if (!int.TryParse(parts[1], out int quality))
                continue;
            if (!int.TryParse(parts[2], out int stack))
                continue;

            try
            {
                var item = ItemRegistry.Create(itemId) as StardewValley.Object;
                if (item != null)
                {
                    item.Quality = quality;
                    item.Stack = stack;
                    chest.Items.Add(item);
                }
            }
            catch
            {
                // Skip invalid items
            }
        }
    }

    // -------------------------------------------------------------------------
    // Object placement — swap placed BigCraftable to Chest
    // -------------------------------------------------------------------------

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        foreach (var pair in e.Added)
        {
            var obj = pair.Value;
            if (obj is Chest)
                continue; // already a Chest (e.g. from save load)

            if (obj.ItemId != ContentInjector.AutoForagerId)
                continue;

            var tile = pair.Key;
            var chest = CreateForagerChest(obj.ItemId, tile);

            // Tag owner for multiplayer
            if (Context.IsMultiplayer)
                chest.modData["tbonehunter.AutoForager/Owner"] = Game1.player.UniqueMultiplayerID.ToString();

            e.Location.Objects[tile] = chest;
            Monitor.Log($"Placed Auto Forager as Chest at {e.Location.Name} ({tile}).", LogLevel.Debug);
        }
    }

    /// <summary>
    /// Creates a Chest instance configured as an Auto Forager machine.
    /// Uses playerChest = true so Chests Anywhere recognizes it.
    /// </summary>
    private static Chest CreateForagerChest(string itemId, Vector2 tile)
    {
        var chest = new Chest(playerChest: true, tileLocation: tile, itemId: itemId)
        {
            SpecialChestType = Chest.SpecialChestTypes.None
        };
        chest.modData["tbonehunter.AutoForager/IsForager"] = "true";
        return chest;
    }

    // -------------------------------------------------------------------------
    // Day lifecycle
    // -------------------------------------------------------------------------

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        var machines = FindPlacedMachines();
        if (machines.Count == 0)
            return;

        var log = _collector.CollectAll(machines);
        _lastDayLog = log;

        // No explicit save needed — items are in Chest.Items which the game persists

        Monitor.Log($"Auto Forager collected {log.TotalItems} items across {log.LocationsVisited} locations.", LogLevel.Info);

        if (log.TotalItems > 0)
        {
            Game1.addHUDMessage(new HUDMessage(
                Helper.Translation.Get("hud.collected.overnight"),
                HUDMessage.achievement_type));
        }
    }

    // -------------------------------------------------------------------------
    // Hotkey dispatch
    // -------------------------------------------------------------------------

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsPlayerFree)
            return;

        if (_config.HotkeyViewLog.JustPressed())
            ShowDailyLog();
    }

    // -------------------------------------------------------------------------
    // Machine discovery
    // -------------------------------------------------------------------------

    private List<MachineInfo> FindPlacedMachines()
    {
        var machines = new List<MachineInfo>();

        foreach (var location in Game1.locations)
        {
            if (location == null)
                continue;

            foreach (var kvp in location.Objects.Pairs)
            {
                if (kvp.Value is not Chest chest)
                    continue;

                bool isAutoForager = chest.ItemId == ContentInjector.AutoForagerId;

                if (!isAutoForager)
                    continue;

                // Check multiplayer ownership
                if (!ShouldProcessMachine(chest))
                    continue;

                const int maxSlots = 36;
                machines.Add(new MachineInfo
                {
                    HomeLocationName = location.Name,
                    HomeTile         = kvp.Key,
                    MaxSlots         = maxSlots,
                    RemainingSlots   = maxSlots - chest.Items.Count,
                    Inventory        = chest.Items.ToList(),
                    PlacedChest      = chest
                });
            }
        }

        return machines;
    }

    /// <summary>
    /// Checks multiplayer ownership rules. In shared mode, any machine is processed.
    /// In per-player mode, only machines placed by this player are processed.
    /// </summary>
    private bool ShouldProcessMachine(StardewValley.Object obj)
    {
        if (!Context.IsMultiplayer)
            return true;

        bool shared = _config.SharedMachines ?? Game1.player.useSeparateWallets == false;
        if (shared)
            return true;

        // Per-player mode: check if this player placed the machine
        if (obj.modData.TryGetValue("tbonehunter.AutoForager/Owner", out string? ownerId))
            return ownerId == Game1.player.UniqueMultiplayerID.ToString();

        return true; // no owner recorded, allow
    }

    // -------------------------------------------------------------------------
    // Daily log popup
    // -------------------------------------------------------------------------

    private void ShowDailyLog()
    {
        if (_lastDayLog == null || _lastDayLog.Items.Count == 0)
        {
            Game1.activeClickableMenu = new LetterViewerMenu(
                Helper.Translation.Get("log.empty"));
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Helper.Translation.Get("log.title"));
        sb.AppendLine($"{_lastDayLog.Season} {_lastDayLog.Day}, Year {_lastDayLog.Year}");
        sb.AppendLine();

        // Group by item name and quality
        var grouped = _lastDayLog.Items
            .GroupBy(i => new { i.DisplayName, i.Quality })
            .OrderBy(g => g.Key.DisplayName);

        foreach (var group in grouped)
        {
            int totalStack = group.Sum(i => i.Stack);
            string qualityStr = group.Key.Quality switch
            {
                1 => "Silver",
                2 => "Gold",
                4 => "Iridium",
                _ => ""
            };
            sb.AppendLine(Helper.Translation.Get("log.entry",
                new { arg0 = totalStack, arg1 = group.Key.DisplayName, arg2 = qualityStr }));
        }

        sb.AppendLine();
        sb.AppendLine(Helper.Translation.Get("log.total", new { arg0 = _lastDayLog.TotalItems }));

        Game1.activeClickableMenu = new LetterViewerMenu(sb.ToString());
    }

    // -------------------------------------------------------------------------
    // GMCM registration
    // -------------------------------------------------------------------------

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(
            "spacechase0.GenericModConfigMenu");

        if (gmcm == null) return;

        gmcm.Register(
            mod:   ModManifest,
            reset: () => _config = new ModConfig(),
            save:  () => Helper.WriteConfig(_config));

        // --- Forage Types ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.forage-types"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.CollectGroundForage,
            setValue: v => _config.CollectGroundForage = v,
            name:     () => Helper.Translation.Get("gmcm.forage.ground.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.forage.ground.description"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.CollectForageCrops,
            setValue: v => _config.CollectForageCrops = v,
            name:     () => Helper.Translation.Get("gmcm.forage.crops.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.forage.crops.description"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.CollectBushes,
            setValue: v => _config.CollectBushes = v,
            name:     () => Helper.Translation.Get("gmcm.forage.bushes.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.forage.bushes.description"));

        // --- Perk Integration ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.perks"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.GrantForagingXP,
            setValue: v => _config.GrantForagingXP = v,
            name:     () => Helper.Translation.Get("gmcm.perks.xp.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.perks.xp.description"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.ApplyBotanist,
            setValue: v => _config.ApplyBotanist = v,
            name:     () => Helper.Translation.Get("gmcm.perks.botanist.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.perks.botanist.description"));

        gmcm.AddBoolOption(
            mod:      ModManifest,
            getValue: () => _config.ApplyGatherer,
            setValue: v => _config.ApplyGatherer = v,
            name:     () => Helper.Translation.Get("gmcm.perks.gatherer.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.perks.gatherer.description"));

        // --- Location Regions ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.locations"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionFarm,
            setValue: v => _config.RegionFarm = v,
            name:     () => Helper.Translation.Get("gmcm.locations.farm.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.farm.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionTown,
            setValue: v => _config.RegionTown = v,
            name:     () => Helper.Translation.Get("gmcm.locations.town.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.town.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionForest,
            setValue: v => _config.RegionForest = v,
            name:     () => Helper.Translation.Get("gmcm.locations.forest.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.forest.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionBeach,
            setValue: v => _config.RegionBeach = v,
            name:     () => Helper.Translation.Get("gmcm.locations.beach.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.beach.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionMountain,
            setValue: v => _config.RegionMountain = v,
            name:     () => Helper.Translation.Get("gmcm.locations.mountain.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.mountain.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionRailroad,
            setValue: v => _config.RegionRailroad = v,
            name:     () => Helper.Translation.Get("gmcm.locations.railroad.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.railroad.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionDesert,
            setValue: v => _config.RegionDesert = v,
            name:     () => Helper.Translation.Get("gmcm.locations.desert.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.desert.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionIsland,
            setValue: v => _config.RegionIsland = v,
            name:     () => Helper.Translation.Get("gmcm.locations.island.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.island.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionSewer,
            setValue: v => _config.RegionSewer = v,
            name:     () => Helper.Translation.Get("gmcm.locations.sewer.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.sewer.description"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.RegionWoods,
            setValue: v => _config.RegionWoods = v,
            name:     () => Helper.Translation.Get("gmcm.locations.woods.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.locations.woods.description"));

        // --- Progression ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.restrictions"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.EnforceProgressionRestrictions,
            setValue: v => _config.EnforceProgressionRestrictions = v,
            name:     () => Helper.Translation.Get("gmcm.restrictions.enforce.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.restrictions.enforce.description"));

        // --- Multiplayer ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.multiplayer"));

        gmcm.AddBoolOption(
            mod: ModManifest,
            getValue: () => _config.SharedMachines ?? (Game1.player?.useSeparateWallets == false),
            setValue: v => _config.SharedMachines = v,
            name:     () => Helper.Translation.Get("gmcm.multiplayer.shared.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.multiplayer.shared.description"));

        // --- Hotkeys ---
        gmcm.AddSectionTitle(
            mod:  ModManifest,
            text: () => Helper.Translation.Get("gmcm.section.hotkeys"));

        gmcm.AddKeybindList(
            mod: ModManifest,
            getValue: () => _config.HotkeyViewLog,
            setValue: v => _config.HotkeyViewLog = v,
            name:     () => Helper.Translation.Get("gmcm.hotkey.log.label"),
            tooltip:  () => Helper.Translation.Get("gmcm.hotkey.log.description"));
    }
}

// -------------------------------------------------------------------------
// GMCM API interface (minimal — only methods used by this mod)
// -------------------------------------------------------------------------

/// <summary>
/// Minimal interface for the Generic Mod Config Menu API.
/// </summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue,
        Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddKeybindList(IManifest mod, Func<StardewModdingAPI.Utilities.KeybindList> getValue,
        Action<StardewModdingAPI.Utilities.KeybindList> setValue,
        Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue,
        Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null,
        int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
}
