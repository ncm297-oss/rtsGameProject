namespace Rts.Sim.ViewApi;

/// <summary>
/// The drawn size of the resource-node props (M3-V4, BUG-0125), as the view's <c>PropsView</c> builds them, so
/// <see cref="ResourcePicker.PickRay"/> tests the shape a pixel shows rather than the footprint's column. Meters.
/// </summary>
/// <remarks>
/// A wood node is a trunk cylinder of <see cref="TrunkRadius"/> up to <see cref="TrunkHeight"/>, then a cone whose base
/// radius is <see cref="CanopyFill"/> x half the footprint's smaller side and whose tip is at <see cref="TreeHeight"/>. A
/// gold node is a block over the whole footprint up to <see cref="MineHeight"/> with a centred block of
/// <see cref="GoldFill"/> x the footprint's sides on top, <see cref="GoldHeight"/> tall. Both stand on the terrain height
/// at the footprint's centre.
/// </remarks>
/// <param name="TrunkHeight">Tree trunk height.</param>
/// <param name="TrunkRadius">Tree trunk radius.</param>
/// <param name="TreeHeight">Tree tip above the ground.</param>
/// <param name="CanopyFill">Share of the footprint's smaller side the canopy's base diameter takes.</param>
/// <param name="MineHeight">Mine block height.</param>
/// <param name="GoldHeight">Height of the gold block on top of a mine.</param>
/// <param name="GoldFill">Share of the footprint's sides the gold block takes.</param>
public readonly record struct PropShape(float TrunkHeight, float TrunkRadius, float TreeHeight, float CanopyFill,
    float MineHeight, float GoldHeight, float GoldFill);
