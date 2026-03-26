// Framework/RoamingEntity.cs - Cosmetic roaming entity for the Auto Forager.
// When the player enters an outdoor location between 7 AM and 6 PM, there is
// a configurable chance the entity spawns. It ambles between actual forageable
// positions at NPC walking speed, pausing briefly at each one as if collecting,
// then wanders off-screen. Purely cosmetic — collection runs at 7 AM.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace AutoForager.Framework;

/// <summary>
/// Manages the cosmetic roaming entity that appears in locations during the day.
/// Moves at NPC walking speed between forageable items, pauses at each one.
/// </summary>
public class RoamingEntity
{
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly ModConfig _config;

    // Entity state
    private bool _isActive;
    private string _currentLocationName = "";
    private Vector2 _position;       // tile coordinates
    private Vector2 _targetPosition;  // tile coordinates
    private string _ownerName = "";

    // Movement: ~2 pixels per tick at 60 tps → ~1.9 tiles/sec (NPC walk pace)
    private const float PixelsPerTick = 2f;
    private const float TileSpeed = PixelsPerTick / 64f;

    // How many waypoints (forageable spots) the entity visits before exiting
    private const int MaxWaypoints = 10;

    // Pause at each waypoint: ~60 ticks = 1 second (looks like picking up item)
    private int _pauseTimer;
    private const int PauseTicksAtForage = 60;

    // Route
    private readonly Queue<Vector2> _route = new();

    // Rendering
    private Texture2D? _sprite;
    private readonly Rectangle _sourceRect = new(0, 0, 16, 16);
    private bool _facingRight = true;

    public RoamingEntity(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        _helper  = helper;
        _monitor = monitor;
        _config  = config;
    }

    /// <summary>Register event handlers for the roaming entity.</summary>
    public void Register()
    {
        _helper.Events.Player.Warped          += OnWarped;
        _helper.Events.GameLoop.UpdateTicked  += OnUpdateTicked;
        _helper.Events.Display.RenderedWorld  += OnRenderedWorld;
        _helper.Events.GameLoop.DayStarted    += OnDayStarted;
        _helper.Events.GameLoop.DayEnding     += OnDayEnding;
        _helper.Events.GameLoop.TimeChanged   += OnTimeChanged;
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _isActive = false;
        _ownerName = Game1.player.Name;
        _route.Clear();

        try
        {
            _sprite = _helper.ModContent.Load<Texture2D>("assets/auto-forager-entity.png");
        }
        catch
        {
            _sprite = null;
        }
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        _isActive = false;
        _route.Clear();
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        // Despawn before 7 AM (not yet departed) or at/after 6 PM (returned)
        if (e.NewTime < 700 || e.NewTime >= 1800)
        {
            _isActive = false;
            _route.Clear();
        }
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        _isActive = false;

        if (!e.IsLocalPlayer || !Context.IsWorldReady)
            return;

        // Only active between 7 AM and 6 PM
        if (Game1.timeOfDay < 700 || Game1.timeOfDay >= 1800)
            return;

        // Roll for sighting (entity currently disabled)
        int chance = 0;
        if (Game1.random.Next(100) >= chance)
            return;

        // Only outdoors, non-mine locations
        if (!e.NewLocation.IsOutdoors)
            return;

        SpawnInLocation(e.NewLocation);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!_isActive || !Context.IsWorldReady)
            return;

        if (Game1.currentLocation?.Name != _currentLocationName)
        {
            _isActive = false;
            return;
        }

        // Pausing at a waypoint (picking up forage)
        if (_pauseTimer > 0)
        {
            _pauseTimer--;
            return;
        }

        float distRemaining = Vector2.Distance(_position, _targetPosition);

