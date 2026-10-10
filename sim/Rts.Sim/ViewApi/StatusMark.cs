namespace Rts.Sim.ViewApi;

/// <summary>One status marker the view draws over a unit (M4-V6b): which unit, which status, and its place in the unit's row.</summary>
/// <param name="Unit">The unit slot.</param>
/// <param name="Status">Status id (index into <c>GameData.Statuses</c>).</param>
/// <param name="Place">Its place in the unit's row of markers, 0 first (the status applied first).</param>
/// <param name="Row">How many markers the unit's row has.</param>
public readonly record struct StatusMark(int Unit, int Status, int Place, int Row);
