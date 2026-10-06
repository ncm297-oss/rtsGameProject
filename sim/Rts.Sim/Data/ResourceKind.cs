namespace Rts.Sim.Data;

/// <summary>The two resources (docs/02 "Economy"): what a resource node yields.</summary>
/// <remarks>Order matches <see cref="DataLimits.ResourceKindIds"/>, which holds the JSON spelling.</remarks>
public enum ResourceKind
{
    /// <summary>Gold, from mines.</summary>
    Gold = 0,
    /// <summary>Wood, from trees.</summary>
    Wood = 1,
}
