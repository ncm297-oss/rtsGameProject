using System;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The bottom-left minimap (docs/02 "Minimap"): baked terrain, unit dots at 5 Hz and the camera's view outline; left-click or drag moves the camera, right-click orders the selection there.</summary>
/// <remarks>
/// Pixels come from the pure <see cref="MinimapRaster"/> (one texel per cell, drawn with nearest
/// filtering) and the pixel/meter mapping from <see cref="MinimapTransform"/>. Mouse filter is
/// Stop, so clicks inside the control never reach <see cref="SelectionController"/>; events outside
/// its rect are never seen here. Holds no gameplay state; orders go through
/// <see cref="SelectionController.Order"/> (a Move, queued while <c>order_queue</c> is held).
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
    private ImageTexture _terrainTexture = null!, _dotsTexture = null!;
    private readonly Vector2[] _outline = new Vector2[5];
    private readonly Stopwatch _watch = new();
    private int _lastRefreshTick = -RefreshTicks;
    private bool _jumping;

    /// <summary>The minimap's pixels; null until <see cref="Init"/>.</summary>
    public MinimapRaster? Raster { get; private set; }

    /// <summary>CPU copy of the dot layer last uploaded to the texture.</summary>
    public Image DotsImage { get; private set; } = null!;

    /// <summary>Wall-clock cost of the last dot refresh (raster + upload) in milliseconds.</summary>
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
        camera.EdgePanBlocker = this;
    }

    public override void _Process(double delta)
    {
        if (Raster == null || _runner.Simulation is not Simulation sim) return;
        if (sim.TickNumber - _lastRefreshTick >= RefreshTicks) Refresh(sim);
        QueueRedraw();
    }

    /// <summary>Redraws the unit dots from the sim's current positions and uploads them.</summary>
    public void Refresh(Simulation sim)
    {
        _watch.Restart();
        UnitStore u = sim.World.Units;
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
            else if (mb.IsActionPressed("command") && Fit.TryToMap(new(mb.Position.X, mb.Position.Y), out System.Numerics.Vector2 p))
                _selection.Order(CommandKind.Move, new Vector2(p.X, p.Y), Input.IsActionPressed("order_queue"));
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion motion && _jumping)
        {
            JumpTo(motion.Position);
            AcceptEvent();
        }
    }

    private void JumpTo(Vector2 pixel)
    {
        if (Fit.TryToMap(new(pixel.X, pixel.Y), out System.Numerics.Vector2 p)) _camera.SetFocus(p.X, p.Y);
    }
}
