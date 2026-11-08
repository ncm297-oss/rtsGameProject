using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Cli;

/// <summary>
/// M5-5: the <c>match</c> verb's match (docs/03 "Implementation (M5-5)"): AI seats only, each base a Town Hall and five
/// workers placed by commands from <see cref="StartBase.Plan"/> on its start, built exactly as the criterion tests build
/// theirs (<c>CriterionMatch.Build</c>), so the hashes are the tests' hashes; then played to its result or a tick limit.
/// </summary>
internal static class AiMatch
{
    /// <summary>The map id that builds the default generated map instead of a map file.</summary>
    public const string Generated = "generated";

    /// <summary>Ticks between the progress lines (5 minutes).</summary>
    public const int LineInterval = 6000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// A match of <paramref name="seats"/> on <paramref name="map"/>: the generated map (8 forests, 6 mines) or a map file
    /// sized to its side; unit capacity 400 for two seats, 1,024 for more; 1,024 commands. A recorder is attached before the
    /// first command when <paramref name="record"/>. Null with <paramref name="error"/> when a start has no room for a hall.
    /// </summary>
    public static Simulation? Build(GameData data, string map, ulong seed, PlayerSeat[] seats, bool record, out ReplayRecorder? recorder, out string error)
    {
        var config = new SimConfig(seed, seats.Length, seats.Length > 2 ? 1024 : 400, 1024)
            { Data = data, Map = MapGenParams.Default with { Forests = 8, GoldMines = 6 }, Seats = seats.ToImmutableArray() };
        if (map != Generated)
        {
            int size = data.Maps[data.FindMap(map)].Size;
            config = config with { MapId = map, Map = config.Map with { Width = size, Height = size } };
        }
        var sim = new Simulation(config);
        recorder = record ? new ReplayRecorder(sim, ReplayRecorder.DefaultCheckpointInterval, commandCapacity: 65_536) : null;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        float maxRadius = 0f;
        foreach (UnitDef d in data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[seats.Length][];
        var factions = new int[seats.Length];
        for (int p = 0; p < seats.Length; p++)
        {
            blocks[p] = Array.Empty<Vector2>();
            factions[p] = w.FactionOf(p);
        }
        StartBasePlan plan = StartBase.Plan(g, data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < seats.Length; p++)
        {
            int anchor = plan.HallAnchor[p];
            if (anchor < 0)
            {
                error = $"no room for player {p.ToString(Inv)}'s hall on '{map}' with seed {seed.ToString(Inv)}";
                recorder = null;
                return null;
            }
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            foreach (Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
        }
        error = "";
        return sim;
    }

    /// <summary>
    /// Plays <paramref name="sim"/> to its result or <paramref name="ticks"/>, printing a line every
    /// <see cref="LineInterval"/> ticks, then the result, the hashes, waves and hunts, and the refused commands by kind.
    /// True when the match has a result.
    /// </summary>
    public static bool Play(Simulation sim, int ticks, TextWriter stdout)
    {
        World w = sim.World;
        int n = w.Config.PlayerCount;
        var refused = new SortedDictionary<string, int>(StringComparer.Ordinal);
        // The sim's test seam: told of every command phase 1 drops (never read by the sim, never hashed).
        sim.RefusedForTests = (c, why, detail) =>
        {
            string kind = $"{c.Kind}:{why}" + why switch
            {
                CommandRefusal.BuildRefused => ":" + (PlacementError)detail,
                CommandRefusal.TrainRefused => ":" + (TrainError)detail,
                CommandRefusal.ResearchRefused => ":" + (ResearchError)detail,
                _ => "",
            };
            refused[kind] = refused.TryGetValue(kind, out int k) ? k + 1 : 1;
        };
        var fold = new StateHasher();
        var waving = new bool[n];
        int waves = 0;
        while (!sim.MatchOver && sim.TickNumber < ticks)
        {
            sim.Tick();
            fold.Add(sim.StateHash());
            for (int p = 0; p < n; p++)
            {
                Rts.Sim.Ai.AiPlayer? ai = sim.AiOf(p);
                if (ai == null) continue;
                if (ai.Board.WaveActive && !waving[p]) waves++;
                waving[p] = ai.Board.WaveActive;
            }
            if (sim.TickNumber % LineInterval == 0) stdout.WriteLine(ProgressLine(sim));
        }
        sim.RefusedForTests = null;
        int hunts = 0;
        for (int p = 0; p < n; p++) hunts += sim.AiOf(p)?.Board.HuntsStarted ?? 0;
        string end = $"tick {sim.TickNumber.ToString(Inv)} ({Clock(sim.TickNumber)})";
        string? winner = Winner(w);
        stdout.WriteLine(!sim.MatchOver ? $"result none by {end}" : winner == null ? $"result draw at {end}" : $"result winner {winner} at {end}");
        stdout.WriteLine($"end tick {sim.TickNumber.ToString(Inv)} hash {sim.StateHash().ToString("X16", Inv)} fold {fold.Value.ToString("X16", Inv)}");
        stdout.WriteLine($"waves {waves.ToString(Inv)} hunts {hunts.ToString(Inv)}");
        if (refused.Count == 0) stdout.WriteLine("refused none");
        foreach (KeyValuePair<string, int> kv in refused) stdout.WriteLine($"refused {kv.Key} {kv.Value.ToString(Inv)}");
        return sim.MatchOver;
    }

    /// <summary>One progress line: the tick, the clock, the state hash, and per player in or out, gathered gold and wood, army and production buildings.</summary>
    private static string ProgressLine(Simulation sim)
    {
        World w = sim.World;
        var parts = new List<string>();
        for (int p = 0; p < w.Config.PlayerCount; p++)
            parts.Add($"p{p.ToString(Inv)} {(w.IsDefeated(p) ? "out" : "in")} gold {w.GatheredGold[p].ToString(Inv)} wood {w.GatheredWood[p].ToString(Inv)}"
                + $" army {(sim.AiOf(p)?.ArmyCount ?? 0).ToString(Inv)} prod {w.ProductionBuildings(p).ToString(Inv)}");
        return $"tick {sim.TickNumber.ToString(Inv)} ({Clock(sim.TickNumber)}) hash {sim.StateHash().ToString("X16", Inv)}: {string.Join("; ", parts)}";
    }

    /// <summary>The one undefeated side (<c>pN</c> without a team, <c>team N</c> with one), or null for none or several.</summary>
    private static string? Winner(World w)
    {
        string? side = null;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p) || !w.EverBuilt(p)) continue;
            int team = w.Config.SeatOf(p).Team;
            string s = team == 0 ? $"p{p.ToString(Inv)}" : $"team {team.ToString(Inv)}";
            if (side != null && side != s) return null;
            side = s;
        }
        return side;
    }

    private static string Clock(int ticks) => $"{(ticks / 1200).ToString(Inv)}:{(ticks / 20 % 60).ToString("D2", Inv)}";
}
