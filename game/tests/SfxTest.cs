using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M2-6: the generated clips' shape, the Select / Command hooks through the real Match with injected input, the rate limit, and <c>--mute</c>.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/SfxTest.tscn</c>; prints
/// "SFX TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class SfxTest : Node
{
    private const int EdgeSamples = Sfx.MixRate * 2 / 1000; // 2 ms

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Sfx _sfx = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Simulation _sim = null!;
    private Vector2 _atA; // an own unit's screen point (Selecting)
    private ulong _lastActionUsec; // wall clock right after the last Expect action: any play it made was at or before this

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            // M6-V12 / BUG-0950: the no-LFS fallback first, before anything has loaded a clip.
            FallbackWhenMissing();
            ClipTable();
            ManifestAndFiles();
            WorldCapRows();
            MusicHook();
            Clips();
            RateLimit();
            ParseMute();
            await Brawl();
            await OwnCues();
            await StartMatch(mute: false);
            Check(!Sfx.Muted, "master bus muted without --mute");
            await Selecting();
            await FastGameClock();
            await Ordering();
            await StartMatch(mute: true);
            Check(Sfx.Muted, "master bus not muted with --mute");
            int before = _sfx.PlayCount(SfxEvent.Select);
            _sfx.Play(SfxEvent.Select);
            Check(_sfx.PlayCount(SfxEvent.Select) == before + 1, "counter did not run while muted");
            ExitAfterPlay();
            // BUG-0087: no wait here any more. The Select above is still sounding as the scene quits; Sfx._ExitTree
            // waits for the audio server's next mix, so the exit log has no ObjectDB leak warning.
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string a in new[] { "order_queue", "select_add", "select_type", "group_assign", "group_add" }) Input.ActionRelease(a);
        foreach (string f in _failures) GD.Print($"SFX TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SFX TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- M6-V12 ----

    // Criterion 3 and BUG-0950: a clip folder as a clone without LFS objects has it, built under user:// (no tracked file is
    // touched, BUG-0954): Alert's file is the LFS text pointer and its committed .import names an imported file the failed
    // import never wrote. The pool must see it as missing *before* ResourceLoader.Load (which logged four ERROR lines per clip
    // and failed the smoke gate and this scene in the loop), play the tone, and warn once per event. The real folder: every
    // clip is loadable.
    private void FallbackWhenMissing()
    {
        const string probe = "user://sfx_nolfs_probe";
        string pointer = Sfx.ClipPath(SfxEvent.Alert, 1, probe);
        DirAccess.MakeDirRecursiveAbsolute(pointer.GetBaseDir());
        using (Godot.FileAccess f = Godot.FileAccess.Open(pointer, Godot.FileAccess.ModeFlags.Write))
            f.StoreString("version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 105836\n");
        using (Godot.FileAccess f = Godot.FileAccess.Open(pointer + ".import", Godot.FileAccess.ModeFlags.Write))
            f.StoreString("[remap]\n\nimporter=\"wav\"\ntype=\"AudioStreamWAV\"\npath=\"res://.godot/imported/alert_1.wav-0000000000000000nolfsprobe.sample\"\n\n[deps]\n\nsource_file=\"" + pointer + "\"\n");
        try
        {
            // The engine takes the pointer for an imported resource, as it does in a no-LFS clone (else the row proves nothing).
            Check(ResourceLoader.Exists(pointer), $"fallback: ResourceLoader.Exists is false for the probe pointer {pointer}");
            Check(!Sfx.ClipLoadable(pointer), $"fallback: an LFS pointer with a dangling .import counts as loadable ({pointer})");
            foreach (SfxEvent e in Enum.GetValues<SfxEvent>())
                for (int v = 1; v <= Sfx.MaxVariants; v++)
                    if (ResourceLoader.Exists(Sfx.ClipPath(e, v))) Check(Sfx.ClipLoadable(Sfx.ClipPath(e, v)), $"fallback: the real {Sfx.ClipPath(e, v)} counts as not loadable");
            var sfx = new Sfx { ClipRoot = probe };
            AddChild(sfx);
            bool played = sfx.Play(SfxEvent.Alert, 1, 10_000_000);
            int events = Enum.GetValues<SfxEvent>().Length;
            GD.Print($"fallback: no-LFS Alert pointer -> tone {sfx.UsesTone(SfxEvent.Alert)}, {sfx.Warnings} warning(s) for {events} events, played {played}, stream is the tone {ReferenceEquals(sfx.LastStream(SfxEvent.Alert), sfx.Clip(SfxEvent.Alert))}");
            Check(sfx.UsesTone(SfxEvent.Alert) && sfx.Warnings == events, $"fallback: tone {sfx.UsesTone(SfxEvent.Alert)}, {sfx.Warnings} warnings (want the tone, one warning per event: {events})");
            Check(played && ReferenceEquals(sfx.LastStream(SfxEvent.Alert), sfx.Clip(SfxEvent.Alert)), "fallback: the Alert play did not start the tone");
            RemoveChild(sfx);
            sfx.QueueFree();
        }
        finally
        {
            DirAccess.RemoveAbsolute(pointer + ".import");
            DirAccess.RemoveAbsolute(pointer);
            DirAccess.RemoveAbsolute(pointer.GetBaseDir());
            DirAccess.RemoveAbsolute(probe);
        }
    }

    // Criterion 3: every SfxEvent has a clip row with at least one file; each loads as AudioStreamWav, mono, 16-bit, at most
    // 44,100 Hz, at most 3 s; no warning; at most 20 files.
    private void ClipTable()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        int files = 0;
        foreach (SfxEvent e in Enum.GetValues<SfxEvent>())
        {
            int n = sfx.VariantCount(e);
            files += n;
            Check(n >= 1 && n <= Sfx.MaxVariants, $"{e}: {n} clip files (want 1-{Sfx.MaxVariants})");
            for (int k = 0; k < n; k++)
            {
                AudioStream s = sfx.Variant(e, k);
                if (s is not AudioStreamWav w)
                {
                    Check(false, $"{e} variant {k + 1}: {s.GetClass()}, not AudioStreamWav");
                    continue;
                }
                double seconds = w.GetLength();
                GD.Print($"clip {Sfx.ClipPath(e, k + 1)}: {w.MixRate} Hz, stereo {w.Stereo}, {w.Format}, {seconds:F3} s, world {Sfx.IsWorld(e)}, gain {Sfx.GainDb(e)} dB");
                Check(!w.Stereo && w.MixRate <= 44100 && w.Format == AudioStreamWav.FormatEnum.Format16Bits && seconds > 0.03 && seconds <= 3.0,
                    $"{e} variant {k + 1}: {w.MixRate} Hz, stereo {w.Stereo}, {w.Format}, {seconds:F3} s");
            }
        }
        Check(files <= 20, $"{files} clip files, at most 20");
        Check(sfx.Warnings == 0, $"{sfx.Warnings} events fell back to the tone with every file in place");
        // The world / UI split the brief names.
        foreach (SfxEvent e in new[] { SfxEvent.MeleeHit, SfxEvent.Shot, SfxEvent.UnitDeath, SfxEvent.BuildingRazed, SfxEvent.Cast })
            Check(Sfx.IsWorld(e), $"{e} is not a world event");
        foreach (SfxEvent e in new[] { SfxEvent.Select, SfxEvent.Command, SfxEvent.Refused, SfxEvent.Ready, SfxEvent.Alert, SfxEvent.BuildingPlaced, SfxEvent.BuildingDone })
            Check(!Sfx.IsWorld(e), $"{e} is a world event");
        // Variants play in turn: MeleeHit's files one after another.
        int count = sfx.VariantCount(SfxEvent.MeleeHit);
        for (int k = 0; k < count; k++)
        {
            sfx.Play(SfxEvent.MeleeHit, (ulong)(1000 + k), 50_000_000UL + (ulong)k * 1_000_000UL);
            Check(ReferenceEquals(sfx.LastStream(SfxEvent.MeleeHit), sfx.Variant(SfxEvent.MeleeHit, k)), $"MeleeHit play {k + 1} did not start variant {k + 1}");
        }
        sfx.QueueFree();
    }

    // Criterion 2: every .wav under assets/audio/sfx is in tools/audio/sfx-manifest.json with a Sonniss source, <= 2 MB, and
    // the manifest lists no file that isn't there.
    private void ManifestAndFiles()
    {
        string game = ProjectSettings.GlobalizePath("res://");
        string repo = Path.GetFullPath(Path.Combine(game, ".."));
        string manifest = Path.Combine(repo, "tools", "audio", "sfx-manifest.json");
        if (!Check(File.Exists(manifest), $"no manifest at {manifest}")) return;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.Json.JsonElement c in doc.RootElement.GetProperty("clips").EnumerateArray())
        {
            string dst = c.GetProperty("destination").GetString() ?? "";
            string src = c.GetProperty("source").GetString() ?? "";
            Check(src.StartsWith("sonniss-gdc-2026/", StringComparison.Ordinal), $"manifest {dst}: source '{src}' is not from the Sonniss bundle");
            Check(listed.Add(dst), $"manifest lists {dst} twice");
            Check(Enum.TryParse(c.GetProperty("event").GetString(), out SfxEvent _), $"manifest {dst}: unknown event");
        }
        string root = Path.Combine(game, "assets", "audio", "sfx");
        int found = 0;
        foreach (string f in Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories))
        {
            found++;
            string rel = "game/" + Path.GetRelativePath(game, f).Replace('\\', '/');
            long size = new FileInfo(f).Length;
            Check(listed.Remove(rel), $"{rel} is not in the manifest");
            Check(size <= 2 * 1024 * 1024, $"{rel}: {size} bytes, over 2 MB");
        }
        Check(listed.Count == 0, $"manifest lists files that aren't there: {string.Join(", ", listed)}");
        GD.Print($"manifest: {found} clip files, all listed with a Sonniss source");
    }

    // Criterion 3, the cap on a hand-driven clock: five different world events inside 100 ms play four; UI events never
    // count against it nor get dropped by it; after the window the next world event plays.
    private void WorldCapRows()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        const ulong t = 20_000_000;
        SfxEvent[] world = { SfxEvent.MeleeHit, SfxEvent.Shot, SfxEvent.UnitDeath, SfxEvent.Cast, SfxEvent.BuildingRazed };
        int played = 0;
        for (int k = 0; k < world.Length; k++) if (sfx.Play(world[k], (ulong)(10 + k), t + (ulong)k * 10_000)) played++;
        Check(played == Sfx.WorldCap && sfx.WorldCapped == 1 && sfx.WorldPlays == Sfx.WorldCap, $"5 world events in 40 ms: {played} played, {sfx.WorldCapped} capped");
        // UI in the same window: all of them play.
        int ui = 0;
        foreach (SfxEvent e in new[] { SfxEvent.Select, SfxEvent.Command, SfxEvent.Refused, SfxEvent.Ready, SfxEvent.Alert, SfxEvent.BuildingPlaced, SfxEvent.BuildingDone })
            if (sfx.Play(e, 20, t + 45_000)) ui++;
        Check(ui == 7 && sfx.WorldCapped == 1, $"UI events under a full world window: {ui} of 7 played, capped {sfx.WorldCapped}");
        // 99 ms after the first world play the window is still full; at 100 ms the oldest has left it.
        Check(!sfx.Play(SfxEvent.BuildingRazed, 30, t + 99_000), "a world play 99 ms after the first of four was not capped");
        Check(sfx.Play(SfxEvent.BuildingRazed, 31, t + 100_000), "a world play 100 ms after the first of four was capped");
        for (int k = 0; k + Sfx.WorldCap < sfx.WorldPlayCount; k++)
            Check(sfx.WorldPlayUsec(k + Sfx.WorldCap) - sfx.WorldPlayUsec(k) >= 100_000, $"history: plays {k} and {k + Sfx.WorldCap} {sfx.WorldPlayUsec(k + Sfx.WorldCap) - sfx.WorldPlayUsec(k)} µs apart");
        // The capped play didn't stamp the event's gap: the same event plays on the next frame once the window allows.
        sfx.QueueFree();
    }

    // The music hook: a Music bus; with no track shipped the player finds none and stays silent; FindTracks lists .ogg files
    // (or, exported, their .import files) in name order and nothing else.
    private void MusicHook()
    {
        Check(AudioServer.GetBusIndex(GameSettings.MusicBus) > 0, "no Music bus");
        var music = new MusicPlayer();
        AddChild(music);
        Check(music.Tracks.Length == 0 && !music.Playing && music.Bus == GameSettings.MusicBus, $"music: {music.Tracks.Length} tracks, playing {music.Playing}, bus {music.Bus}");
        music.QueueFree();
        string dir = "user://music_probe";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (string name in new[] { "b.ogg", "a.ogg.import", "c.txt", "d.wav" })
            using (Godot.FileAccess f = Godot.FileAccess.Open($"{dir}/{name}", Godot.FileAccess.ModeFlags.Write)) f.StoreString("x");
        string[] tracks = MusicPlayer.FindTracks(dir);
        Check(tracks.Length == 2 && tracks[0] == $"{dir}/a.ogg" && tracks[1] == $"{dir}/b.ogg", $"FindTracks: [{string.Join(", ", tracks)}]");
        Check(MusicPlayer.FindTracks("user://no_such_music").Length == 0, "FindTracks on a missing folder");
        foreach (string name in new[] { "b.ogg", "a.ogg.import", "c.txt", "d.wav" }) DirAccess.RemoveAbsolute($"{dir}/{name}");
        DirAccess.RemoveAbsolute(dir);
        GD.Print($"music: Music bus {AudioServer.GetBusIndex(GameSettings.MusicBus)}, no track shipped, FindTracks ok");
    }

    // Criterion 3 under load: a 100 v 100 brawl on screen at 4x. Every 5 world plays span at least 100 ms (no window ever
    // held 5), world sounds did play (and the cap bit), and a UI sound tried every 120 ms during it always played.
    private async Task Brawl()
    {
        if (IsInstanceValid(_match)) _match.QueueFree();
        await Frame();
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "100", "--seed", "3", "--speed", "4", "--zoom", "60", "--mute" }));
        _sfx = _match.GetNode<Sfx>("Sfx");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        var cues = _match.GetNode<SoundCues>("SoundCues");
        while (_sim.TickNumber < 3) await Frame();
        UnitStore u = _sim.World.Units;
        System.Numerics.Vector2 c0 = default, c1 = default;
        int n0 = 0, n1 = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Owner[i] == 0) { c0 += u.Position[i]; n0++; }
            else { c1 += u.Position[i]; n1++; }
        }
        System.Numerics.Vector2 mid = (c0 / Math.Max(1, n0) + c1 / Math.Max(1, n1)) / 2;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid));
        _camera.SetFocus(mid.X, mid.Y);
        long plays0 = _sfx.WorldPlays, capped0 = _sfx.WorldCapped;
        int uiTried = 0, uiPlayed = 0, uiUnderFullWindow = 0;
        ulong lastUi = 0, start = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - start < 25_000_000UL && _sim.World.Losses[0] + _sim.World.Losses[1] < 120)
        {
            await Frame();
            ulong now = Time.GetTicksUsec();
            if (now - lastUi >= 120_000)
            {
                lastUi = now;
                bool full = _sfx.WorldPlayCount >= Sfx.WorldCap && now - _sfx.WorldPlayUsec(_sfx.WorldPlayCount - Sfx.WorldCap) < 100_000;
                uiTried++;
                if (full) uiUnderFullWindow++;
                if (_sfx.Play(SfxEvent.Command)) uiPlayed++;
            }
        }
        long plays = _sfx.WorldPlays - plays0, capped = _sfx.WorldCapped - capped0;
        int worst = 0, k0 = Math.Max(0, _sfx.WorldPlayCount - (int)Math.Min(plays, Sfx.WorldHistory));
        for (int k = k0; k + Sfx.WorldCap < _sfx.WorldPlayCount; k++)
            if (_sfx.WorldPlayUsec(k + Sfx.WorldCap) - _sfx.WorldPlayUsec(k) < 100_000) worst++;
        GD.Print($"brawl 100 v 100 at 4x: {_sim.World.Losses[0] + _sim.World.Losses[1]} deaths by tick {_sim.TickNumber}; world plays {plays}, capped {capped}; " +
            $"cued MeleeHit {cues.Cued(SfxEvent.MeleeHit)}, Shot {cues.Cued(SfxEvent.Shot)}, UnitDeath {cues.Cued(SfxEvent.UnitDeath)}, Cast {cues.Cued(SfxEvent.Cast)} " +
            $"(unseen deaths {cues.Unseen(SfxEvent.UnitDeath)}, shots {cues.Unseen(SfxEvent.Shot)}); windows of 5 under 100 ms: {worst}; " +
            $"UI tries {uiTried}, played {uiPlayed}, {uiUnderFullWindow} with the world window full");
        Check(plays >= 20, $"brawl: only {plays} world sounds played");
        Check(cues.Cued(SfxEvent.MeleeHit) > 0 && cues.Cued(SfxEvent.UnitDeath) > 0 && cues.Cued(SfxEvent.Shot) > 0, "brawl: no melee, death or shot cue");
        Check(worst == 0, $"brawl: {worst} windows held 5 world plays inside 100 ms");
        Check(uiTried >= 20 && uiPlayed == uiTried, $"brawl: UI sounds {uiPlayed} of {uiTried} played");
    }

    // The own-production cues in a match with bases at 8x: a site placed (BuildingPlaced), finished (BuildingDone), and a
    // worker trained at the Town Hall (Ready); a cancelled train is no Ready.
    private async Task OwnCues()
    {
        if (IsInstanceValid(_match)) _match.QueueFree();
        await Frame();
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--seed", "1", "--speed", "8", "--mute" }));
        _sfx = _match.GetNode<Sfx>("Sfx");
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        var cues = _match.GetNode<SoundCues>("SoundCues");
        while (_sim.TickNumber < 5) await Frame();
        World w = _sim.World;
        Check(cues.Cued(SfxEvent.BuildingPlaced) == 0 && cues.Cued(SfxEvent.BuildingDone) == 0 && cues.Cued(SfxEvent.Ready) == 0,
            "the start base cued a placed / done / ready sound");
        StartBasePlan bases = _match.Bases!;
        int house = -1;
        for (int t = 0; t < w.Data.Buildings.Length; t++)
            if (w.Data.Buildings[t].Faction == w.FactionOf(0) && w.Data.Buildings[t].Slot == BuildingSlot.House) house = t;
        UnitStore u = w.Units;
        int worker = -1;
        for (int i = 0; i < u.Capacity && worker < 0; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && u.TypeId[i] == bases.WorkerType[0]) worker = i;
        Rts.Sim.Map.NavGrid g = w.NavGrid;
        g.WorldToCell(u.Position[Math.Max(0, worker)], out int wx, out int wy);
        int anchor = -1;
        for (int r = 2; r < 12 && anchor < 0 && worker >= 0 && house >= 0; r++)
            for (int dy = -r; dy <= r && anchor < 0; dy++)
                for (int dx = -r; dx <= r && anchor < 0; dx++)
                    if (g.InBounds(wx + dx, wy + dy) && w.CanPlace(0, house, (wy + dy) * g.Width + wx + dx, out _)) anchor = (wy + dy) * g.Width + wx + dx;
        if (!Check(house >= 0 && worker >= 0 && anchor >= 0, $"own cues: house {house}, worker {worker}, anchor {anchor}")) return;
        _sim.Enqueue(Command.Build(0, new EntityHandle(worker, u.Generation[worker]), house, g.CellCenter(anchor % g.Width, anchor / g.Width)));
        await Until(() => cues.Cued(SfxEvent.BuildingPlaced) > 0, 10);
        Check(cues.Cued(SfxEvent.BuildingPlaced) == 1, $"site placed: BuildingPlaced cued {cues.Cued(SfxEvent.BuildingPlaced)}");
        await Until(() => cues.Cued(SfxEvent.BuildingDone) > 0, 40);
        Check(cues.Cued(SfxEvent.BuildingDone) == 1 && _sfx.PlayCount(SfxEvent.BuildingDone) == 1, $"site finished: BuildingDone cued {cues.Cued(SfxEvent.BuildingDone)}, played {_sfx.PlayCount(SfxEvent.BuildingDone)}");
        System.Numerics.Vector2 hall = StartBase.FootprintCenter(g, w.Data.Buildings[bases.HallType[0]], bases.HallAnchor[0]);
        // A train cancelled at once: the queue shrinks with no new unit, no Ready.
        _sim.Enqueue(Command.Train(0, hall, bases.WorkerType[0]));
        int hallSlot = DefeatStaging.BuildingAt(w, 0, bases.HallAnchor[0]).Index;
        await Until(() => w.Buildings.QueueCount[hallSlot] > 0, 5);
        _sim.Enqueue(Command.CancelTrain(0, hall, 0));
        await Until(() => w.Buildings.QueueCount[hallSlot] == 0, 5);
        await Frame();
        Check(cues.Cued(SfxEvent.Ready) == 0, $"a cancelled train cued Ready {cues.Cued(SfxEvent.Ready)} times");
        _sim.Enqueue(Command.Train(0, hall, bases.WorkerType[0]));
        await Until(() => cues.Cued(SfxEvent.Ready) > 0, 40);
        Check(cues.Cued(SfxEvent.Ready) == 1 && _sfx.PlayCount(SfxEvent.Ready) == 1, $"worker trained: Ready cued {cues.Cued(SfxEvent.Ready)}, played {_sfx.PlayCount(SfxEvent.Ready)}");
        GD.Print($"own cues at tick {_sim.TickNumber}: placed {cues.Cued(SfxEvent.BuildingPlaced)}, done {cues.Cued(SfxEvent.BuildingDone)}, ready {cues.Cued(SfxEvent.Ready)}");
    }

    // Waits frames until the condition holds or the wall-clock limit (s) passes.
    private async Task Until(Func<bool> done, double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (!done() && Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000)) await Frame();
    }

    // Criterion 1: length, peak, quiet edges, no NaN, both as floats and as the 16-bit clip.
    private void Clips()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        (SfxEvent e, float ms)[] rows = { (SfxEvent.Select, 70f), (SfxEvent.Command, 120f) };
        foreach ((SfxEvent e, float wantMs) in rows)
        {
            float[] s = Sfx.Synthesize(e);
            AudioStreamWav clip = sfx.Clip(e);
            byte[] data = clip.Data;
            Check(clip.Format == AudioStreamWav.FormatEnum.Format16Bits && clip.MixRate == Sfx.MixRate && !clip.Stereo, $"{e}: clip format {clip.Format} {clip.MixRate} Hz stereo {clip.Stereo}");
            Check(data.Length == s.Length * 2, $"{e}: {data.Length} bytes for {s.Length} samples");
            float ms = data.Length / 2 * 1000f / Sfx.MixRate;
            Check(Math.Abs(ms - wantMs) <= 5f, $"{e}: {ms:F1} ms, expected {wantMs} +- 5");
            float peak = 0f, head = 0f, tail = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float pcm = (short)(data[2 * i] | (data[2 * i + 1] << 8)) / 32768f;
                Check(float.IsFinite(s[i]), $"{e}: sample {i} is {s[i]}");
                float a = Math.Max(Math.Abs(s[i]), Math.Abs(pcm));
                peak = Math.Max(peak, a);
                if (i < EdgeSamples) head = Math.Max(head, a);
                if (i >= s.Length - EdgeSamples) tail = Math.Max(tail, a);
            }
            GD.Print($"clip {e}: {ms:F1} ms, peak {peak:F3}, first 2 ms {head:F4}, last 2 ms {tail:F4}");
            Check(peak <= 0.9f && peak > 0.2f, $"{e}: peak {peak}");
            Check(head < 0.05f && tail < 0.05f, $"{e}: edges {head} / {tail} not under 0.05");
        }
        Check(!sfx.Clip(SfxEvent.Select).Data.AsSpan().SequenceEqual(sfx.Clip(SfxEvent.Command).Data), "Select and Command clips are identical");
        sfx.QueueFree();
    }

    // Criterion 4 on a fresh pool with the clock driven by hand: 20 ms apart drops, 60 ms plays; one per frame.
    private void RateLimit()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        const ulong t = 10_000_000;
        sfx.Play(SfxEvent.Select, 100, t);
        sfx.Play(SfxEvent.Select, 101, t + 20_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 1, $"20 ms apart: {sfx.PlayCount(SfxEvent.Select)} plays, expected 1");
        sfx.Play(SfxEvent.Select, 102, t + 60_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 2, $"60 ms apart: {sfx.PlayCount(SfxEvent.Select)} plays, expected 2");
        Check(sfx.LastPlayedFrame(SfxEvent.Select) == 102, $"last played frame {sfx.LastPlayedFrame(SfxEvent.Select)}");
        sfx.Play(SfxEvent.Select, 102, t + 500_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 2, "a second play in the same frame was not dropped");
        // Events are limited separately.
        Check(sfx.LastPlayedFrame(SfxEvent.Command) == -1, "Command has a last frame before it ever played");
        sfx.Play(SfxEvent.Command, 102, t + 60_000);
        Check(sfx.PlayCount(SfxEvent.Command) == 1 && sfx.LastPlayedFrame(SfxEvent.Command) == 102, "Command was limited by Select");
        // Play allocates nothing once the clips exist (the pool's players are reused).
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (ulong k = 1; k <= 20; k++) sfx.Play(SfxEvent.Command, 102 + k, t + 60_000 + k * 100_000);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(sfx.PlayCount(SfxEvent.Command) == 21, $"20 spaced plays counted {sfx.PlayCount(SfxEvent.Command) - 1}");
        Check(bytes == 0, $"20 plays allocated {bytes} bytes");
        sfx.QueueFree();
    }

    // BUG-0087: a pool that played just now waits for one audio mix as it leaves the tree (well under the limit);
    // one that never played doesn't wait at all.
    private void ExitAfterPlay()
    {
        var quiet = new Sfx();
        AddChild(quiet);
        RemoveChild(quiet);
        Check(quiet.LastExitWaitMs == 0, $"a pool that never played waited {quiet.LastExitWaitMs} ms at exit");
        quiet.QueueFree();

        var sfx = new Sfx();
        AddChild(sfx);
        Check(sfx.Play(SfxEvent.Command), "Command did not play");
        RemoveChild(sfx);
        GD.Print($"exit right after a play: waited {sfx.LastExitWaitMs:F1} ms for an audio mix (limit {Sfx.ExitWaitLimitMs} ms)");
        Check(sfx.LastExitWaitMs > 0 && sfx.LastExitWaitMs < Sfx.ExitWaitLimitMs, $"exit after a play waited {sfx.LastExitWaitMs} ms");
        Check(sfx.StopAll() == 0, "a player still plays after the pool left the tree");
        sfx.QueueFree();
    }

    private void ParseMute()
    {
        Check(!LaunchOptions.Parse(Array.Empty<string>()).Mute, "Mute set without --mute");
        LaunchOptions o = LaunchOptions.Parse(new[] { "--mute", "--units", "5" });
        Check(o.Mute && o.UnitsPerPlayer == 5, "--mute swallowed the next flag");
        o = LaunchOptions.Parse(new[] { "--mute", "7", "--seed", "3" });
        Check(o.Mute && o.Seed == 3 && o.UnitsPerPlayer == LaunchOptions.DefaultUnitsPerPlayer, "--mute took a value");
    }

    private async Task StartMatch(bool mute)
    {
        if (IsInstanceValid(_match)) _match.QueueFree();
        await Frame();
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(mute ? new[] { "--units", "100", "--no-combat", "--mute" } : new[] { "--units", "100", "--no-combat" }));
        _sfx = _match.GetNode<Sfx>("Sfx");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        while (_sim.TickNumber < 3) await Frame();
    }

    // Criterion 2.
    private async Task Selecting()
    {
        UnitStore u = _sim.World.Units;
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;
        int a = -1;
        Vector2 atA = default;
        for (int i = 0; i < u.Capacity && a < 0; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer && _sel.TryScreenPosition(i, out atA) && atA.X > 50 && atA.Y > 50 && atA.X < screen.X - 50 && atA.Y < screen.Y - 50) a = i;
        Check(a >= 0, "no own unit on screen");
        _atA = atA;
        Vector2 empty = FindEmpty(screen);
        await Expect("click own unit", SfxEvent.Select, 1, () => Click(atA));
        await Expect("box", SfxEvent.Select, 1, () => Box(new Vector2(1, 1), screen - new Vector2(1, 1)));
        GD.Print($"box selected {_sel.Selection.Count}");
        await Expect("click empty ground", SfxEvent.Select, 0, () => Click(empty));
        await Expect("double-click a type", SfxEvent.Select, 1, () => Click(atA, doubleClick: true));
        await Expect("Ctrl+1", SfxEvent.Select, 0, () => { Input.ActionPress("group_assign"); Press(Key.Key1); Input.ActionRelease("group_assign"); });
        await Expect("click own unit again", SfxEvent.Select, 1, () => Click(atA));
        await Expect("group recall", SfxEvent.Select, 1, () => Press(Key.Key1));
        await Expect("Tab", SfxEvent.Select, 0, () => Press(Key.Tab));
        await Expect("click own unit", SfxEvent.Select, 1, () => Click(atA));
        await Expect("re-select the same unit", SfxEvent.Select, 0, () => Click(atA));
        Check(_sfx.LastPlayedFrame(SfxEvent.Select) >= 0, "LastPlayedFrame never set");
    }

    // Criterion 3.
    private async Task Ordering()
    {
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;
        Vector2 empty = FindEmpty(screen);
        Box(new Vector2(1, 1), screen - new Vector2(1, 1));
        int n = _sel.Selection.Count;
        await Expect("right-click", SfxEvent.Command, 1, () => RightClick(empty));
        await Expect("Shift+right-click", SfxEvent.Command, 1, () => { Input.ActionPress("order_queue"); RightClick(empty); Input.ActionRelease("order_queue"); });
        await Expect("S", SfxEvent.Command, 1, () => Press(Key.S));
        await Expect("H", SfxEvent.Command, 1, () => Press(Key.H));
        await Expect("A then click", SfxEvent.Command, 1, () => { Press(Key.A); Click(empty); });
        Rect2 mini = _match.GetNode<Minimap>("Hud/Minimap").GetGlobalRect();
        await Expect("minimap right-click", SfxEvent.Command, 1, () =>
        {
            Vector2 at = mini.GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Right });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
        });
        int dropped = _sel.DroppedOrders;
        await Expect("order dropped whole", SfxEvent.Command, 0, () =>
        {
            int fill = _sim.World.Config.CommandCapacity - _sim.PendingCommandCount - _sel.Selection.Count + 1;
            for (int i = 0; i < fill; i++) _sim.Enqueue(Command.Noop(0));
            RightClick(empty);
        });
        Check(_sel.DroppedOrders == dropped + 1, $"the overflow order was not dropped ({dropped} -> {_sel.DroppedOrders})");
        while (_sim.PendingCommandCount > 0) await Frame(); // let a tick drain the fill, or all 50 would be dropped too
        int moves = _sel.IssuedCount(CommandKind.Move);
        await Expect("50 right-clicks in one frame", SfxEvent.Command, 1, () => { for (int i = 0; i < 50; i++) RightClick(empty); });
        Check(_sel.IssuedCount(CommandKind.Move) > moves, "none of the 50 right-clicks enqueued anything");
        await Expect("right-click with nothing selected", SfxEvent.Command, 0, () => { _sel.Selection.Clear(); RightClick(empty); });
        GD.Print($"orders to {n} units: Command played {_sfx.PlayCount(SfxEvent.Command)} times, last frame {_sfx.LastPlayedFrame(SfxEvent.Command)}");
    }

    // BUG-0220 regression: with the game clock 8x the wall clock (Engine.TimeScale), a scene-tree timer of 0.06 s fires
    // after about 7.5 ms of wall time, so a wait on it lands inside Sfx's 50 ms wall-clock gap and the play is dropped (the
    // same disagreement a stalled frame's clamped delta causes under CPU load). Expect waits on the wall clock, so these
    // rows hold whatever the game clock does.
    private async Task FastGameClock()
    {
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;
        Vector2 empty = FindEmpty(screen);
        double scale = Engine.TimeScale;
        Engine.TimeScale = 8.0;
        try
        {
            // Box (all) and a click on one unit alternate, so every selection is a change (re-selecting the same set is silent).
            for (int k = 0; k < 3; k++)
            {
                await Expect($"8x game clock, box {k + 1}", SfxEvent.Select, 1, () => Box(new Vector2(1, 1), screen - new Vector2(1, 1)));
                await Expect($"8x game clock, click own unit {k + 1}", SfxEvent.Select, 1, () => Click(_atA));
            }
            // Orders last: they move the units off _atA.
            for (int k = 0; k < 3; k++) await Expect($"8x game clock, right-click {k + 1}", SfxEvent.Command, 1, () => RightClick(empty));
        }
        finally
        {
            Engine.TimeScale = scale;
        }
    }

    // Waits until the wall clock (the one Sfx's rate limit reads) is past the 50 ms gap since the last action, plus at
    // least one frame, runs the action, and checks how many times each event played. BUG-0220: waiting on a scene-tree
    // timer instead counted frame deltas, which can run ahead of the wall clock (a clamped delta after a stalled frame,
    // or a time scale), so under CPU load a play was dropped as too soon.
    private async Task Expect(string what, SfxEvent e, int want, Action action)
    {
        ulong gapUsec = (ulong)(Sfx.MinGapMs * 1000) + 10_000;
        do await Frame();
        while (Time.GetTicksUsec() - _lastActionUsec < gapUsec);
        SfxEvent other = e == SfxEvent.Select ? SfxEvent.Command : SfxEvent.Select;
        int before = _sfx.PlayCount(e), otherBefore = _sfx.PlayCount(other);
        action();
        _lastActionUsec = Time.GetTicksUsec();
        int got = _sfx.PlayCount(e) - before, otherGot = _sfx.PlayCount(other) - otherBefore;
        GD.Print($"{what}: {e} +{got}, {other} +{otherGot}");
        Check(got == want, $"{what}: {e} played {got} times, expected {want}");
        Check(otherGot == 0, $"{what}: {other} played {otherGot} times, expected 0");
    }

    // A screen point at least 30 px from every live unit, in the middle of the screen (so it is on the map).
    private Vector2 FindEmpty(Vector2 size)
    {
        UnitStore u = _sim.World.Units;
        for (float y = size.Y * 0.3f; y < size.Y * 0.8f; y += 10)
        {
            for (float x = size.X * 0.3f; x < size.X * 0.9f; x += 10)
            {
                bool clear = true;
                for (int i = 0; i < u.Capacity && clear; i++)
                    if (u.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p) && p.DistanceTo(new Vector2(x, y)) <= 30f) clear = false;
                if (clear) return new Vector2(x, y);
            }
        }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private void Press(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Click(Vector2 at, bool doubleClick = false)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, DoubleClick = doubleClick });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void Box(Vector2 from, Vector2 to)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = (from + to) / 2 });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = to });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
