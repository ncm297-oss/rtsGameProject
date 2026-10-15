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

    /// <summary>The edge pan's speed as a multiple of <see cref="CameraLimits.PanSpeed"/> (the settings' edge pan speed, 0.5-2; M6-V6). Keyboard panning keeps 1.</summary>
    public float EdgePanSpeed { get; set; } = 1f;

    /// <summary>A control (the minimap) that sits on the screen edge; edge panning pauses while the mouse is over it.</summary>
    public Control? EdgePanBlocker { get; set; }

    /// <summary>Ground focus point (sim x, y) in meters.</summary>
    public System.Numerics.Vector2 Focus => _focus;

    /// <summary>Height above the focus in meters, within the camera limits.</summary>
    public float Zoom => _zoom;

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
        Vector2 keys = Input.GetVector("camera_pan_left", "camera_pan_right", "camera_pan_up", "camera_pan_down");
        Vector2 edge = EdgePanEnabled && !_dragging ? EdgeDirection() : Vector2.Zero;
        Vector2 dir = PanVector(keys, edge, EdgePanSpeed);
        if (dir != Vector2.Zero)
        {
            float step = CameraLimits.PanSpeed(_zoom) * (float)delta;
            MoveFocus(dir.X * step, dir.Y * step);
        }
    }

    /// <summary>
    /// The pan this frame as a multiple of the pan speed: the keys' direction (length at most 1) plus the edge direction
    /// (length at most 1) times <paramref name="edgeSpeed"/>, together at most the larger of 1 and the edge speed, so the
    /// keys and the edge pushing the same way are not faster than the faster alone.
    /// </summary>
    public static Vector2 PanVector(Vector2 keys, Vector2 edge, float edgeSpeed)
    {
        if (!(edgeSpeed > 0f)) edgeSpeed = 0f; // NaN or negative: no edge pan
        Vector2 sum = keys.LimitLength(1f) + edge.LimitLength(1f) * edgeSpeed;
        return sum.LimitLength(edge == Vector2.Zero ? 1f : Mathf.Max(1f, edgeSpeed));
    }

    public override void _Notification(int what)
    {
        // BUG-0580: a paused node gets no input, so a camera_drag release while the match is paused never arrives; a drag
        // must not outlive the pause (Resume starts with no button held as far as the camera knows).
        if (what == NotificationPaused || what == NotificationUnpaused) _dragging = false;
    }

    /// <summary>True while a middle-button (<c>camera_drag</c>) drag is live.</summary>
    public bool Dragging => _dragging;

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
        if (EdgePanBlocker != null && EdgePanBlocker.IsVisibleInTree() && EdgePanBlocker.GetGlobalRect().HasPoint(m)) return Vector2.Zero;
        // The band and the outside-the-window rule (M6-V7: the last pixel pans, so a cursor confined to the window does).
        System.Numerics.Vector2 d = CameraLimits.EdgeDirection(new(m.X, m.Y), new(size.X, size.Y), CameraLimits.EdgePanPixels);
        return new Vector2(d.X, d.Y);
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
