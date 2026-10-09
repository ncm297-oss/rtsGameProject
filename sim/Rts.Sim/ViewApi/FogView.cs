using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The local player's fog as the view draws it (M4-V4, docs/03 "Implementation (M4-V4)"): the fog texture's bytes, and
/// which unit and building slots the screen shows. Read-only on <see cref="FogStore"/>; view state only; allocation-free
/// after construction.
/// </summary>
/// <remarks>
/// <para><b>Texture.</b> <see cref="Texture"/> is a copy of <c>Fog.Visibility(player)</c>, one byte a cell, row-major
/// (0 unexplored, 1 explored, 2 visible): the R8 texture the terrain shader samples. <see cref="PackTexture"/> copies it
/// only when <c>Fog.Version(player)</c> moved since the last copy (at most once per fog update, every 4 ticks), and counts
/// the copies in <see cref="Uploads"/>.</para>
/// <para><b>Hide rule.</b> <see cref="Refresh"/> fills <see cref="UnitShown"/> with <c>Fog.CanSeeUnit(player, slot)</c> (an own
/// live unit, or an enemy whose cell was visible at the last update, or one revealed by a high-ground hit) and
/// <see cref="BuildingShown"/> with <c>Fog.CanSeeBuilding(player, slot)</c>. Both change only when a tick runs, so a call
/// with the tick number of the last call does nothing: every view may call it each frame.</para>
/// <para><b>Disabled</b> (the <c>--no-fog</c> dev flag): the texture is all <see cref="VisionConstants.Visible"/> and every live
/// slot is shown, the pre-M4-V4 screen.</para>
/// <para><b>Ghosts</b> (M4-V5): <see cref="GhostShown"/> marks the slots whose last-known enemy building
/// (<c>Fog.Ghosts(player)</c>) isn't drawn now; the views draw those darkened at the remembered anchor and a right-click
/// on one is an Attack on its remembered handle. None when disabled.</para>
/// </remarks>
public sealed class FogView
{
    /// <summary>Brightness of explored ground (and of what stands on it) against visible ground: drawn at 40 %.</summary>
    public const float ExploredBrightness = 0.4f;

    private readonly byte[] _texture;
    private readonly bool[] _unitShown;
    private readonly bool[] _buildingShown;
    private readonly bool[] _ghostShown;
    private readonly BuildingGhost[] _ghosts;

