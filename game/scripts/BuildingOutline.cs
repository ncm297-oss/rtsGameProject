using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The selected building's highlight (M3-V2): a flat outline round its footprint, four thin bars just above the ground.</summary>
/// <remarks>
/// One node set, made in <see cref="_Ready"/> and moved by <see cref="SelectionController.SyncOutline"/> only when the
/// selected building changes, so a steady frame touches nothing. Placeholder colour until the art pass (the M2-1 rule).
/// </remarks>
public partial class BuildingOutline : Node3D
{
    /// <summary>Bar width in meters and its height above the terrain at the footprint centre.</summary>
    public const float Thickness = 0.25f, Lift = 0.08f;

    private static readonly Color OutlineColor = new(0.35f, 1f, 0.45f);

    private readonly MeshInstance3D[] _bars = new MeshInstance3D[4];

    /// <summary>The building slot the outline is on, or -1 while hidden.</summary>
    public int ShownSlot { get; private set; } = -1;

    /// <summary>Times the outline was moved or hidden (only on a selection change).</summary>
    public int Updates { get; private set; }

    public override void _Ready()
    {
        var mesh = new BoxMesh { Size = Vector3.One };
        var mat = new StandardMaterial3D { AlbedoColor = OutlineColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        mesh.Material = mat;
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new MeshInstance3D { Name = $"Bar{i}", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_bars[i]);
        }
        Visible = false;
    }

    /// <summary>Puts the outline round building slot <paramref name="slot"/>'s footprint and shows it.</summary>
    public void ShowFor(World world, int slot)
    {
        BuildingDef def = world.Data.Buildings[world.Buildings.TypeId[slot]];
        System.Numerics.Vector2 c = StartBase.FootprintCenter(world.NavGrid, def, world.Buildings.Cell[slot]);
        // The outline sits just outside the box so the box doesn't hide it.
        float w = def.FootprintWidth * MapConstants.CellSize + Thickness, d = def.FootprintHeight * MapConstants.CellSize + Thickness;
        Position = new Vector3(c.X, TerrainHeight.At(world.Heightmap, c.X, c.Y) + Lift, c.Y);
        _bars[0].Transform = new Transform3D(Basis.FromScale(new Vector3(w + Thickness, Thickness * 0.4f, Thickness)), new Vector3(0f, 0f, -d / 2f));
        _bars[1].Transform = new Transform3D(Basis.FromScale(new Vector3(w + Thickness, Thickness * 0.4f, Thickness)), new Vector3(0f, 0f, d / 2f));
        _bars[2].Transform = new Transform3D(Basis.FromScale(new Vector3(Thickness, Thickness * 0.4f, d + Thickness)), new Vector3(-w / 2f, 0f, 0f));
        _bars[3].Transform = new Transform3D(Basis.FromScale(new Vector3(Thickness, Thickness * 0.4f, d + Thickness)), new Vector3(w / 2f, 0f, 0f));
        Visible = true;
        ShownSlot = slot;
        Updates++;
    }

    /// <summary>Hides the outline.</summary>
    public void HideOutline()
    {
        Visible = false;
        ShownSlot = -1;
        Updates++;
    }
}
