using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>One zone disc the view draws on the ground (M4-V6c): which zone, where, how wide, how opaque, and whose.</summary>
/// <param name="Slot">The zone's slot in <c>World.Zones</c> (its owner, ability and ticks left are read there).</param>
/// <param name="Center">The zone's centre (x, z) in meters.</param>
/// <param name="Radius">The zone's radius in meters (its ability's <c>radius</c>).</param>
/// <param name="Alpha">The disc's opacity: <see cref="ZoneDiscs.DiscAlpha"/>, falling to 0 over the zone's last <see cref="ZoneDiscs.FadeTicks"/> ticks.</param>
/// <param name="Own">Whether the zone is the viewing player's own.</param>
public readonly record struct ZoneDisc(int Slot, Vector2 Center, float Radius, float Alpha, bool Own);
