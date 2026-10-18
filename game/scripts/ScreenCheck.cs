using Godot;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Whether a ground point is on screen (M6-V12): the camera's view decides which world sounds play and whether an alert shows its text.</summary>
public static class ScreenCheck
{
    /// <summary>
    /// True when sim point <paramref name="point"/> (m), at the terrain's height there, is in front of <paramref name="camera"/>
    /// and projects inside its viewport's visible rect. Allocation-free.
    /// </summary>
    public static bool OnScreen(Camera3D camera, Heightmap map, System.Numerics.Vector2 point)
    {
        var at = new Vector3(point.X, TerrainHeight.At(map, point.X, point.Y), point.Y);
        if (camera.IsPositionBehind(at)) return false;
        Vector2 px = camera.UnprojectPosition(at);
        return camera.GetViewport().GetVisibleRect().HasPoint(px);
    }
}
