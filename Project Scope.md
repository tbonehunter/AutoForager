<!-- Project Scope.md -->
# SVDataExtractor & AutoForager — Project Scope

## Overview

Two interconnected Stardew Valley SMAPI mods:

1. **SVDataExtractor** (existing, to be extended) — A **framework mod** that exposes live game-state data via a programmatic API. It does not "do" anything player-facing on its own. Other mods declare it as a dependency and call its API to get structured game data at runtime. Similar in concept to UIInfoSuite2 or EscasModdingPlugins.

2. **AutoForager** (new mod) — A consumer mod built on SVDataExtractor. Adds a craftable Big Craftable machine that autonomously forages all ready-to-harvest items across the game world each day.

---

## SVDataExtractor — Framework Changes

SVDataExtractor currently writes JSON files to disk on hotkey press. To serve as a framework, it needs to additionally:

- **Expose a public API interface** (e.g., `ISVDataExtractorAPI`) registered with SMAPI's mod registry.
- **Provide methods** that return the same data the extractors already compute, but as in-memory objects (not just JSON files).
- **Consumer mods** would declare a dependency in their `manifest.json` and call:
  ```csharp
  var api = helper.ModRegistry.GetApi<ISVDataExtractorAPI>("tbonehunter.SVDataExtractor");
  ```
- The existing JSON-export functionality remains intact — the API is an additional access layer.

### API Methods (Proposed)

| Method | Returns | Description |
|--------|---------|-------------|
| `GetForageables()` | `List<ExtractedForageable>` | All ground forageables across all locations |
| `GetForageCrops()` | `List<ExtractedTerrainFeature>` | Forage crops (spring onion, ginger) that are ready to harvest |
| `GetHarvestableBushes()` | `List<ExtractedHarvestableBush>` | Bushes ready to harvest (berries, tea, walnut) |
| `GetWorldState()` | `ExtractedWorldState` | Full world state snapshot |
| `GetGameState()` | `ExtractedGameState` | Full game state snapshot |
| `GetItemData()` | `ExtractedItemData` | Full item registry data |

---

## AutoForager — New Mod

### Core Concept

A Big Craftable machine that:

- **Morning (`DayStarted`)**: Disappears from its farm tile, spawns a roaming entity that travels across world locations.
- **During the day**: The entity uses SVDataExtractor's API to identify all ready-to-harvest forageables, removes them from the world, and stores them in the machine's internal inventory. If the player enters a location where the entity currently is, it's visible and wanders like a farm animal.
- **Evening (~6 PM or `DayEnding`)**: The entity despawns and the machine reappears on its farm tile with collected items inside.
- **Player interaction**: Right-click the returned machine to open its inventory and retrieve the collected forage.

---

## Open Design Questions

### Foraging Scope

- [ ] **Ground forageables only?** (Daffodils, Leeks, Clams, Cave Fruit, etc.)
- [ ] **Ground forageables + forage crops?** (Spring Onion, Ginger)
- [x] **All of the above + harvestable bushes?** (Salmonberries, Blackberries, Green Tea Leaves)
- [x] **Should this be configurable?** (per-machine toggle, GMCM setting, or both?) See GMCM section at end

### Player Progression

- [X] Does the player earn **Foraging XP** for auto-collected items?
- [X] Does the **Botanist perk** (Level 10 — all forage is iridium quality) apply to auto-collected items?
- [X] Should any other Foraging perks affect the machine's behavior? (e.g., Gatherer's double-harvest chance)
ALL three are available and toggleable in GMCM (both in game and title screen)

### Crafting & Balance

