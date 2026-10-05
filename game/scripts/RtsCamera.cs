using Godot;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Fixed-pitch RTS camera: pans over the map, zooms with the wheel, never rotates (docs/02 "Controls and camera").</summary>
/// <remarks>
/// State is a ground focus point (sim x, y = Godot x, z) and a zoom (height above it in meters);
/// limits come from <see cref="CameraLimits"/>. The camera sits behind the focus on +Z, looking
/// toward -Z, so screen-up is map -Z.
/// </remarks>
public partial class RtsCamera : Camera3D
{
    /// <summary>Screen-edge panning; turned off for scripted screenshots so the mouse can't move the shot.</summary>
    [Export] public bool EdgePanEnabled { get; set; } = true;

    private System.Numerics.Vector2 _focus;
    private float _zoom = CameraLimits.DefaultZoom;
    private int _mapWidthCells = 1, _mapHeightCells = 1;
    private bool _dragging;

    /// <summary>Sets the map size the focus is clamped to and centers the camera on it.</summary>
    public void SetMap(int widthCells, int heightCells)
    {
        _mapWidthCells = widthCells;
        _mapHeightCells = heightCells;
        _focus = CameraLimits.ClampFocus(new System.Numerics.Vector2(float.NaN, float.NaN), widthCells, heightCells);
        ApplyTransform();
    }

    /// <summary>Moves the ground focus to sim point (x, y) in meters, clamped to the map.</summary>
    public void SetFocus(float x, float y)
    {
        _focus = CameraLimits.ClampFocus(new System.Numerics.Vector2(x, y), _mapWidthCells, _mapHeightCells);
        ApplyTransform();
    }

    public override void _Ready()
    {
        RotationDegrees = new Vector3(-CameraLimits.PitchDegrees, 0f, 0f);
        ApplyTransform();
    }

    public override void _Process(double delta)
    {
        Vector2 dir = Input.GetVector("camera_pan_left", "camera_pan_right", "camera_pan_up", "camera_pan_down");
        if (EdgePanEnabled && !_dragging) dir += EdgeDirection();
        if (dir != Vector2.Zero)
        {
            dir = dir.LimitLength(1f);
            float step = CameraLimits.PanSpeed(_zoom) * (float)delta;
            MoveFocus(dir.X * step, dir.Y * step);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("camera_zoom_in")) SetZoom(_zoom - CameraLimits.ZoomStep);
        else if (e.IsActionPressed("camera_zoom_out")) SetZoom(_zoom + CameraLimits.ZoomStep);
        else if (e.IsActionPressed("camera_drag")) _dragging = true;
        else if (e.IsActionReleased("camera_drag")) _dragging = false;
        else if (_dragging && e is InputEventMouseMotion motion)
        {
            // Grab-the-ground drag: the map follows the cursor.
            float mpp = MetersPerPixel();
            MoveFocus(-motion.Relative.X * mpp, -motion.Relative.Y * mpp);
        }
    }

    /// <summary>Sets the zoom (meters above the focus), clamped to the camera limits.</summary>
    public void SetZoom(float zoom)
    {
        _zoom = CameraLimits.ClampZoom(zoom);
        ApplyTransform();
    }

    private void MoveFocus(float dx, float dz)
    {
        _focus = CameraLimits.ClampFocus(_focus + new System.Numerics.Vector2(dx, dz), _mapWidthCells, _mapHeightCells);
        ApplyTransform();
    }

    private Vector2 EdgeDirection()
    {
        if (!DisplayServer.WindowIsFocused()) return Vector2.Zero;
        Viewport vp = GetViewport();
        Vector2 size = vp.GetVisibleRect().Size;
        Vector2 m = vp.GetMousePosition();
        if (m.X < 0 || m.Y < 0 || m.X > size.X || m.Y > size.Y) return Vector2.Zero; // cursor outside the window
        float edge = CameraLimits.EdgePanPixels;
        var d = Vector2.Zero;
        if (m.X < edge) d.X -= 1;
        else if (m.X > size.X - edge) d.X += 1;
        if (m.Y < edge) d.Y -= 1;
        else if (m.Y > size.Y - edge) d.Y += 1;
        return d;
    }

    // Approximate ground meters under one screen pixel at the focus distance.
    private float MetersPerPixel()
    {
        float distance = _zoom / Mathf.Sin(Mathf.DegToRad(CameraLimits.PitchDegrees));
        float viewHeight = 2f * distance * Mathf.Tan(Mathf.DegToRad(Fov) / 2f);
        return viewHeight / Mathf.Max(1f, GetViewport().GetVisibleRect().Size.Y);
    }

    private void ApplyTransform()
    {
        float back = _zoom / Mathf.Tan(Mathf.DegToRad(CameraLimits.PitchDegrees));
        Position = new Vector3(_focus.X, _zoom, _focus.Y + back);
    }
}
