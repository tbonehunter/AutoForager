// Content/ContentInjector.cs - Injects Big Craftable data and crafting recipes
// into the game via SMAPI's content API (IAssetRequested event).
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Machines;

namespace AutoForager.Content;

/// <summary>
///  Injects the Auto Forager and Heavy Auto Forager Big Craftables, crafting
///  recipes, and machine rules into the game's content pipeline.
/// </summary>
public class ContentInjector
{
    // Internal item ID — prefixed with mod UniqueID to avoid conflicts
    public const string AutoForagerId = "tbonehunter.AutoForager_AutoForager";

    private readonly IModHelper _helper;

    public ContentInjector(IModHelper helper)
    {
        _helper = helper;
    }

    /// <summary>Register with the content pipeline.</summary>
    public void Register()
    {
        _helper.Events.Content.AssetRequested += OnAssetRequested;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // --- Localized strings dictionary (required for [LocalizedText] tokens) ---
        if (e.NameWithoutLocale.IsEquivalentTo("Strings/tbonehunter.AutoForager"))
        {
            e.LoadFrom(() => new Dictionary<string, string>
            {
                ["machine.name"]              = _helper.Translation.Get("machine.name"),
                ["machine.description"]       = _helper.Translation.Get("machine.description")
            }, AssetLoadPriority.Exclusive);
        }

        // --- Big Craftables data ---
        if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, BigCraftableData>();

                data.Data[AutoForagerId] = new BigCraftableData
                {
                    Name         = AutoForagerId,
                    DisplayName  = "[LocalizedText Strings\\tbonehunter.AutoForager:machine.name]",
                    Description  = "[LocalizedText Strings\\tbonehunter.AutoForager:machine.description]",
                    Texture      = $"Mods/{AutoForagerId}/BigCraftable",
                    SpriteIndex  = 0,
                    CanBePlacedOutdoors = true,
                    CanBePlacedIndoors  = true,
                    Fragility    = 0 // 0 = can be picked up with tools
                };


            });
        }

        // --- Crafting recipes ---
        if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, string>();

                // Format: ingredients/field2/output/big_craftable_flag/unlock_condition
                // Ingredients: itemId count pairs separated by spaces
                // Auto Forager: 15 Copper Ore (378), 50 Fiber (771), 1 Horseradish (16),
                //               1 Leek (20), 1 Dandelion (22), 1 Salmonberry (296)
                data.Data[AutoForagerId] =
                    "378 15 771 50 16 1 20 1 22 1 296 1" +  // ingredients
                    "/Home/" +                               // field 2 (unused)
                    $"{AutoForagerId}/true" +                // output / isBigCraftable
                    "/Foraging 3";                           // unlock: Foraging Level 3

            });
        }

        // --- Textures ---
        if (e.NameWithoutLocale.IsEquivalentTo($"Mods/{AutoForagerId}/BigCraftable"))
        {
            e.LoadFromModFile<Texture2D>("assets/auto-forager.png", AssetLoadPriority.Exclusive);
        }


    }
}
