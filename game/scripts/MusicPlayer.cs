using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game;

/// <summary>The music hook (M6-V12): plays every <c>res://assets/audio/music/*.ogg</c> in name order on the <c>Music</c> bus, round and round; with no file it stays silent.</summary>
/// <remarks>
/// No track ships (music is the owner's call, docs/04 "UI, fonts, audio"): dropping <c>.ogg</c> files in the folder and
/// importing them is all it takes. The music volume setting is the <c>Music</c> bus's gain (M6-V6); <c>--mute</c> mutes the
/// master bus over it. A child of the shell (<c>Main</c>), so it plays on through the menu, a match and the pause menu.
/// </remarks>
public partial class MusicPlayer : AudioStreamPlayer
{
    /// <summary>The folder the tracks are read from (docs/04 "Directory layout": <c>audio/music/&lt;track&gt;.ogg</c>).</summary>
    public const string Folder = "res://assets/audio/music";

    private int _track = -1;

    /// <summary>The tracks found at <c>_Ready</c>, in name order (empty: no music).</summary>
    public string[] Tracks { get; private set; } = Array.Empty<string>();

    /// <summary>The index in <see cref="Tracks"/> playing (or last played); -1 before any.</summary>
    public int Track => _track;

    public override void _Ready()
    {
        Bus = GameSettings.MusicBus;
        ProcessMode = ProcessModeEnum.Always;
        Tracks = FindTracks(Folder);
        Finished += PlayNext;
        PlayNext();
    }

    /// <summary>The <c>.ogg</c> resources in <paramref name="folder"/>, in name order (an exported build lists only their <c>.import</c> files: those count); none when the folder is missing.</summary>
    public static string[] FindTracks(string folder)
    {
        if (!DirAccess.DirExistsAbsolute(folder)) return Array.Empty<string>();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in DirAccess.GetFilesAt(folder))
        {
            string name = file.EndsWith(".import", StringComparison.OrdinalIgnoreCase) ? file[..^".import".Length] : file;
            if (name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) found.Add($"{folder}/{name}");
        }
        var tracks = new string[found.Count];
        found.CopyTo(tracks);
        return tracks;
    }

    // The next track that loads, wrapping round; none loading: silence (one warning per bad file).
    private void PlayNext()
    {
        for (int tried = 0; tried < Tracks.Length; tried++)
        {
            _track = (_track + 1) % Tracks.Length;
            if (ResourceLoader.Load(Tracks[_track]) is AudioStream s)
            {
                Stream = s;
                Play();
                return;
            }
            GD.PushWarning($"MusicPlayer: {Tracks[_track]} did not load; skipped.");
        }
    }
}
