using Godot;
using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The fog of war on screen (M4-V4): the local player's fog texture, the fog materials, and the hide rule the views read.</summary>
/// <remarks>
/// <para><b>Texture.</b> An R8 <see cref="ImageTexture"/>, one texel per map cell, holding <c>World.Fog.Visibility(local)</c>'s
/// own bytes (0 unexplored, 1 explored, 2 visible), packed by the pure <see cref="FogView"/> and uploaded only when
/// <c>Fog.Version(local)</c> moved (once per fog update, every 4 ticks at most). The shaders in <c>res://shaders/</c> sample
/// it: the terrain per fragment with smooth filtering (<c>terrain_fog.gdshader</c>), trees, mines and rubble
/// (<c>prop_fog.gdshader</c>) and corpses (<c>marker_fog.gdshader</c>) by the cell under each instance: not drawn on
/// unexplored ground, darkened to <see cref="FogView.ExploredBrightness"/> and half desaturated on explored ground.</para>
/// <para><b>Hide rule.</b> <see cref="View"/>'s <c>UnitShown</c> / <c>BuildingShown</c> (<c>CanSeeUnit</c> /
/// <c>CanSeeBuilding</c> of the local player); each view refreshes it itself (once per tick, whichever asks first), so the
/// node order in the scene doesn't matter.</para>
/// <para><b>Last-seen resources</b> (M4-VH2, BUG-0281 item 1): <see cref="Resources"/> keeps <see cref="Rts.Sim.ViewApi.SeenResources"/>,
/// the nodes as the player last saw them, so a tree felled in explored fog stays drawn (darkened) on the map and the
/// minimap until its footprint is in sight again.</para>
/// <para><b>Disabled</b> (<c>--no-fog</c>, a dev and test flag): the texture is all visible and every live slot is shown.</para>
/// Holds no gameplay state; reads the sim only.
/// </remarks>
public partial class FogOfWar : Node
{
    /// <summary>The fog shaders (text resources; no import step).</summary>
    public const string TerrainShaderPath = "res://shaders/terrain_fog.gdshader",
        PropShaderPath = "res://shaders/prop_fog.gdshader",
        MarkerShaderPath = "res://shaders/marker_fog.gdshader";

    private Shader? _terrainShader, _propShader, _markerShader;
    private Vector2 _worldSize;

    /// <summary>The runner whose sim's fog is drawn; null draws nothing new (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The pure fog state the views read (texture bytes, shown slots); null before <see cref="Bind"/>.</summary>
    public FogView? View { get; private set; }

    /// <summary>The CPU copy of the texture last uploaded (R8, map width x height).</summary>
    public Image Image { get; private set; } = null!;

    /// <summary>The fog texture every fog material samples.</summary>
    public ImageTexture Texture { get; private set; } = null!;

    /// <summary>Texture uploads so far (one per fog version seen, plus the first).</summary>
    public int Uploads { get; private set; }

    /// <summary>The resource nodes as the local player last saw them (BUG-0281 item 1), kept by <see cref="Resources"/>; null before <see cref="Bind"/>.</summary>
    public SeenResources? SeenResources { get; private set; }

    /// <summary>Sizes the texture for the match's map and makes the hide rule for <paramref name="player"/>; call once after the sim exists, before the views bind.</summary>
    /// <param name="enabled">False for <c>--no-fog</c>.</param>
    public void Bind(World world, int player, bool enabled)
    {
        View = new FogView(world.Fog.Width, world.Fog.Height, world.Units.Capacity, world.Buildings.Capacity, player, enabled);
        SeenResources = new SeenResources(world.Data.Resources, world.Resources.Capacity);
        Image = Image.CreateFromData(View.Width, View.Height, false, Image.Format.R8, View.Texture);
        Texture = ImageTexture.CreateFromImage(Image);
        _worldSize = new Vector2(View.Width * MapConstants.CellSize, View.Height * MapConstants.CellSize);
        Sync(world);
    }

    public override void _Process(double delta)
    {
        if (Runner?.Simulation is Simulation sim) Sync(sim.World);
    }

    /// <summary>Refreshes the hide rule for this tick and uploads the texture if the fog's version moved; true when it uploaded. Allocation-free.</summary>
    public bool Sync(World world)
    {
        if (View == null) return false;
        View.Refresh(world.Fog, world.TickNumber, world.Units.Alive, world.Buildings.Alive, world.Buildings.Generation);
        if (!View.PackTexture(world.Fog)) return false;
        Image.SetData(View.Width, View.Height, false, Image.Format.R8, View.Texture);
        Texture.Update(Image);
        Uploads++;
        return true;
    }

    /// <summary>The hide rule refreshed for <paramref name="world"/>'s tick (a no-op after the first call of a tick); null before <see cref="Bind"/>.</summary>
    public FogView? Refreshed(World world)
    {
        View?.Refresh(world.Fog, world.TickNumber, world.Units.Alive, world.Buildings.Alive, world.Buildings.Generation);
        return View;
    }

    /// <summary>
    /// The last-seen resource nodes brought up to this frame (<see cref="SeenResources.Update"/>: only slots that changed in
    /// the store are checked against the fog), for the props and the minimap's resource layer; null before <see cref="Bind"/>.
    /// Allocation-free.
    /// </summary>
    public SeenResources? Resources(World world)
    {
        if (View == null || SeenResources == null) return null;
        Rts.Sim.Entities.ResourceStore r = world.Resources;
        SeenResources.Update(world.NavGrid.Version, r.Alive, r.TypeId, r.Cell, world.Fog, View.Player, View.Enabled, world.NavGrid.Width);
        return SeenResources;
    }

    /// <summary>The terrain's material: vertex colours under the fog, per fragment.</summary>
    public ShaderMaterial TerrainMaterial() => Material(_terrainShader ??= GD.Load<Shader>(TerrainShaderPath));

    /// <summary>A lit prop's material (a tree's trunk or canopy, a mine, rubble) in <paramref name="albedo"/>, under the fog by the cell under each instance.</summary>
    public ShaderMaterial PropMaterial(Color albedo, float roughness)
    {
        ShaderMaterial m = Material(_propShader ??= GD.Load<Shader>(PropShaderPath));
        m.SetShaderParameter("albedo", albedo);
        m.SetShaderParameter("roughness", roughness);
        return m;
    }

    /// <summary>An unshaded marker's material coloured by its instance colour (corpses), under the fog by the cell under each instance.</summary>
    public ShaderMaterial MarkerMaterial() => Material(_markerShader ??= GD.Load<Shader>(MarkerShaderPath));

    private ShaderMaterial Material(Shader shader)
    {
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("fog_tex", Texture);
        m.SetShaderParameter("fog_world_size", _worldSize);
        m.SetShaderParameter("fog_explored", FogView.ExploredBrightness);
        return m;
    }
}
