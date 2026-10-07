namespace Rts.Sim.Economy;

/// <summary>Why <see cref="World.CanPlace"/> refuses a building placement (M3-3); the first rule broken, in this order.</summary>
public enum PlacementError
{
    /// <summary>The placement is legal.</summary>
    None = 0,

    /// <summary>No building type has that id.</summary>
    UnknownType = 1,

    /// <summary>The type belongs to another faction than the player's (or the player doesn't exist).</summary>
    WrongFaction = 2,

    /// <summary>Part of the footprint lies outside the map.</summary>
    OffMap = 3,

    /// <summary>A footprint cell is blocked (cliff, border, resource node, building), a ramp, or on another level than the anchor.</summary>
    Blocked = 4,

    /// <summary>Taking the footprint would cut some passable cells off from others (BUG-0078: a placement never seals ground).</summary>
    SealsGround = 5,

    /// <summary>An enemy unit, or one of the player's own units holding position, has its center in the footprint.</summary>
    UnitInTheWay = 6,

    /// <summary>The player has less gold or wood than the type costs.</summary>
    CannotAfford = 7,

    /// <summary>The building store has no free slot.</summary>
    StoreFull = 8,
}
