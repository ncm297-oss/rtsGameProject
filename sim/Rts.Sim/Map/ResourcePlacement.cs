namespace Rts.Sim.Map;

/// <summary>What the resource placer put on the map at world construction (M3-1); the counts can fall short of the request.</summary>
/// <param name="Forests">Forests placed (of <see cref="MapGenParams.Forests"/>).</param>
/// <param name="Trees">Trees in those forests.</param>
/// <param name="Mines">Gold mines placed (of <see cref="MapGenParams.GoldMines"/>).</param>
public readonly record struct ResourcePlacement(int Forests, int Trees, int Mines);
