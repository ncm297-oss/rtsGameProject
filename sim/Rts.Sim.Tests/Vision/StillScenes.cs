using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 test copies: the shipped data plus a <c>stillStealth</c> block (<c>stealthed</c>, 3 s, the Shadow Archer's numbers
/// from docs/factions/shadow.md) on five unit types of all three factions: the Malazan Heavy Infantry, Crossbowman and
/// Laborer, the Whirlwind Desert Archer and the Teblor Javelin Thrower; and the M4-5 test-only stealth sources
/// (<see cref="StealthTests.AddTo"/>: the Heavy Infantry's 60 s <c>zz_test_stealth</c>, the Malazan Laborer's 10 m detector).
/// <see cref="Both"/> also gives the Malazan the M8-1 forest block (<see cref="ForestScenes.Block"/>, 3 s, siege excepted).
/// No shipped file changes. The block is written over whatever the shipped row holds, so a shipped row that already
/// carries the field (the Shadow roster, later) changes nothing here (rule 58).
/// </summary>
internal static class StillScenes
{
    /// <summary>The unit types the copies give the field.</summary>
    internal static readonly (string Faction, string Unit)[] FieldUnits =
    {
        ("malazan", "malazan_heavy_infantry"), ("malazan", "malazan_crossbowman"), ("malazan", "malazan_laborer"),
        ("whirlwind", "whirlwind_desert_archer"), ("teblor", "teblor_javelin_thrower"),
    };

    /// <summary>The field on <see cref="FieldUnits"/>, no forest block.</summary>
    internal static GameData Still => s_still.Value;

    /// <summary>As <see cref="Still"/>, plus the Malazan forest block (3 s, siege excepted).</summary>
    internal static GameData Both => s_both.Value;

    private static readonly Lazy<GameData> s_still = new(() => DataWith(3, forest: false));
    private static readonly Lazy<GameData> s_both = new(() => DataWith(3, forest: true));

    /// <summary>The block as JSON text.</summary>
    internal static string Block(double delay, string status = "stealthed") =>
        new JsonObject { ["status"] = status, ["delay"] = delay }.ToJsonString();

    /// <summary>The test copy with the field at <paramref name="delay"/> seconds on <see cref="FieldUnits"/> (see the type's summary).</summary>
    internal static GameData DataWith(double delay, bool forest) => AiScenes.DataWith(dir =>
    {
        foreach ((string f, string unit) in FieldUnits) dir.SetUnitField(f, unit, "stillStealth", Block(delay));
        if (forest) dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = ForestScenes.Block(3));
        StealthTests.AddTo(dir);
        // Eight do-nothing marks (frenzied at magnitude 0), so a test can fill a unit's 8 status slots without stealth.
        dir.EditJson("common/statuses.json", r =>
        {
            for (int k = 0; k < Abilities.StatusStore.PerUnit; k++)
                r["statuses"]!.AsArray().Add(new JsonObject { ["id"] = $"zz_test_mark_{k}", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "frenzied" });
        });
    });

    /// <summary>The status id of do-nothing mark <paramref name="k"/> (0-7).</summary>
    internal static int Mark(GameData d, int k) => d.FindStatus($"zz_test_mark_{k}");

    /// <summary>A flat sim on <paramref name="data"/> (players 0-2: Malazan, Whirlwind, Teblor).</summary>
    internal static Simulation Scene(GameData data, int units = 64, bool combat = true, int size = 48, int players = 2) =>
        new(new SimConfig(1, players, units, 8 * units + 32) { Data = data, Combat = combat }, LocalMovementTests.Flat(size));

    /// <summary>The status id the field holds.</summary>
    internal static int Stealthed(GameData d) => d.FindStatus("stealthed");

    /// <summary>Whether the stand-still rule holds unit <paramref name="h"/>'s stealth.</summary>
    internal static bool Held(Simulation sim, EntityHandle h) => sim.World.Units.Still!.Held[h.Index] != 0;

    /// <summary>Whether the forest holds unit <paramref name="h"/>'s stealth (false on data without the forest block).</summary>
    internal static bool ForestHeld(Simulation sim, EntityHandle h) => sim.World.Units.Forest is { } f && f.Held[h.Index] != 0;

    /// <summary>The ticks the timed applications left on <paramref name="h"/>'s Stealthed entry (every held tick taken off), or -1 with no entry.</summary>
    internal static int OwnTicks(Simulation sim, EntityHandle h)
    {
        var s = sim.World.Units.Statuses;
        int at = s.IndexOf(h.Index, Stealthed(sim.World.Data));
        return at < 0 ? -1 : s.TicksRemaining[at] - (Held(sim, h) ? 1 : 0) - (ForestHeld(sim, h) ? 1 : 0);
    }

    internal static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Tick();
    }

    /// <summary>The cell centre of (x, y) offset by (<paramref name="dx"/>, <paramref name="dy"/>) m.</summary>
    internal static Vector2 At(Simulation sim, int x, int y, float dx = 0f, float dy = 0f) => CombatScenes.At(sim, x, y, dx, dy);

    /// <summary>
    /// The invariant the fuzz and the match rows check after every tick: for every live unit, the stand-still rule holds it
    /// only if it is of a type with the field, not summoned, and every count (move, attack, hp loss) has passed by the tick
    /// just run (an attack noted in that tick, after the pass, is let go by the next one); a held unit carries the entry with at least its holders' ticks; a unit no rule holds and no timed application
    /// covers has no entry left (no held entry outlives its holders). <paramref name="timed"/> says, per slot, whether a timed
    /// application of the status may still be running (the caller's record), or null when the run makes none.
    /// </summary>
    internal static void AssertInvariant(Simulation sim, bool[]? timed)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        Abilities.StillStealthStore st = u.Still!;
        int tick = sim.TickNumber - 1;
        int status = Stealthed(w.Data);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                Assert.True(st.Held[i] == 0 && st.MovedTick[i] == -1, $"tick {tick}: free slot {i} has state");
                continue;
            }
            UnitDef def = w.Data.Units[u.TypeId[i]];
            int at = u.Statuses.IndexOf(i, status);
            if (st.Held[i] != 0)
            {
                int delay = def.StillStealthDelayTicks;
                Assert.True(def.HasStillStealth && !u.IsSummoned(i), $"tick {tick}: slot {i} held without the field");
                Assert.True(tick - st.MovedTick[i] >= delay, $"tick {tick}: slot {i} held {tick - st.MovedTick[i]} ticks after its move");
                // An attack landing (phase 11) or a cast resolving (phase 6) after the pass is let go by the next pass.
                Assert.True(st.LastAttackTick[i] < 0 || st.LastAttackTick[i] == tick || tick - st.LastAttackTick[i] >= delay, $"tick {tick}: slot {i} held {tick - st.LastAttackTick[i]} ticks after its attack");
                Assert.True(st.LastHurtTick[i] < 0 || tick - st.LastHurtTick[i] >= delay, $"tick {tick}: slot {i} held {tick - st.LastHurtTick[i]} ticks after its hp loss");
                Assert.True(at >= 0 && u.Statuses.TicksRemaining[at] >= Abilities.StatusSystem.Holders(w, i, status), $"tick {tick}: slot {i} held without its entry");
            }
            else if (at >= 0 && (timed == null || !timed[i]))
                Assert.True(Abilities.StatusSystem.Holders(w, i, status) > 0, $"tick {tick}: slot {i} keeps an entry no rule holds");
        }
    }
}
