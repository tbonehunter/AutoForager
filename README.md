<!-- README.md -->
# Auto Forager

A [Stardew Valley](https://www.stardewvalley.net/) mod that adds craftable **Auto Forager** machines. Place one on your farm (or anywhere) and it will automatically collect forageable items from across the valley overnight. Open it like a chest to retrieve your haul.

## Requirements

| Dependency | Required |
|---|---|
| [SMAPI](https://smapi.io/) 4.0.0+ | Yes |
| [SV Data Extractor](https://github.com/tbonehunter/SVDataExtractor) (`tbonehunter.SVDataExtractor`) | Yes |
| [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) | No (recommended) |

## Installation

1. Install SMAPI and SV Data Extractor.
2. Drop the `AutoForager` folder into your `Stardew Valley/Mods` directory.
3. Launch the game.

## Machine

The Auto Forager is crafted at a workbench:

- **Unlocked at:** Foraging Level 3
- **Recipe:** 15 Copper Ore, 50 Fiber, 1 Horseradish, 1 Leek, 1 Dandelion, 1 Salmonberry
- **Inventory:** 36 slots
- **Quality:** Mirrors the vanilla foraging quality formula (see below)

## How It Works

1. **Craft** an Auto Forager and place it anywhere in the world.
2. At the start of each day, the machine scans every eligible outdoor location and collects forageable items into its internal inventory.
3. A HUD message lets you know that collection has occurred.
4. **Open the machine** (interact with it like any chest) to take your items.
5. If the machine's inventory fills up mid-collection, it stops and logs a message.

The machine is implemented as a native Stardew Valley `Chest`, so it is fully compatible with **[Chests Anywhere](https://www.nexusmods.com/stardewvalley/mods/518)** — your Auto Forager will appear in the Chests Anywhere menu without any extra setup.

### What Gets Collected

| Category | Examples | Config Toggle |
|---|---|---|
| **Ground Forageables** | Daffodils, Leeks, Clams, Snow Yams, etc. | `CollectGroundForage` |
| **Forage Crops** | Spring Onions, Ginger | `CollectForageCrops` |
| **Harvestable Bushes** | Salmonberries, Blackberries, Green Tea Leaves | `CollectBushes` |
| **Mussel Stones** | Mussels (from Mussel Stones on IslandWest beach) | Collected when `CollectGroundForage` is enabled |

### Perk Integration

The Auto Forager respects the player's Foraging skill perks:

- **Foraging XP** — You earn 7 Foraging XP per item collected (toggleable).
- **Botanist** (Level 10) — All auto-collected forage is upgraded to **Iridium quality**.
- **Gatherer** (Level 5) — 20% chance to **double-harvest** each item.

### Quality Scaling

Forage quality is determined by the same formula the game uses when the player picks up forage by hand:

| Foraging Level | Possible Quality |
|---|---|
| 0–3 | Normal only |
| 4–7 | Chance of Silver (level/15) |
| 8–9 | Chance of Silver or Gold (level/15, level/30) |
| 10 + Botanist | Always Iridium |

This means as your Foraging skill improves, the Auto Forager's output improves naturally — no machine upgrades needed.

## Ginger Island Isolation

**Ginger Island collection is geographically isolated from the mainland.** This is hard-coded behavior and cannot be toggled off.

- A machine placed on the **mainland** will only collect from mainland locations.
- A machine placed on **Ginger Island** will only collect from Ginger Island locations.

This means that **even if Progression Restrictions are turned off**, you still need **two separate Auto Forager machines** — one on the mainland and one on Ginger Island — to harvest forage from both areas. A single machine cannot cover the entire world.

A location is considered "Ginger Island" if its internal name starts with `Island` (e.g., `IslandWest`, `IslandSouth`, `IslandNorth`, etc.).

## Configuration (GMCM)

All settings are accessible through [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098). They are also stored in `config.json` and can be edited manually.

### Forage Types

| Setting | Default | Description |
|---|---|---|
| **Ground Forageables** | `true` | Collect items found on the ground (Daffodils, Leeks, Clams, etc.) |
| **Forage Crops** | `true` | Collect forage crops (Spring Onions, Ginger) |
| **Harvestable Bushes** | `true` | Collect from harvestable bushes (Salmonberries, Blackberries, Green Tea Leaves) |

### Perk Integration

| Setting | Default | Description |
|---|---|---|
| **Grant Foraging XP** | `true` | Player earns Foraging XP for each auto-collected item |
| **Apply Botanist Perk** | `true` | If the player has the Botanist perk (Foraging 10), all auto-collected forage is upgraded to Iridium quality |
| **Apply Gatherer Perk** | `true` | If the player has the Gatherer perk (Foraging 5), there is a 20% chance to double-harvest each item |

### Location Regions

Each region can be independently enabled or disabled. When a region is disabled, the Auto Forager will skip all locations in that region entirely.

| Setting | Default | Locations Covered |
|---|---|---|
| **Farm & Surroundings** | `true` | Farm, FarmCave, Greenhouse |
| **Pelican Town** | `true` | Town and surrounding buildings |
| **Forest & Backwoods** | `true` | Forest, Backwoods |
| **Secret Woods** | `true` | Secret Woods (requires Steel Axe when progression is enforced) |
| **Beach & Tide Pools** | `true` | Beach and Tide Pools (bridge repair required when progression is enforced) |
| **Mountain & Lake** | `true` | Mountain, Quarry, Mines area (landslide cleared Spring 5 Year 1 when progression is enforced) |
| **Railroad & Bathhouse** | `true` | Railroad area (rubble cleared Summer 3 Year 1 when progression is enforced) |
| **Desert** | `true` | Desert (requires Vault/Joja bus repair when progression is enforced) |
| **Ginger Island** | `true` | All Ginger Island outdoor locations (requires boat repair when progression is enforced) |
| **Sewer** | `true` | Sewer (requires Rusty Key when progression is enforced) |

### Progression

| Setting | Default | Description |
|---|---|---|
| **Enforce Location Progression** | `true` | When enabled, the Auto Forager can only collect from locations the player has actually unlocked. When disabled, all regions are accessible regardless of game progress (except for Ginger Island isolation — see above). |

**Progression checks include:**

- **Mountain/Mines** — Landslide cleared (Spring 5, Year 1)
- **Railroad** — Rubble cleared (Summer 3, Year 1)
- **Secret Woods** — Player owns a Steel Axe or better
- **Desert** — Vault bundle completed or Joja membership purchased
- **Sewer** — Player has the Rusty Key
- **Ginger Island** — Willy's boat repaired
- **Island North** — First parrot paid
- **Island West/Farm** — Turtle parrot paid (10 Golden Walnuts)

### Multiplayer

| Setting | Default | Description |
|---|---|---|
| **Shared Machines** | `null` (follows money setting) | When enabled, all players share one forage pool — items are collected once for everyone. When disabled, each player's machine collects independently. By default, this follows the game's shared money setting. |

### Hotkeys

| Setting | Default | Description |
|---|---|---|
| **View Daily Log** | `F10` | Press to display a summary of the previous day's forage collection, listing each item, quantity, quality, and source location. |

## Compatibility

- **Stardew Valley** 1.6.x
- **SMAPI** 4.0.0+
- **Chests Anywhere** — Fully compatible. Auto Forager machines appear in the Chests Anywhere overlay automatically.
- **Multiplayer** — Supported. See the Shared Machines setting above.
- **Mod-added locations** — Locations added by other mods that are not recognized by the region mapper are enabled by default and will be collected from unless progression restrictions block them.

## Source

[GitHub](https://github.com/tbonehunter/AutoForager)

## License

See [LICENSE](LICENSE) for details.
