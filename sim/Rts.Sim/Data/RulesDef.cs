namespace Rts.Sim.Data;

/// <summary>Global economy rules from <c>common/rules.json</c> (docs/02 "Economy"), converted to sim units.</summary>
public sealed class RulesDef
{
    internal RulesDef()
    {
    }

    /// <summary>Gold each player starts with.</summary>
    public int StartingGold { get; init; }
    /// <summary>Wood each player starts with.</summary>
    public int StartingWood { get; init; }
    /// <summary>Workers each player starts with.</summary>
    public int StartingWorkers { get; init; }
    /// <summary>Hard population cap, in half-pop units (100 pop = 200).</summary>
    public int HalfPopCap { get; init; }
    /// <summary>Resources a worker carries per trip.</summary>
    public int WorkerCarry { get; init; }
    /// <summary>Gold gathered per tick while working a mine.</summary>
    public float GoldPerTick { get; init; }
    /// <summary>Wood gathered per tick while working a tree.</summary>
    public float WoodPerTick { get; init; }
    /// <summary>Gold mines at each start location.</summary>
    public int StartMineCount { get; init; }
    /// <summary>Gold in each start-location mine.</summary>
    public int StartMineGold { get; init; }
    /// <summary>Gold mines at each expansion.</summary>
    public int ExpansionMineCount { get; init; }
    /// <summary>Gold in each expansion mine.</summary>
    public int ExpansionMineGold { get; init; }
    /// <summary>Wood in each tree.</summary>
    public int TreeWood { get; init; }
    /// <summary>Meters a worker searches for a new node of the same type when its node is depleted.</summary>
    public float NodeSearchRadius { get; init; }
    /// <summary>Repair speed as a fraction of the one-builder build rate (docs/02 "Buildings": 50%), above 0 and at most 1.</summary>
    public float RepairRateFactor { get; init; }
    /// <summary>Repair cost as a fraction of the building's cost for its full hit points, scaled by damage (docs/02: 25%), above 0 and at most 1.</summary>
    public float RepairCostFactor { get; init; }
}
