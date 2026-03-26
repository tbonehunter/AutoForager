// Framework/LocationRestrictions.cs - Progression restriction checks for Auto Forager.
// Adapted from CheatAnon's WarpRestrictions.cs. Determines which location
// regions are accessible based on the player's current game progression.
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;

namespace AutoForager.Framework;

/// <summary>
/// Checks whether the player meets progression requirements for various
/// location regions. Used to gate which areas the Auto Forager can visit.
/// </summary>
internal static class LocationRestrictions
{
    /// <summary>Check if a given region name is accessible, respecting config overrides.</summary>
    /// <param name="regionKey">The region key (e.g., "Farm", "Desert", "Island").</param>
    /// <param name="enforceRestrictions">Whether progression restrictions are active.</param>
    public static bool CanAccessRegion(string regionKey, bool enforceRestrictions)
    {
        if (!enforceRestrictions)
            return true;

        return regionKey switch
        {
            "Farm"     => true, // always accessible
            "Town"     => true, // always accessible
            "Forest"   => true, // forest itself is always accessible; Woods sub-gated separately
            "Beach"    => true, // beach itself is always accessible; Tide Pools sub-gated
            "Mountain" => CanAccessMines(),
            "Railroad" => CanAccessRailroadArea(),
            "Desert"   => CanAccessDesert(),
            "Island"   => CanAccessGingerIsland(),
            "Sewer"    => CanAccessSewer(),
            "Woods"    => CanAccessSecretWoods(),
            _          => true
        };
    }

    /// <summary>Check if a specific location name is accessible given its region context.</summary>
    /// <param name="locationName">The internal game location name.</param>
    /// <param name="enforceRestrictions">Whether progression restrictions are active.</param>
    public static bool CanAccessLocation(string locationName, bool enforceRestrictions)
    {
        if (!enforceRestrictions)
            return true;

        return locationName switch
        {
            // Mines area (landslide cleared Spring 5 Year 1)
            "Mine" => CanAccessMines(),

            // Desert locations
            "Desert" or "SandyHouse" or "Club" => CanAccessDesert(),

            // Skull Cavern
            "SkullCave" => CanAccessSkullCavern(),

            // Ginger Island - base access
            "IslandEast" or "IslandHut" or "LeoTreeHouse" or
            "IslandSouth" or "IslandSouthEast" or "IslandShrine" => CanAccessGingerIsland(),

            // Ginger Island - North (Leo's hut parrot paid)
            "IslandNorth" or "IslandFieldOffice" or "Caldera" => CanAccessIslandNorth(),

            // Ginger Island - West / Farm (10 Golden Walnuts)
            "IslandWest" or "IslandFarmHouse" or "IslandFarmCave" or
            "QiNutRoom" => CanAccessIslandWest(),

            // Secret Woods
            "Woods" => CanAccessSecretWoods(),

            // Sewer
            "Sewer" => CanAccessSewer(),

            // Railroad area
            "Railroad" or "BathHouse_Entry" or "BathHouse_MensLocker" or
            "BathHouse_WomensLocker" or "BathHouse_Pool" => CanAccessRailroadArea(),

            // Quarry (Crafts Room or Joja)
            "Mountain" => CanAccessMines(),

            // All other locations are unrestricted
            _ => true
        };
    }

    // -------------------------------------------------------------------------
    // Individual restriction checks
    // -------------------------------------------------------------------------

    public static bool CanAccessDesert()
    {
        return Game1.player.mailReceived.Contains("ccVault")
            || Game1.player.mailReceived.Contains("JojaMember");
    }

    public static bool CanAccessSkullCavern()
    {
        return CanAccessDesert() && Game1.player.hasSkullKey;
    }

    public static bool CanAccessGingerIsland()
    {
        return Game1.player.hasOrWillReceiveMail("willyBoatFixed");
    }

    public static bool CanAccessIslandNorth()
    {
        return CanAccessGingerIsland()
            && Game1.player.hasOrWillReceiveMail("Island_FirstParrot");
    }

    public static bool CanAccessIslandWest()
    {
        return CanAccessGingerIsland()
            && Game1.player.hasOrWillReceiveMail("Island_Turtle");
    }

    public static bool CanAccessSecretWoods()
    {
        if (Game1.player.getToolFromName("Axe") is Axe axe)
            return axe.UpgradeLevel >= Tool.steel;
        return false;
    }

    public static bool CanAccessSewer()
    {
        return Game1.player.hasRustyKey;
    }

    public static bool CanAccessRailroadArea()
    {
        if (Game1.year > 1)
            return true;
        if (Game1.year == 1)
        {
            if (Game1.currentSeason == "summer" && Game1.dayOfMonth >= 3)
                return true;
            if (Game1.currentSeason is "fall" or "winter")
                return true;
        }
        return false;
    }

    public static bool CanAccessMines()
    {
        if (Game1.year > 1)
            return true;
        if (Game1.year == 1)
        {
            if (Game1.currentSeason == "spring" && Game1.dayOfMonth >= 5)
                return true;
            if (Game1.currentSeason != "spring")
                return true;
        }
        return false;
    }

    public static bool CanAccessTidePools()
    {
        if (Game1.getLocationFromName("Beach") is Beach beach)
            return beach.bridgeFixed.Value;
        return false;
    }

    public static bool CanAccessQuarry()
    {
        return Game1.player.mailReceived.Contains("ccCraftsRoom")
            || Game1.player.mailReceived.Contains("JojaMember");
    }
}
