using System;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>Sound effects (M2-6, real clips since M6-V12): 1-3 clip files per <see cref="SfxEvent"/>, played in turn through a small player pool; an event without a file plays a tone synthesized at start-up.</summary>
/// <remarks>
/// Reads nothing from the sim: callers (the selection controller, the ability views, <see cref="SoundCues"/>,
/// <see cref="AlertHud"/>) decide when an event happened and call <see cref="Play(SfxEvent)"/>. Each event plays at most
/// once per frame and never within <see cref="MinGapMs"/> of its last play, so a burst is one sound. The world events
/// (<see cref="IsWorld"/>) also share a cap: at most <see cref="WorldCap"/> of them start in any <see cref="WorldWindowMs"/>
/// window; the player's own feedback (UI events) is never capped. The counters (for tests) count plays whether or not the bus
/// is muted.
/// <para><b>Clip table.</b> One row per event, in enum order: its folder under <see cref="Root"/> (the file name is the
/// event's snake_case name and the variant, <see cref="ClipPath"/>: no file is named in C#), whether it is a world event,
/// a gain on top of <see cref="SfxVolumeDb"/>, and the fallback tone's notes. The files are cut from the owner's Sonniss
/// bundle by <c>tools/audio/trim-wav.ps1</c> from <c>tools/audio/sfx-manifest.json</c>; the import makes them mono 16-bit at
/// most 44,100 Hz. An event with no loadable file (a clone without <c>git lfs pull</c>, a file moved aside) plays its tone
/// and logs one warning; <see cref="Clip"/> is always the tone.</para>
/// </remarks>
public partial class Sfx : Node
{
    /// <summary>Volume of every effect's player in dB before the row's gain; the player's SFX volume setting is the <c>Sfx</c> bus's gain on top (M6-V6).</summary>
    public const float SfxVolumeDb = -6f;

    /// <summary>The bus every effect plays on (<c>game/default_bus_layout.tres</c>; it sends to Master), so the SFX volume setting reaches them.</summary>
    public const string Bus = GameSettings.SfxBus;

    /// <summary>Fallback tone sample rate (Hz); tones are mono 16-bit PCM.</summary>
    public const int MixRate = 44100;

    /// <summary>Players in the pool: the most effects that can sound at once.</summary>
    public const int PoolSize = 8;

    /// <summary>Shortest time between two plays of the same event.</summary>
    public const double MinGapMs = 50.0;

    /// <summary>The folder the clip files live under: <c>&lt;Root&gt;/&lt;category&gt;/&lt;event&gt;_&lt;n&gt;.wav</c> (docs/04 "Directory layout").</summary>
    public const string Root = "res://assets/audio/sfx";

    /// <summary>Most clip files (variants) per event; they play in turn.</summary>
    public const int MaxVariants = 3;

    /// <summary>Most world sounds that may start in one <see cref="WorldWindowMs"/> window.</summary>
    public const int WorldCap = 4;

    /// <summary>The world-sound cap's window, in milliseconds of wall clock.</summary>
    public const double WorldWindowMs = 100.0;

    /// <summary>World play start times kept for tests (<see cref="WorldPlayUsec"/>).</summary>
    public const int WorldHistory = 256;

    private const int MasterBus = 0;

    // Envelope (ms): a cubic attack and release keep each note's edges silent (no clicks) and an
    // exponential decay makes it a blip rather than a beep. Peak stays well under 0.9.
    private const float Amplitude = 0.8f, AttackMs = 6f, ReleaseMs = 10f, DecayMs = 30f;

    private readonly record struct Note(float Hz, float Ms);

    private readonly record struct Row(string Category, bool World, float GainDb, Note[] Tone);

