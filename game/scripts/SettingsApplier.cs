using Godot;

namespace Rts.Game;

/// <summary>
/// Puts <see cref="GameSettings"/> into effect (M6-V6): the window (mode, size, VSync), the quality preset and the bus
/// volumes. The camera's edge pan is the shell's to pass on (<see cref="RtsCamera.EdgePanSpeed"/>).
/// </summary>
/// <remarks>
/// Headless there is no window: the window and VSync calls are skipped (<see cref="HasWindow"/>), everything else applies.
/// The quality presets (docs/03 "Implementation (M6-V6)"): Low = sun shadow atlas 2048 with hard edges, no MSAA, 3D at 75 %;
/// Medium = Godot's defaults (atlas 4096, soft shadow filter Low, no MSAA, 100 %), the game's look before settings; High =
/// atlas 4096, soft shadow filter Medium, 4x MSAA, 100 %.
/// </remarks>
public static class SettingsApplier
{
    /// <summary>One quality preset's values.</summary>
    public readonly record struct Preset(int ShadowAtlas, RenderingServer.ShadowQuality ShadowFilter, Viewport.Msaa Msaa, float RenderScale);

    /// <summary>The preset of each <see cref="SettingsQuality"/>, in enum order.</summary>
    public static readonly Preset[] Presets =
    {
        new(2048, RenderingServer.ShadowQuality.Hard, Viewport.Msaa.Disabled, 0.75f),
        new(4096, RenderingServer.ShadowQuality.SoftLow, Viewport.Msaa.Disabled, 1f),
        new(4096, RenderingServer.ShadowQuality.SoftMedium, Viewport.Msaa.Msaa4X, 1f),
    };

    /// <summary>False under <c>--headless</c> (no window to size or sync).</summary>
    public static bool HasWindow => DisplayServer.GetName() != "headless";

    /// <summary>Applies everything; <paramref name="vsyncOverride"/> (<c>--vsync on|off</c>) wins over the file for the run.</summary>
    public static void Apply(GameSettings s, Viewport root, bool? vsyncOverride)
    {
        ApplyWindow(s, vsyncOverride);
        ApplyQuality(s.Quality, root);
        ApplyVolumes(s);
    }

    /// <summary>Window mode, size (windowed only, centred on its screen) and VSync; nothing headless.</summary>
    public static void ApplyWindow(GameSettings s, bool? vsyncOverride)
    {
        if (!HasWindow) return;
        DisplayServer.WindowMode mode = s.WindowMode switch
        {
            SettingsWindowMode.Borderless => DisplayServer.WindowMode.Fullscreen,
            SettingsWindowMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed)
        {
            var size = new Vector2I(s.Width, s.Height);
            if (DisplayServer.WindowGetSize() != size)
            {
                DisplayServer.WindowSetSize(size);
                int screen = DisplayServer.WindowGetCurrentScreen();
                Vector2I origin = DisplayServer.ScreenGetPosition(screen), area = DisplayServer.ScreenGetSize(screen);
                DisplayServer.WindowSetPosition(origin + (area - size) / 2);
            }
        }
        bool vsync = vsyncOverride ?? s.Vsync;
        DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    /// <summary>The preset's shadows (global), MSAA and 3D scale (on <paramref name="root"/>, the window's viewport).</summary>
    public static void ApplyQuality(SettingsQuality quality, Viewport root)
    {
        Preset p = Presets[(int)quality];
        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowAtlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(p.ShadowFilter);
        root.Msaa3D = p.Msaa;
        root.Scaling3DScale = p.RenderScale;
    }

    /// <summary>Master, Music and Sfx bus volumes (<see cref="GameSettings.VolumeDb"/>); a missing bus is skipped with a warning.</summary>
    public static void ApplyVolumes(GameSettings s)
    {
        SetBus(GameSettings.MasterBus, s.MasterVolume);
        SetBus(GameSettings.MusicBus, s.MusicVolume);
        SetBus(GameSettings.SfxBus, s.SfxVolume);
    }

    private static void SetBus(string name, int volume)
    {
        int bus = AudioServer.GetBusIndex(name);
        if (bus < 0)
        {
            GD.PushWarning($"Settings: no '{name}' audio bus (game/default_bus_layout.tres); its volume is not applied.");
            return;
        }
        AudioServer.SetBusVolumeDb(bus, GameSettings.VolumeDb(volume));
    }
}
