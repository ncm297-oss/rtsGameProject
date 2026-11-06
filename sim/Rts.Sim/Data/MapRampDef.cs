namespace Rts.Sim.Data;

/// <summary>
/// M6-S7: one ramp of a hand-made map: a rectangle of cells (left-top <paramref name="X"/>, <paramref name="Y"/>, in cells)
/// on one level that slopes up to the next level toward <paramref name="Up"/>; its length along the slope is
/// <paramref name="Height"/> for north / south and <paramref name="Width"/> for east / west.
/// </summary>
/// <param name="X">The left column.</param>
/// <param name="Y">The top row.</param>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
/// <param name="Up">The side the higher level lies on.</param>
public readonly record struct MapRampDef(int X, int Y, int Width, int Height, MapDirection Up);
