namespace Rts.Sim.Map;

/// <summary>One player's start location (M5-4a): the centre cell (<c>y * Width + x</c>) of an open, flat disc and its elevation level.</summary>
/// <param name="Cell">The disc's centre cell.</param>
/// <param name="Level">The level of every cell of the disc.</param>
public readonly record struct StartLocation(int Cell, int Level);