        if (distRemaining < TileSpeed)
        {
            _position = _targetPosition;

            if (_route.Count > 0)
            {
                _targetPosition = _route.Dequeue();
                _facingRight = _targetPosition.X >= _position.X;
                // Pause at each stop to "collect"
                _pauseTimer = PauseTicksAtForage;
            }
            else
            {
                // Done — entity finishes its rounds and disappears
                _isActive = false;
            }
        }
        else
        {
            var direction = _targetPosition - _position;
            direction.Normalize();
            _position += direction * TileSpeed;
        }
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!_isActive || _sprite == null)
            return;

        if (Game1.currentLocation?.Name != _currentLocationName)
            return;

        var screenPos = Game1.GlobalToLocal(Game1.viewport, _position * 64f);

        e.SpriteBatch.Draw(
            _sprite,
            screenPos,
            _sourceRect,
            Color.White,
            0f,
            Vector2.Zero,
            4f, // scale: 16px * 4 = 64px tile
            _facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally,
            (_position.Y * 64f + 32f) / 10000f
        );

        // Draw name label above the entity
        var nameText = _helper.Translation.Get("entity.name", new { name = _ownerName });
        var nameSize = Game1.smallFont.MeasureString(nameText);
        var namePos = new Vector2(
            screenPos.X + 32f - nameSize.X / 2f,
            screenPos.Y - nameSize.Y - 4f
        );
        e.SpriteBatch.DrawString(
            Game1.smallFont,
            nameText,
            namePos,
            Color.White
        );
    }

    // -------------------------------------------------------------------------
    // Spawn & route generation
    // -------------------------------------------------------------------------

    private void SpawnInLocation(GameLocation location)
    {
        _currentLocationName = location.Name;
        _route.Clear();
        _pauseTimer = 0;

        // Gather actual forageable positions in this location
        var forageSpots = new List<Vector2>();

        // Ground forageables
        foreach (var kvp in location.Objects.Pairs)
        {
            if (kvp.Value is StardewValley.Object obj
                && (obj.Category == -81 || obj.isForage()))
            {
                forageSpots.Add(kvp.Key);
            }
        }

        // Forage crops (spring onion, ginger)
        foreach (var kvp in location.terrainFeatures.Pairs)
        {
            if (kvp.Value is HoeDirt dirt
                && dirt.crop != null
                && dirt.crop.forageCrop.Value
                && dirt.readyForHarvest())
            {
                forageSpots.Add(kvp.Key);
            }
        }

        // Harvestable bushes
        foreach (var feature in location.largeTerrainFeatures)
        {
            if (feature is Bush bush && bush.readyForHarvest())
                forageSpots.Add(bush.Tile);
        }

        // Shuffle and take up to MaxWaypoints
        ShuffleList(forageSpots);
        int waypointCount = Math.Min(forageSpots.Count, MaxWaypoints);

        // If too few forageables, pad with a couple of random walkable tiles
        // so the entity still wanders a bit even in sparse locations
        if (waypointCount < 4)
        {
            int mapWidth  = location.Map.Layers[0].LayerWidth;
            int mapHeight = location.Map.Layers[0].LayerHeight;
            int needed = 4 - waypointCount;
            for (int i = 0; i < needed; i++)
                forageSpots.Add(GetRandomWalkableTile(location, mapWidth, mapHeight));
            waypointCount = Math.Min(forageSpots.Count, MaxWaypoints);
        }

        if (waypointCount == 0)
            return;

        // Start near the first waypoint but offset slightly
        _position = forageSpots[0] + new Vector2(Game1.random.Next(-2, 3), Game1.random.Next(-2, 3));
        _targetPosition = forageSpots[0];

        for (int i = 1; i < waypointCount; i++)
            _route.Enqueue(forageSpots[i]);

        _facingRight = _targetPosition.X >= _position.X;
        _isActive = true;

        _monitor.Log(
            _helper.Translation.Get("log.entity.spawned", new { name = location.Name }),
            LogLevel.Trace);
    }

    /// <summary>Fisher-Yates shuffle.</summary>
    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Game1.random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Returns a random passable tile within the map, used as padding when
    /// the location has very few forageables.
    /// </summary>
    private static Vector2 GetRandomWalkableTile(GameLocation location, int mapWidth, int mapHeight)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            int x = Game1.random.Next(2, mapWidth - 2);
            int y = Game1.random.Next(2, mapHeight - 2);
            if (location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport))
                return new Vector2(x, y);
        }
        return new Vector2(Game1.random.Next(2, mapWidth - 2), Game1.random.Next(2, mapHeight - 2));
    }
}