    // One row per SfxEvent, in enum order. The tones of Select, Command and Refused are the M2-6 / M5-V1 ones (QaM26Test).
    private static readonly Row[] Table =
    {
        new("ui", false, 0f, new[] { new Note(1320f, 70f) }),                                         // Select: one short high blip
        new("ui", false, 0f, new[] { new Note(660f, 60f), new Note(990f, 60f) }),                     // Command: a rising two-note confirm
        new("ui", false, 0f, new[] { new Note(392f, 80f), new Note(262f, 110f) }),                    // Refused: a low falling "no"
        new("combat", true, -4f, new[] { new Note(196f, 60f) }),                                      // MeleeHit: a low knock
        new("combat", true, -8f, new[] { new Note(1760f, 40f) }),                                     // Shot: a thin tick
        new("combat", true, -4f, new[] { new Note(220f, 80f), new Note(147f, 120f) }),                // UnitDeath: a falling thud
        new("building", false, -2f, new[] { new Note(330f, 80f) }),                                   // BuildingPlaced
        new("building", false, -2f, new[] { new Note(523f, 70f), new Note(659f, 70f), new Note(784f, 110f) }), // BuildingDone: a rising triad
        new("building", true, -2f, new[] { new Note(110f, 200f) }),                                   // BuildingRazed: a deep boom
        new("ui", false, -2f, new[] { new Note(880f, 70f), new Note(1175f, 90f) }),                   // Ready
        new("combat", true, -5f, new[] { new Note(740f, 110f), new Note(988f, 130f) }),               // Cast
        new("ui", false, -4f, new[] { new Note(880f, 140f), new Note(660f, 140f), new Note(880f, 140f) }), // Alert: a two-tone call
    };

    private readonly AudioStreamWav[] _clips = new AudioStreamWav[Table.Length];
    private readonly AudioStream[][] _variants = new AudioStream[Table.Length][];
    private readonly AudioStream?[] _lastStream = new AudioStream?[Table.Length];
    private readonly int[] _nextVariant = new int[Table.Length];
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private readonly int[] _plays = new int[Table.Length];
    private readonly long[] _lastFrame = new long[Table.Length];
    private readonly ulong[] _lastUsec = new ulong[Table.Length];
    private readonly ulong[] _worldRecent = new ulong[WorldCap];
    private readonly ulong[] _worldHistory = new ulong[WorldHistory];
    private int _worldNext, _worldFilled, _historyNext, _historyCount;
    private int _next;
    private ulong _startedUsec; // wall clock of the last sound actually started (not the limiter's test clock)
    // A sound started this recently (the longest clip plus a margin) may still be in the audio server.
    private ulong _recentUsec = (ulong)(LongestToneMs() + 100f) * 1000UL;

