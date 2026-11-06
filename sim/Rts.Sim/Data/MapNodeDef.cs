namespace Rts.Sim.Data;

/// <summary>M6-S7: one resource node of a hand-made map: its resource type, anchor cell (<c>y * Size + x</c>) and the amount it holds.</summary>
/// <param name="TypeId">The resource type (<see cref="GameData.Resources"/>): the first of its kind, as the generator places.</param>
/// <param name="Cell">The footprint's anchor (top-left) cell.</param>
/// <param name="Amount">Gold for a mine (the file's), wood for a tree (<c>rules.json</c> <c>treeWood</c>).</param>
public readonly record struct MapNodeDef(int TypeId, int Cell, int Amount);
