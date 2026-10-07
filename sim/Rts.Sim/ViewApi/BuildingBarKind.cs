namespace Rts.Sim.ViewApi;

/// <summary>Which bar a building view shows over its box (M3-V1).</summary>
public enum BuildingBarKind : byte
{
    /// <summary>No bar: a finished building at full hit points.</summary>
    None = 0,

    /// <summary>Construction progress of a site: <c>Work / WorkNeeded</c>.</summary>
    Progress = 1,

    /// <summary>Hit points of a damaged finished building: <c>Hp / max</c>.</summary>
    HitPoints = 2,
}
