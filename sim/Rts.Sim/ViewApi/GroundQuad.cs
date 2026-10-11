namespace Rts.Sim.ViewApi;

/// <summary>One terrain cell's drawn top surface under a ground disc: the cell and its four corner heights in meters (<see cref="TerrainHeight.CellCorners"/>'s order).</summary>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="H00">Height at the cell's (min x, min y) corner.</param>
/// <param name="H10">Height at its (max x, min y) corner.</param>
/// <param name="H01">Height at its (min x, max y) corner.</param>
/// <param name="H11">Height at its (max x, max y) corner.</param>
public readonly record struct GroundQuad(int X, int Y, float H00, float H10, float H01, float H11);
