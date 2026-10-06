namespace Rts.Sim.Data;

/// <summary>The ten template building slots every faction fills (docs/02 "Buildings").</summary>
/// <remarks>Order matches <see cref="DataLimits.BuildingSlotIds"/>, which holds the JSON spelling. M3-2 ships the Town Hall only.</remarks>
public enum BuildingSlot
{
    /// <summary>Drop-off, population, trains workers, researches Age II.</summary>
    TownHall = 0,
    /// <summary>Population.</summary>
    House = 1,
    /// <summary>Forward drop-off.</summary>
    Camp = 2,
    /// <summary>Trains the Line slot.</summary>
    InfantryHall = 3,
    /// <summary>Trains the Ranged slot.</summary>
    RangedHall = 4,
    /// <summary>Trains the Shock slot.</summary>
    ShockHall = 5,
    /// <summary>Upgrades.</summary>
    Forge = 6,
    /// <summary>Trains the Caster slot.</summary>
    CasterHall = 7,
    /// <summary>Trains siege.</summary>
    SiegeWorks = 8,
    /// <summary>Static defense and detection.</summary>
    WatchTower = 9,
}
