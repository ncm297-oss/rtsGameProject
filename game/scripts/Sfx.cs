using System;
using Godot;

namespace Rts.Game;

/// <summary>Placeholder sound effects (docs/04 "Placeholder art"): one clip per <see cref="SfxEvent"/>, synthesized at start-up, played through a small player pool.</summary>
/// <remarks>
/// Reads nothing from the sim: callers (the selection controller today) decide when an event
/// happened and call <see cref="Play(SfxEvent)"/>. Each event plays at most once per frame and
/// never within <see cref="MinGapMs"/> of its last play, so a burst of input is one sound. The
/// counters (for tests) count plays whether or not the bus is muted. Adding an event (an alert, M3/M4) is an
/// enum member plus a row in <see cref="Table"/>.
/// </remarks>
public partial class Sfx : Node
{
    /// <summary>Volume of every effect in dB; a placeholder for the SFX volume setting (docs/02 "Settings", M6).</summary>
    public const float SfxVolumeDb = -6f;

    /// <summary>Clip sample rate (Hz); clips are mono 16-bit PCM.</summary>
    public const int MixRate = 44100;

    /// <summary>Players in the pool: the most effects that can sound at once.</summary>
    public const int PoolSize = 8;

    /// <summary>Shortest time between two plays of the same event.</summary>
    public const double MinGapMs = 50.0;

    private const int MasterBus = 0;

    // Envelope (ms): a cubic attack and release keep each note's edges silent (no clicks) and an
    // exponential decay makes it a blip rather than a beep. Peak stays well under 0.9.
    private const float Amplitude = 0.8f, AttackMs = 6f, ReleaseMs = 10f, DecayMs = 30f;

    private readonly record struct Note(float Hz, float Ms);

    // One row per SfxEvent, in enum order: the notes played back to back.
    private static readonly Note[][] Table =
    {
        new[] { new Note(1320f, 70f) },                    // Select: one short high blip
        new[] { new Note(660f, 60f), new Note(990f, 60f) }, // Command: a rising two-note confirm
    };

    private readonly AudioStreamWav[] _clips = new AudioStreamWav[Table.Length];
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private readonly int[] _plays = new int[Table.Length];
    private readonly long[] _lastFrame = new long[Table.Length];
    private readonly ulong[] _lastUsec = new ulong[Table.Length];
    private int _next;

    public override void _Ready()
    {
        if (Enum.GetValues<SfxEvent>().Length != Table.Length)
            throw new InvalidOperationException("Sfx.Table needs one row per SfxEvent");
        for (int e = 0; e < Table.Length; e++)
        {
            _clips[e] = ToWav(Synthesize((SfxEvent)e));
            _lastFrame[e] = -1;
        }
        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i] = new AudioStreamPlayer { Name = $"Player{i}", VolumeDb = SfxVolumeDb };
            AddChild(_pool[i]);
        }
    }

    /// <summary>Mutes or unmutes the master bus (<c>--mute</c>); counters keep running either way.</summary>
    public static void SetMuted(bool muted) => AudioServer.SetBusMute(MasterBus, muted);

    /// <summary>True if the master bus is muted.</summary>
    public static bool Muted => AudioServer.IsBusMute(MasterBus);

    /// <summary>Times <paramref name="e"/> has played (after rate limiting).</summary>
    public int PlayCount(SfxEvent e) => (uint)e < (uint)_plays.Length ? _plays[(int)e] : 0;

    /// <summary>Process frame of <paramref name="e"/>'s last play, or -1 if it never played.</summary>
    public long LastPlayedFrame(SfxEvent e) => (uint)e < (uint)_lastFrame.Length ? _lastFrame[(int)e] : -1;

    /// <summary>The generated clip for <paramref name="e"/>; null before <c>_Ready</c>.</summary>
    public AudioStreamWav Clip(SfxEvent e) => _clips[(int)e];

    /// <summary>Plays <paramref name="e"/> now unless the rate limit drops it; true if it played.</summary>
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
        _plays[i]++;
        _lastFrame[i] = (long)frame;
        _lastUsec[i] = nowUsec;
        // Headless runs play too: the dummy audio driver takes playback without logging errors.
        AudioStreamPlayer player = _pool[_next];
        _next = (_next + 1) % PoolSize;
        player.Stream = _clips[i];
        player.Play();
        return true;
    }

    /// <summary>The event's samples in [-1, 1] at <see cref="MixRate"/>: its notes back to back, each with its own envelope.</summary>
    public static float[] Synthesize(SfxEvent e)
    {
        Note[] notes = Table[(int)e];
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