- [Level 3 Foraging and Level 7 for "Heavy AutoForager" similar to the "Heavy Tapper"] What **game tier** is this item? (Early game / Mid game / Late game unlock)
- [15 Copper ore, 50 Fiber, 1 each Horseradish, leek, dandelion and Salmonberry] What are the **crafting recipe ingredients**? Add 1 Blackberry and 1 coconut for "heavy" machine
- [No] Is there a **daily fuel or operating cost**? (e.g., battery pack, coal, gold)
- [Yes, see below] Can the player build **multiple auto-foragers** that operate simultaneously?
- [If multiple are built, the only advantage is that they can get more items without being emptied in mid-day (or miss forage items)] If multiple are allowed, do they split locations or redundantly cover everything? A single machine can harvest the world map except Ginger Island, that will require a separate machine that lives there. All other locations are not separated by water requiring a boat for transport.

### Machine Behavior

- [Can only be picked up when empty, just like chests, and if picked up is in inventory/chest until placed. Can't harvest forage while in inventory or a chest, HUD will alert player that "AutoForager cannot harvest forage until placed"] What happens if the player **picks up the machine** while it's home?
- [It parks in the closest available tile] What if the **farm tile is blocked** when the machine tries to return in the evening?
- [32 slots for normal machine, 72 for "Heavy"] Does the machine have a **fixed inventory capacity**, or is it unlimited?
- [See above] If capacity is limited, what is the max? Does it stop collecting when full?
- [HUD that it is returned, player can see it parked in spot (spot it returns to is the one the  player placed it in when crafting, just like any other machine] What visual/audio cue indicates the machine has **returned with items**?

### Roaming Entity

- [Outdoors only] Which locations does it visit? **All outdoor locations only**, or also interiors?
- [YES] Should it **skip mines, Skull Cavern, and Volcano Dungeon**?
- [Yes, see GMCM note, and I can provide code for progression restrictions on locations available] Should it only visit locations the **player has already unlocked/visited**?
- [I will create sprite] Does it need a **custom sprite**, or reuse an existing game sprite as a placeholder to start?
- [Yes, with owner's name] Should it display a **name or hover text** when the player encounters it?
- [Direct path unless player is in viewing range, then it will appear to "sweep" area systematically, and it can move through everything, not blocked by trees, etc.] How does it **move/pathfind** within a location? (Random wander like farm animals, or direct path to forage tiles?)
- [Moves over item and item disappears] Does it appear to **physically pick up items** as it moves, or are items just removed silently?

### UI & Feedback

- [No] Should there be a **HUD notification** in the morning when the machine departs?
- [No, just the HUD that it's back. Player can open it to see what's in it.] Should there be a **HUD notification** in the evening listing what was collected?
- [One day only, hotkey activated that is GMCM configurable] Should the machine keep a **daily log** the player can review? (e.g., "Day 16: collected 3 Daffodils, 2 Leeks, 1 Clam")
- [See GMCM options at end of file] Should there be **GMCM config options**? If so, what's configurable?

### Technical

- [x] Mod name / UniqueID convention? (e.g., `tbonehunter.AutoForager`)
- [x] Minimum Stardew Valley version target? (1.6.x)
- [x] Minimum SMAPI version target? (4.0.0)
- [Per Player (owner) OR shared, configurable in GMCM. Default is per player if separate money, shared if money is shared] Should the mod support **multiplayer**? If so, one machine per player, or shared?
- [ANY location, forageables must conform to vanilla forageable standards if there are new types of forageables (berries, etc.] Any **mod compatibility concerns** to consider? (e.g., SVE, Ridgeside Village adding new locations with forageables)

---
### GMCM Configuration
- Types of forage to collect (List of categories with on/off toggle for each)
 - Locations to Forage (All Map Locations (forest, mountains, town, beach, desert, Ginger Island locations) listed with on/off toggle for each)
 - Override Location Progression Restrictions toggle on/off (discuss progression restrictions code from another mod)
 - Multiplayer shared forage or ownership by crafter toggle



---
## Next Steps

1. Answer the design questions above.
2. Add the API layer to SVDataExtractor.
3. Build the AutoForager mod skeleton.
4. Implement core foraging logic.
5. Add the Big Craftable item and crafting recipe.
6. Implement the roaming entity behavior.
7. Add UI/feedback systems.
8. Test and balance.
