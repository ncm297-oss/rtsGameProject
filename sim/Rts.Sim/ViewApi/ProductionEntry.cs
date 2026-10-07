namespace Rts.Sim.ViewApi;

/// <summary>One button of a building's production card (M3-V3): a unit to train or a tech to research.</summary>
/// <param name="TypeId">The unit type id, or the tech id when <paramref name="IsTech"/>.</param>
/// <param name="IsTech">True for a tech (<c>Command.Research</c>), false for a unit (<c>Command.Train</c>).</param>
public readonly record struct ProductionEntry(int TypeId, bool IsTech);
