using System.Collections.Generic;
using Godot;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>QA (M2-1): drives the real <see cref="RtsCamera"/> with input actions and wheel/drag events and checks it clamps.</summary>
/// <remarks>
/// Headless-safe (no edge pan, which needs a focused window). Run:
/// <c>&amp; $env:GODOT --headless --path game res://tests/CameraClampTest.tscn</c>; prints
/// "CAMERA TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class CameraClampTest : Node
{
    private const int MapCells = 128;
    private const float MapMeters = MapCells * 2f;

    private RtsCamera _cam = null!;
    private readonly List<string> _failures = new();
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        _cam = new RtsCamera { Fov = 50f };
        AddChild(_cam);
        _cam.EdgePanEnabled = false;
        _cam.SetMap(MapCells, MapCells);
        Engine.TimeScale = 20.0; // pan across the map in a few hundred frames
        Expect("start", MapMeters / 2, CameraLimits.DefaultZoom, MapMeters / 2);
    }

    // Each pan leg holds one action for 400 frames (x20 time scale: far more than a map width), then checks.
    public override void _Process(double delta)
    {
        if (_frame++ == 0)
        {
            if (_step == 0)
            {
                for (int i = 0; i < 30; i++) Wheel(MouseButton.WheelUp);
                Expect("zoom in to min", MapMeters / 2, CameraLimits.MinZoom, MapMeters / 2);
                for (int i = 0; i < 30; i++) Wheel(MouseButton.WheelDown);
                Expect("zoom out to max", MapMeters / 2, CameraLimits.MaxZoom, MapMeters / 2);
            }
            Hold(s_legs[_step].Action);
            return;
        }
        if (_frame < 400) return;

        (string _, string name, float x, float z) = s_legs[_step];
        Expect(name, x, CameraLimits.MaxZoom, z);
        _frame = 0;
        if (++_step < s_legs.Length) return;

        ReleaseAll();
        // Middle-drag far right and down: the map follows the cursor, so the focus moves left/up.
        _cam._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
        for (int i = 0; i < 50; i++)
            _cam._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(500, 500) });
        _cam._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
        Expect("drag to north-west", 0f, CameraLimits.MaxZoom, 0f);
        SetProcess(false);
        Finish();
    }

    private static readonly (string Action, string Name, float X, float Z)[] s_legs =
    {
        ("camera_pan_left", "west edge", 0f, MapMeters / 2),
        ("camera_pan_up", "north-west corner", 0f, 0f),
        ("camera_pan_right", "north-east corner", MapMeters, 0f),
        ("camera_pan_down", "south-east corner", MapMeters, MapMeters),
    };

    private void Wheel(MouseButton button)
    {
        _cam._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true });
        _cam._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false });
    }

    private static void Hold(string action)
    {
        ReleaseAll();
        Input.ActionPress(action);
    }

    private static void ReleaseAll()
    {
        foreach (string a in new[] { "camera_pan_left", "camera_pan_right", "camera_pan_up", "camera_pan_down" })
            Input.ActionRelease(a);
    }

    private void Expect(string what, float focusX, float zoom, float focusZ)
    {
        float back = zoom / Mathf.Tan(Mathf.DegToRad(CameraLimits.PitchDegrees));
        var want = new Vector3(focusX, zoom, focusZ + back);
        Vector3 got = _cam.Position;
        GD.Print($"camera check {what}: {got}");
        if (!got.IsEqualApprox(want) && got.DistanceTo(want) > 0.01f)
            _failures.Add($"{what}: camera at {got}, expected {want}");
        float pitch = _cam.RotationDegrees.X, yaw = _cam.RotationDegrees.Y;
        if (Mathf.Abs(pitch + CameraLimits.PitchDegrees) > 0.01f || Mathf.Abs(yaw) > 0.01f)
            _failures.Add($"{what}: rotation {_cam.RotationDegrees}, expected pitch -55, yaw 0");
    }

    private void Finish()
    {
        ReleaseAll();
        Engine.TimeScale = 1.0;
        foreach (string f in _failures) GD.Print($"CAMERA TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("CAMERA TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
