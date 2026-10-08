using System;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The bottom-left minimap (docs/02 "Minimap"): baked terrain, resource nodes and unit dots at 5 Hz and the camera's view outline; left-click or drag moves the camera, right-click orders the selection there.</summary>
/// <remarks>
/// Pixels come from the pure <see cref="MinimapRaster"/> (one texel per cell, drawn with nearest
/// filtering) and the pixel/meter mapping from <see cref="MinimapTransform"/>. Mouse filter is
/// Stop, so clicks inside the control never reach <see cref="SelectionController"/>; events outside
/// its rect are never seen here. Holds no gameplay state; orders go through
/// <see cref="SelectionController.Order"/> (a Move, queued while <c>order_queue</c> is held); a
/// right-click while A-targeting only cancels it; with a building selected a right click sets its rally point
/// (<see cref="SelectionController.RallyOrder"/>, M3-V3). A left click here never ends targeting.
/// </remarks>
public partial class Minimap : Control
{
    /// <summary>Sim ticks between dot refreshes: 20 Hz / 4 = 5 Hz (docs/03 "Rendering and presentation").</summary>
    public const int RefreshTicks = 4;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.6f);
    private static readonly Color OutlineColor = new(1f, 1f, 1f);

    private SimRunner _runner = null!;
    private RtsCamera _camera = null!;
    private SelectionController _selection = null!;
    private ImageTexture _terrainTexture = null!, _dotsTexture = null!, _resourcesTexture = null!;
    private readonly Vector2[] _outline = new Vector2[5];
    private readonly Stopwatch _watch = new();
    private int _lastRefreshTick = -RefreshTicks;
    private bool _jumping;

    /// <summary>The minimap's pixels; null until <see cref="Init"/>.</summary>
    public MinimapRaster? Raster { get; private set; }

    /// <summary>CPU copy of the resource layer last uploaded to its texture (trees, mines; under the dots).</summary>
    public Image ResourcesImage { get; private set; } = null!;

    /// <summary>CPU copy of the dot layer last uploaded to the texture.</summary>
    public Image DotsImage { get; private set; } = null!;

    /// <summary>Wall-clock cost of the last refresh (resource check or redraw, dots, uploads) in milliseconds.</summary>
    public double LastRefreshMs { get; private set; }

    /// <summary>Dot refreshes so far.</summary>
    public int Refreshes { get; private set; }

    /// <summary>The camera outline's four corners in control pixels (top-left, top-right, bottom-right, bottom-left of the screen), as last drawn.</summary>
    public ReadOnlySpan<Vector2> Outline => _outline.AsSpan(0, 4);

    /// <summary>Current pixel/meter mapping for the control's size.</summary>
    public MinimapTransform Fit => new(new(Size.X, Size.Y),
        new System.Numerics.Vector2(Raster!.Width, Raster.Height) * MapConstants.CellSize);

    /// <summary>Bakes the terrain texture and connects the minimap to the match; call once after the sim exists.</summary>
    /// <param name="playerRgb">Dot colour 0xRRGGBB per player index.</param>
    public void Init(SimRunner runner, RtsCamera camera, SelectionController selection, uint[] playerRgb)
    {
        _runner = runner;
        _camera = camera;
        _selection = selection;
        World world = runner.Simulation!.World;
        Raster = new MinimapRaster(world.Heightmap, world.NavGrid, playerRgb, world.Units.Capacity);
        _terrainTexture = ImageTexture.CreateFromImage(Image.CreateFromData(Raster.Width, Raster.Height, false, Image.Format.Rgba8, Raster.Terrain));
        DotsImage = Image.CreateFromData(Raster.Width, Raster.Height, false, Image.Format.Rgba8, Raster.Dots);
        _dotsTexture = ImageTexture.CreateFromImage(DotsImage);
        ResourcesImage = Image.CreateFromData(Raster.Width, Raster.Height, false, Image.Format.Rgba8, Raster.Resources);
        _resourcesTexture = ImageTexture.CreateFromImage(ResourcesImage);
        camera.EdgePanBlocker = this;
    }

    public override void _Process(double delta)
    {
        if (Raster == null || _runner.Simulation is not Simulation sim) return;
        if (sim.TickNumber - _lastRefreshTick >= RefreshTicks) Refresh(sim);
        QueueRedraw();
    }

    /// <summary>Redraws the resource layer if a node was felled or mined out, then the unit dots from the sim's current positions, and uploads what changed.</summary>
    public void Refresh(Simulation sim)
    {
        _watch.Restart();
        World world = sim.World;
        ResourceStore r = world.Resources;
        // Keyed on the resource set (FreeCount rises with each fell), not NavGrid.Version, which every building change bumps (BUG-0107).
        if (Raster!.DrawResources(world.Data.Resources, r.FreeCount, r.Alive, r.TypeId, r.Cell))
        {
            ResourcesImage.SetData(Raster.Width, Raster.Height, false, Image.Format.Rgba8, Raster.Resources);
            _resourcesTexture.Update(ResourcesImage);
        }
        UnitStore u = world.Units;
        Raster!.DrawDots(u.Alive, u.Position, u.Owner);
        DotsImage.SetData(Raster.Width, Raster.Height, false, Image.Format.Rgba8, Raster.Dots);
        _dotsTexture.Update(DotsImage);
        _watch.Stop();
        LastRefreshMs = _watch.Elapsed.TotalMilliseconds;
        _lastRefreshTick = sim.TickNumber;
        Refreshes++;
    }

    public override void _Draw()
    {
        if (Raster == null) return;
        (System.Numerics.Vector2 pos, System.Numerics.Vector2 size) = Fit.MapRect;
        var rect = new Rect2(pos.X, pos.Y, size.X, size.Y);
        DrawRect(new Rect2(Vector2.Zero, Size), Backdrop);
        DrawTextureRect(_terrainTexture, rect, false);
        DrawTextureRect(_resourcesTexture, rect, false);
        DrawTextureRect(_dotsTexture, rect, false);
        UpdateOutline();
        DrawPolyline(_outline, OutlineColor, 1.5f);
    }

    /// <summary>Projects the four viewport corners onto the ground plane at the focus height and maps them to control pixels, clamped to the map.</summary>
    public void UpdateOutline()
    {
        MinimapTransform fit = Fit;
        Vector2 view = _camera.GetViewport().GetVisibleRect().Size;
        System.Numerics.Vector2 focus = _camera.Focus;
        float planeY = TerrainHeight.At(_runner.Simulation!.World.Heightmap, focus.X, focus.Y);
        for (int i = 0; i < 4; i++)
        {
            var corner = new Vector2(i == 1 || i == 2 ? view.X : 0f, i >= 2 ? view.Y : 0f);
            Vector3 o = _camera.ProjectRayOrigin(corner), d = _camera.ProjectRayNormal(corner);
            System.Numerics.Vector2 ground = MinimapTransform.RayToGround(new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), planeY);
            System.Numerics.Vector2 px = fit.ToPixel(fit.ClampToMap(ground));
            _outline[i] = new Vector2(px.X, px.Y);
        }
        _outline[4] = _outline[0];
    }

    public override void _GuiInput(InputEvent e)
    {
        if (Raster == null) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.IsActionPressed("select"))
            {
                _jumping = true;
                JumpTo(mb.Position);
            }
            else if (mb.IsActionReleased("select")) _jumping = false;
            else if (mb.IsActionPressed("command"))
            {
                // A right-click while A is armed cancels it and orders nothing, as on the 3D view (BUG-0068).
                if (_selection.Targeting) _selection.CancelTargeting();
                else if (Fit.TryToMap(new(mb.Position.X, mb.Position.Y), out System.Numerics.Vector2 p))
                {
                    // With a building selected the right click sets its rally point there (M3-V3), as on the 3D view.
                    if (_selection.SelectedBuilding >= 0) _selection.RallyOrder(p);
                    else _selection.Order(CommandKind.Move, new Vector2(p.X, p.Y), Input.IsActionPressed("order_queue"));
                }
            }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion motion && _jumping)
        {
            JumpTo(motion.Position);
            AcceptEvent();
        }
    }

    /// <summary>What a left click at control pixel <paramref name="pixel"/> does: centres the camera on that map point; false (nothing moves) off the map.</summary>
    public bool JumpTo(Vector2 pixel)
    {
        if (!Fit.TryToMap(new(pixel.X, pixel.Y), out System.Numerics.Vector2 p)) return false;
        _camera.SetFocus(p.X, p.Y);
        return true;
    }
}
