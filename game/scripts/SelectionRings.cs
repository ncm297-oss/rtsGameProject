using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Entities;

namespace Rts.Game;

/// <summary>Flat rings under the selected units: one <see cref="MultiMesh"/> instance per selected unit, one draw call.</summary>
public partial class SelectionRings : MultiMeshInstance3D
{
    /// <summary>Ring radius as a multiple of the unit's radius.</summary>
    public const float RadiusScale = 1.35f;

    /// <summary>Height above the ground so the ring doesn't z-fight the terrain, in meters.</summary>
    public const float Lift = 0.06f;

    // UI tint, not team colour (team colour comes from faction data).
    private static readonly Color RingColor = new(0.35f, 1f, 0.45f);

    /// <summary>Instances currently drawn (one per live selected unit).</summary>
    public int ShownCount => _mm?.VisibleInstanceCount ?? 0;

    // Kept so the per-frame update doesn't go through the engine property each time.
    private MultiMesh? _mm;

    /// <summary>Creates the ring mesh and sizes the instance buffer for every unit slot.</summary>
    public void Init(int unitCapacity)
    {
        var mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 24, RingSegments = 4 };
        mesh.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = RingColor,
        };
        Multimesh = _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = unitCapacity,
            VisibleInstanceCount = 0,
        };
        CastShadow = ShadowCastingSetting.Off;
    }

    /// <summary>Puts one ring under each live selected unit at its interpolated position; no allocation.</summary>
    public void Sync(World world, float alpha, ReadOnlySpan<EntityHandle> selected)
    {
        MultiMesh? mm = _mm;
        if (mm == null) return;
        int n = 0, cap = mm.InstanceCount;
        for (int i = 0; i < selected.Length && n < cap; i++)
        {
            EntityHandle h = selected[i];
            if (!world.Units.IsAlive(h)) continue;
            float s = world.Units.Radius[h.Index] * RadiusScale;
            Vector3 p = UnitViews.GroundPoint(world, h.Index, alpha) + new Vector3(0f, Lift, 0f);
            // Flattened in Y so the torus reads as a ring painted on the ground.
            mm.SetInstanceTransform(n++, new Transform3D(Basis.FromScale(new Vector3(s, 0.15f, s)), p));
        }
        mm.VisibleInstanceCount = n;
    }
}
