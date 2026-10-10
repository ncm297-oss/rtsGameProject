using System;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim;

/// <summary>Match setup that sizes every sim buffer up front, so ticks never grow arrays.</summary>
/// <param name="Seed">The single seed every RNG stream derives from.</param>
/// <param name="PlayerCount">Number of players (human and AI), indexed from 0.</param>
/// <param name="UnitCapacity">Maximum number of live units; the unit store never grows past it.</param>
/// <param name="CommandCapacity">Maximum number of commands that can be pending at once.</param>
public sealed record SimConfig(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity)
{
    /// <summary>Loaded unit, faction and rules data; every stat the sim uses comes from here (CLAUDE.md rule 6).</summary>
    public required GameData Data { get; init; }

    /// <summary>Terrain generator and resource placer settings; defaults to the docs/02 128 x 128 map without resources.</summary>
    public MapGenParams Map { get; init; } = MapGenParams.Default;

    /// <summary>Maximum number of live resource nodes (trees, mines); the store is allocated once at this size and refuses spawns past it. Default <see cref="Entities.ResourceStore.DefaultCapacity"/> (4,096).</summary>
    public int ResourceCapacity { get; init; } = Entities.ResourceStore.DefaultCapacity;

    /// <summary>Maximum number of live buildings; the store is allocated once at this size and refuses spawns past it. Default <see cref="Entities.BuildingStore.DefaultCapacity"/> (256).</summary>
    public int BuildingCapacity { get; init; } = Entities.BuildingStore.DefaultCapacity;

    /// <summary>
    /// Maximum number of projectiles in flight (M4-2b); the store is allocated once at this size, and a shot fired while it
    /// is full is lost. 0 (the default) sizes it from the data (<see cref="ProjectileSlots"/>). Not in the replay header: a
    /// replay plays with the default, and a recorder refuses a sim with another size (BUG-0181).
    /// </summary>
    public int ProjectileCapacity { get; init; }

    /// <summary>
    /// Maximum number of live zones (M4-4b-2); the store is allocated once at this size, and a zone cast while it is full is
    /// not made (the cast still resolves). Default <see cref="Abilities.ZoneStore.DefaultCapacity"/> (64). Not in the replay
    /// header: a replay plays with the default.
    /// </summary>
    public int ZoneCapacity { get; init; } = Abilities.ZoneStore.DefaultCapacity;

    /// <summary>
    /// The projectile store's size: <see cref="ProjectileCapacity"/>, or by default as many shooters as the players'
    /// population caps allow (<c>players x rules.popCap / the smallest pop of a unit with a projectile</c>, at most
    /// <see cref="UnitCapacity"/>, at least 1): every shipped shot lands before its shooter's next one (flight shorter than
    /// the cooldown, pinned by a test), so a shooter has at most one in the air. Dev spawns past the cap can outnumber it;
    /// a scene that does sets the capacity.
    /// </summary>
    public int ProjectileSlots
    {
        get
        {
            if (ProjectileCapacity > 0) return ProjectileCapacity;
            int smallest = int.MaxValue;
            foreach (UnitDef u in Data.Units)
                if (u.Attack.ProjectileTypeId >= 0 && u.HalfPop < smallest) smallest = Math.Max(u.HalfPop, 1);
            if (smallest == int.MaxValue) return 1;
            long shooters = (long)PlayerCount * Data.Rules.HalfPopCap / smallest;
            return (int)Math.Clamp(shooters, 1, UnitCapacity);
        }
    }

    /// <summary>
    /// Whether units fight (M4-1, default true): target scans, swings, damage and death. False turns the three combat
    /// steps off and an attack-move sets no combat mode, so a scene of two owners plays as it did before M4 (enemies are
    /// only walls). A test and tooling switch for the pre-M4 movement, economy and production scenes (BUG-0135, Producer
    /// decision 2026-10-07); a match always fights. Not in the replay header yet (M4-2 format 4): <c>ReplayPlayer</c>
    /// plays back with combat on unless its caller passes <c>combat: false</c>.
    /// </summary>
    public bool Combat { get; init; } = true;

    /// <summary>Throws if any size is out of range.</summary>
    public void Validate()
    {
        if (PlayerCount < 1) throw new ArgumentOutOfRangeException(nameof(PlayerCount));
        if (UnitCapacity < 1) throw new ArgumentOutOfRangeException(nameof(UnitCapacity));
        if (CommandCapacity < 1) throw new ArgumentOutOfRangeException(nameof(CommandCapacity));
        if (ResourceCapacity < 1) throw new ArgumentOutOfRangeException(nameof(ResourceCapacity));
        if (BuildingCapacity < 1) throw new ArgumentOutOfRangeException(nameof(BuildingCapacity));
        if (ProjectileCapacity < 0) throw new ArgumentOutOfRangeException(nameof(ProjectileCapacity));
        if (ZoneCapacity < 1) throw new ArgumentOutOfRangeException(nameof(ZoneCapacity));
        if (Data == null) throw new ArgumentNullException(nameof(Data));
        if (Map == null) throw new ArgumentNullException(nameof(Map));
        Map.Validate();
    }
}