    public override void _Ready()
    {
        if (Enum.GetValues<SfxEvent>().Length != Table.Length)
            throw new InvalidOperationException("Sfx.Table needs one row per SfxEvent");
        double longest = LongestToneMs();
        for (int e = 0; e < Table.Length; e++)
        {
            _clips[e] = ToWav(Synthesize((SfxEvent)e));
            _lastFrame[e] = -1;
            _variants[e] = LoadVariants((SfxEvent)e);
            foreach (AudioStream s in _variants[e]) longest = Math.Max(longest, s.GetLength() * 1000.0);
            if (_variants[e].Length == 0)
            {
                Warnings++;
                GD.PushWarning($"Sfx: no clip file for {(SfxEvent)e} ({ClipPath((SfxEvent)e, 1)} .. _{MaxVariants}); the generated tone plays instead.");
            }
        }
        _recentUsec = (ulong)(longest + 100.0) * 1000UL;
        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i] = new AudioStreamPlayer { Name = $"Player{i}", VolumeDb = SfxVolumeDb, Bus = Bus };
            AddChild(_pool[i]);
        }
    }

    /// <summary>The clip file of variant <paramref name="variant"/> (1-based) of <paramref name="e"/>: <c>res://assets/audio/sfx/&lt;category&gt;/&lt;event&gt;_&lt;variant&gt;.wav</c>.</summary>
    public static string ClipPath(SfxEvent e, int variant) =>
        string.Create(CultureInfo.InvariantCulture, $"{Root}/{Table[(int)e].Category}/{UiText.Key(e)}_{variant}.wav");

    /// <summary>True for a world event (capped, played by <see cref="SoundCues"/> only for what the player sees on screen).</summary>
    public static bool IsWorld(SfxEvent e) => (uint)e < (uint)Table.Length && Table[(int)e].World;

    /// <summary>The row's gain in dB on top of <see cref="SfxVolumeDb"/>.</summary>
    public static float GainDb(SfxEvent e) => (uint)e < (uint)Table.Length ? Table[(int)e].GainDb : 0f;

    /// <summary>Clip files loaded for <paramref name="e"/> (0: the tone plays).</summary>
    public int VariantCount(SfxEvent e) => _variants[(int)e]?.Length ?? 0;

    /// <summary>Loaded clip <paramref name="k"/> (0-based) of <paramref name="e"/>.</summary>
    public AudioStream Variant(SfxEvent e, int k) => _variants[(int)e][k];

    /// <summary>True when <paramref name="e"/> has no clip file and plays its tone.</summary>
    public bool UsesTone(SfxEvent e) => VariantCount(e) == 0;

    /// <summary>Events that fell back to the tone in <c>_Ready</c> (one warning each).</summary>
    public int Warnings { get; private set; }

    /// <summary>The stream <paramref name="e"/>'s last play started (a clip file or the tone); null before it played.</summary>
    public AudioStream? LastStream(SfxEvent e) => _lastStream[(int)e];

    /// <summary>World sounds started so far.</summary>
    public long WorldPlays { get; private set; }

    /// <summary>World sounds dropped by the <see cref="WorldCap"/> cap (not by the per-event gap).</summary>
    public long WorldCapped { get; private set; }

    /// <summary>World plays kept in the history (at most <see cref="WorldHistory"/>).</summary>
    public int WorldPlayCount => _historyCount;

    /// <summary>The start time (the play's clock, µs) of kept world play <paramref name="k"/>, oldest first.</summary>
    public ulong WorldPlayUsec(int k) => _worldHistory[(_historyNext - _historyCount + k + 2 * WorldHistory) % WorldHistory];

    // Every existing variant file of the event, loaded; a path that exists but doesn't load (an LFS pointer) counts as missing.
    private static AudioStream[] LoadVariants(SfxEvent e)
    {
        var found = new System.Collections.Generic.List<AudioStream>(MaxVariants);
        for (int v = 1; v <= MaxVariants; v++)
        {
            string path = ClipPath(e, v);
            if (!ResourceLoader.Exists(path)) continue;
            if (ResourceLoader.Load(path) is AudioStream s) found.Add(s);
        }
        return found.ToArray();
    }

    /// <summary>Longest time <see cref="_ExitTree"/> waits for the audio thread to drop stopped sounds, in milliseconds.</summary>
    public const int ExitWaitLimitMs = 200;

    /// <summary>Milliseconds the last <see cref="_ExitTree"/> waited for the audio thread (0 if no sound had started recently).</summary>
    public double LastExitWaitMs { get; private set; }

    public override void _ExitTree()
    {
        // BUG-0087: the audio server drops a stopped or finished playback only on its next mix, on
        // the audio thread. Quitting before that mix left the clip and its playback in the server: an
        // ObjectDB leak warning at exit. The pool players stop their sounds as they leave the tree
        // (children leave before this node), so if a sound started recently, wait (bounded) for one mix.
        StopAll();
        if (_startedUsec > 0 && Time.GetTicksUsec() - _startedUsec < _recentUsec) LastExitWaitMs = WaitForAMix();
    }

    public override void _Notification(int what)
    {
        // The window's close button: stop early, before the engine starts tearing the tree down.
        if (what == NotificationWMCloseRequest) StopAll();
    }

    /// <summary>Stops every pool player; returns how many were playing.</summary>
    public int StopAll()
    {
        int playing = 0;
        foreach (AudioStreamPlayer? player in _pool)
        {
            if (player == null || !IsInstanceValid(player)) continue;
            if (player.Playing) playing++;
            player.Stop();
        }
        return playing;
    }

    // Blocks until a mix that started after this call has finished (it removes stopped playbacks), or the limit.
    private static double WaitForAMix()
    {
        ulong start = Time.GetTicksUsec();
        while (true)
        {
            ulong waited = Time.GetTicksUsec() - start;
            // The last mix began after `start`: take the server lock, which the driver holds while it mixes.
            if (AudioServer.GetTimeSinceLastMix() * 1_000_000.0 < waited)
            {
                AudioServer.Lock();
                AudioServer.Unlock();
                break;
            }
            if (waited >= ExitWaitLimitMs * 1000UL) break;
            OS.DelayUsec(500);
        }
        return (Time.GetTicksUsec() - start) / 1000.0;
    }

    /// <summary>Mutes or unmutes the master bus (<c>--mute</c>); counters keep running either way.</summary>
    public static void SetMuted(bool muted) => AudioServer.SetBusMute(MasterBus, muted);

    /// <summary>True if the master bus is muted.</summary>
    public static bool Muted => AudioServer.IsBusMute(MasterBus);

    /// <summary>Times <paramref name="e"/> has played (after rate limiting and the world cap).</summary>
    public int PlayCount(SfxEvent e) => (uint)e < (uint)_plays.Length ? _plays[(int)e] : 0;

    /// <summary>Process frame of <paramref name="e"/>'s last play, or -1 if it never played.</summary>
    public long LastPlayedFrame(SfxEvent e) => (uint)e < (uint)_lastFrame.Length ? _lastFrame[(int)e] : -1;

    /// <summary>The generated fallback tone for <paramref name="e"/>; null before <c>_Ready</c>.</summary>
    public AudioStreamWav Clip(SfxEvent e) => _clips[(int)e];

    /// <summary>Plays <paramref name="e"/> now unless the rate limit or the world cap drops it; true if it played.</summary>
    public bool Play(SfxEvent e) => Play(e, Engine.GetProcessFrames(), Time.GetTicksUsec());

    /// <summary>Plays <paramref name="e"/> as if at process frame <paramref name="frame"/> and time <paramref name="nowUsec"/> (tests drive the clock here).</summary>
    public bool Play(SfxEvent e, ulong frame, ulong nowUsec)
    {
        int i = (int)e;
        if ((uint)i >= (uint)_plays.Length || _clips[i] == null) return false;
        if (_lastFrame[i] >= 0)
        {
            if ((long)frame == _lastFrame[i]) return false;
            if ((long)nowUsec - (long)_lastUsec[i] < (long)(MinGapMs * 1000)) return false;
        }
        Row row = Table[i];
        if (row.World)
        {
            // The oldest of the last WorldCap world plays is still inside the window: this one would be the (cap + 1)-th.
            if (_worldFilled == WorldCap && (long)nowUsec - (long)_worldRecent[_worldNext] < (long)(WorldWindowMs * 1000))
            {
                WorldCapped++;
                return false;
            }
            _worldRecent[_worldNext] = nowUsec;
            _worldNext = (_worldNext + 1) % WorldCap;
            if (_worldFilled < WorldCap) _worldFilled++;
            _worldHistory[_historyNext] = nowUsec;
            _historyNext = (_historyNext + 1) % WorldHistory;
            if (_historyCount < WorldHistory) _historyCount++;
            WorldPlays++;
        }
        _plays[i]++;
        _lastFrame[i] = (long)frame;
        _lastUsec[i] = nowUsec;
        AudioStream[] files = _variants[i];
        AudioStream stream = files.Length > 0 ? files[_nextVariant[i]++ % files.Length] : _clips[i];
        _lastStream[i] = stream;
        // Headless runs play too: the dummy audio driver takes playback without logging errors.
        AudioStreamPlayer player = _pool[_next];
        _next = (_next + 1) % PoolSize;
        player.Stream = stream;
        player.VolumeDb = SfxVolumeDb + row.GainDb;
        player.Play();
        _startedUsec = Math.Max(1UL, Time.GetTicksUsec());
        return true;
    }

    /// <summary>The event's fallback tone in [-1, 1] at <see cref="MixRate"/>: its notes back to back, each with its own envelope.</summary>
    public static float[] Synthesize(SfxEvent e)
    {
        Note[] notes = Table[(int)e].Tone;
        int total = 0;
        foreach (Note n in notes) total += Samples(n.Ms);
        var samples = new float[total];
        int at = 0;
        foreach (Note n in notes)
        {
            int count = Samples(n.Ms);
            for (int s = 0; s < count; s++)
            {
                float ms = s * 1000f / MixRate;
                float attack = Cube(Math.Min(1f, ms / AttackMs));
                float release = Cube(Math.Min(1f, (n.Ms - ms) / ReleaseMs));
                float envelope = attack * release * MathF.Exp(-ms / DecayMs);
                samples[at + s] = Amplitude * envelope * MathF.Sin(2f * MathF.PI * n.Hz * ms / 1000f);
            }
            at += count;
        }
        return samples;
    }

    private static float LongestToneMs()
    {
        float longest = 0f;
        foreach (Row row in Table)
        {
            float ms = 0f;
            foreach (Note n in row.Tone) ms += n.Ms;
            longest = Math.Max(longest, ms);
        }
        return longest;
    }

    private static int Samples(float ms) => (int)MathF.Round(ms * MixRate / 1000f);

    private static float Cube(float x) => x * x * x;

    // Mono 16-bit little-endian PCM.
    private static AudioStreamWav ToWav(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)MathF.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[2 * i] = (byte)(v & 0xFF);
            data[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Stereo = false, Data = data };
    }
}