    /// <summary>Sizes the texture for a <paramref name="width"/> x <paramref name="height"/> cell map and the slot lists for the stores' capacities.</summary>
    /// <param name="player">The local player whose fog is drawn.</param>
    /// <param name="enabled">False for <c>--no-fog</c>: everything drawn as visible.</param>
    public FogView(int width, int height, int unitCapacity, int buildingCapacity, int player, bool enabled = true)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "the map needs at least one cell");
        Width = width;
        Height = height;
        Player = player;
        Enabled = enabled;
        _texture = new byte[width * height];
        _unitShown = new bool[Math.Max(0, unitCapacity)];
        _buildingShown = new bool[Math.Max(0, buildingCapacity)];
        _ghostShown = new bool[_buildingShown.Length];
        _ghosts = new BuildingGhost[_buildingShown.Length];
        if (!enabled) Array.Fill(_texture, VisionConstants.Visible);
    }

    /// <summary>Map width in cells (the texture's width in texels).</summary>
    public int Width { get; }

    /// <summary>Map height in cells.</summary>
    public int Height { get; }

    /// <summary>The player whose fog this is.</summary>
    public int Player { get; }

    /// <summary>False under <c>--no-fog</c>: the texture says visible everywhere and every live slot is shown.</summary>
    public bool Enabled { get; }

    /// <summary>The fog texture's bytes as last packed: one per cell, row-major, the fog's own values.</summary>
    public byte[] Texture => _texture;

    /// <summary>The <c>Fog.Version(player)</c> the texture was last packed at; -1 before the first pack.</summary>
    public int TextureVersion { get; private set; } = -1;

    /// <summary>Times <see cref="PackTexture"/> copied the bytes (the view uploads the texture after each).</summary>
    public int Uploads { get; private set; }

    /// <summary>The tick number of the last <see cref="Refresh"/> that recomputed the slot lists; <see cref="int.MinValue"/> before the first.</summary>
    public int RefreshedTick { get; private set; } = int.MinValue;

    /// <summary>Per unit slot: drawn on screen (live and seen, or live under <c>--no-fog</c>).</summary>
    public ReadOnlySpan<bool> UnitShown => _unitShown;

    /// <summary>Per building slot: drawn on screen (live and seen, or live under <c>--no-fog</c>).</summary>
    public ReadOnlySpan<bool> BuildingShown => _buildingShown;

    /// <summary>Whether unit slot <paramref name="slot"/> is drawn; false out of range.</summary>
    public bool ShowsUnit(int slot) => (uint)slot < (uint)_unitShown.Length && _unitShown[slot];

    /// <summary>Whether building slot <paramref name="slot"/> is drawn; false out of range.</summary>
    public bool ShowsBuilding(int slot) => (uint)slot < (uint)_buildingShown.Length && _buildingShown[slot];

    /// <summary>
    /// Copies <c>fog.Visibility(Player)</c> into <see cref="Texture"/> when <c>fog.Version(Player)</c> differs from
    /// <see cref="TextureVersion"/> (the first call always copies); returns true when it copied, and the view then uploads
    /// the texture. Disabled: the first call returns true once (the all-visible texture is uploaded once), later calls false.
    /// </summary>
    public bool PackTexture(FogStore fog)
    {
        if (!Enabled)
        {
            if (TextureVersion >= 0) return false;
            TextureVersion = 0;
            Uploads++;
            return true;
        }
        int version = fog.Version(Player);
        if (version == TextureVersion) return false;
        ReadOnlySpan<byte> bytes = fog.Visibility(Player);
        int n = Math.Min(bytes.Length, _texture.Length);
        bytes.Slice(0, n).CopyTo(_texture);
        if (n < _texture.Length) Array.Clear(_texture, n, _texture.Length - n);
        TextureVersion = version;
        Uploads++;
        return true;
    }

    /// <summary>
    /// Recomputes <see cref="UnitShown"/> and <see cref="BuildingShown"/> for tick number <paramref name="tick"/> (the world's
    /// <c>TickNumber</c>); nothing when the last call was for the same tick (the answers only change when a tick runs).
    /// Returns true when it recomputed.
    /// </summary>
    /// <param name="fog">The world's fog.</param>
    /// <param name="tick">The world's tick number now.</param>
    /// <param name="unitAlive">The unit store's <c>Alive</c> (used when disabled).</param>
    /// <param name="buildingAlive">The building store's <c>Alive</c> (used when disabled).</param>
    public bool Refresh(FogStore fog, int tick, ReadOnlySpan<bool> unitAlive, ReadOnlySpan<bool> buildingAlive) =>
        Refresh(fog, tick, unitAlive, buildingAlive, ReadOnlySpan<int>.Empty);

    /// <summary>
    /// As the 4-argument overload, and also collects the ghosts (<see cref="CollectGhosts"/>) against the building store's
    /// <paramref name="buildingGeneration"/> (M4-V5): what the views call.
    /// </summary>
    public bool Refresh(FogStore fog, int tick, ReadOnlySpan<bool> unitAlive, ReadOnlySpan<bool> buildingAlive, ReadOnlySpan<int> buildingGeneration)
    {
        if (tick == RefreshedTick) return false;
        RefreshedTick = tick;
        bool[] units = _unitShown, buildings = _buildingShown;
        for (int i = 0; i < units.Length; i++)
            units[i] = Enabled ? fog.CanSeeUnit(Player, i) : i < unitAlive.Length && unitAlive[i];
        for (int i = 0; i < buildings.Length; i++)
            buildings[i] = Enabled ? fog.CanSeeBuilding(Player, i) : i < buildingAlive.Length && buildingAlive[i];
        CollectGhosts(fog, buildingGeneration);
        return true;
    }

    /// <summary>
    /// Whether a point in the world (sim meters) lies in a cell visible to <see cref="Player"/> at the last fog update: the
    /// projectile and impact-mark rule (a shot is drawn only from a visible cell, docs/03 "Known limits" (1)). Always true
    /// when disabled.
    /// </summary>
    public bool ShowsPoint(FogStore fog, Vector2 point) => !Enabled || fog.IsVisible(Player, CellOf(Width, Height, point));

    /// <summary>The cell holding <paramref name="point"/> (m) on a <paramref name="width"/> x <paramref name="height"/> map, clamped onto it, exactly as the fog maps a unit to its cell (a NaN or far-out coordinate clamps as the fog does).</summary>
    public static int CellOf(int width, int height, Vector2 point)
    {
        int x = (int)(point.X / MapConstants.CellSize), y = (int)(point.Y / MapConstants.CellSize);
        if (!(x >= 0)) x = 0;
        else if (x >= width) x = width - 1;
        if (!(y >= 0)) y = 0;
        else if (y >= height) y = height - 1;
        return y * width + x;
    }

    /// <summary>How bright a cell of fog state <paramref name="state"/> is drawn: 0 unexplored (black), <see cref="ExploredBrightness"/> explored, 1 visible (states above visible count as visible).</summary>
    public static float Brightness(byte state) => state switch
    {
        VisionConstants.Unexplored => 0f,
        VisionConstants.Explored => ExploredBrightness,
        _ => 1f,
    };

    /// <summary>The minimap fog layer's alpha (black over the terrain) for fog state <paramref name="state"/>: 255 unexplored, 153 explored (40 % shows through), 0 visible.</summary>
    public static byte MinimapAlpha(byte state) => MinimapRaster.ToByte(1f - Brightness(state));

    /// <summary>
    /// Per building slot: a last-known enemy building drawn as a ghost (M4-V5, docs/02 "Vision and fog of war"): the
    /// slot's entry in <c>fog.Ghosts(Player)</c> is known and that building isn't drawn now (the slot isn't shown, or it
    /// holds another generation). Filled by <see cref="CollectGhosts"/>; all false when disabled.
    /// </summary>
    public ReadOnlySpan<bool> GhostShown => _ghostShown;

    /// <summary>Per building slot, the remembered entry as last collected (generation, type, anchor cell, owner); meaningful where <see cref="GhostShown"/> is true.</summary>
    public ReadOnlySpan<BuildingGhost> Ghosts => _ghosts;

    /// <summary>How many ghosts the last <see cref="CollectGhosts"/> found (the true entries of <see cref="GhostShown"/>).</summary>
    public int GhostCount { get; private set; }

    /// <summary>Whether building slot <paramref name="slot"/> is drawn as a ghost; false out of range.</summary>
    public bool ShowsGhost(int slot) => (uint)slot < (uint)_ghostShown.Length && _ghostShown[slot];

    /// <summary>The handle an Attack on slot <paramref name="slot"/>'s ghost names (the slot with its remembered generation); default when the slot shows no ghost.</summary>
    public EntityHandle GhostHandle(int slot) => ShowsGhost(slot) ? new EntityHandle(slot, _ghosts[slot].Generation) : default;

    /// <summary>
    /// Fills <see cref="GhostShown"/> and <see cref="Ghosts"/> from <c>fog.Ghosts(Player)</c> and returns the count: a
    /// known entry is a ghost unless <see cref="BuildingShown"/> marks its slot and <paramref name="buildingGeneration"/>
    /// (the building store's <c>Generation</c>) says that slot still holds the remembered building (an empty span: shown
    /// alone hides it). Call after <see cref="Refresh"/> for the same tick (the 5-argument <see cref="Refresh(FogStore, int, ReadOnlySpan{bool}, ReadOnlySpan{bool}, ReadOnlySpan{int})"/>
    /// does). The sim's list changes only on a fog update tick, but the shown rule can change on any tick (a building
    /// dies in sight, a slot is reused), so this runs whenever the slot lists do. Allocation-free.
    /// </summary>
    /// <remarks>An entry remembers no build progress, so a site seen once draws as a finished building (BUG-0275 item 1;
    /// the sim's <see cref="BuildingGhost"/> would need the progress).</remarks>
    public int CollectGhosts(FogStore fog, ReadOnlySpan<int> buildingGeneration)
    {
        int count = 0;
        if (Enabled)
        {
            ReadOnlySpan<BuildingGhost> list = fog.Ghosts(Player);
            for (int i = 0; i < _ghostShown.Length; i++)
            {
                BuildingGhost g = i < list.Length ? list[i] : default;
                bool drawn = _buildingShown[i] && (buildingGeneration.IsEmpty || (i < buildingGeneration.Length && buildingGeneration[i] == g.Generation));
                bool ghost = g.Known && !drawn;
                _ghostShown[i] = ghost;
                _ghosts[i] = ghost ? g : default;
                if (ghost) count++;
            }
        }
        GhostCount = count;
        return count;
    }
}
