using System.Text.RegularExpressions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S4: every "is this an enemy" site in <c>sim/Rts.Sim/</c>'s <c>Combat/</c>, <c>Abilities/</c>, <c>Vision/</c>, <c>Ai/</c>
/// and the <c>World*.cs</c> partials (BUG-1044: the scan began with <c>Combat/</c> only) asks the one alliance predicate
/// (<see cref="World.AreAllied"/> / <see cref="World.IsEnemy"/>, or the spatial hash's enemy queries, which take the same
/// table). A raw owner compare is allowed only where it means "the same player" (docs/03 "Implementation (M6-S4)"'s
/// table): the four listed below, or as the fast first half of a predicate test on the same line
/// (<c>u.Owner[j] == owner || (world.HasAlliances &amp;&amp; world.AreAllied(owner, u.Owner[j]))</c>). A new one fails this test
/// until it goes through the predicate or is added to the table.
/// </summary>
public class TeamArchitectureTests
{
    // An owner compared with == or != (an Owner[...] read, or a local named *owner / *Owner).
    private static readonly Regex s_rawOwnerCompare = new(@"Owner(\[[^\]]*\])?\s*[!=]=|[!=]=\s*[A-Za-z_.]*[Oo]wner\b|\b[a-z]*[Oo]wner\s*[!=]=", RegexOptions.Compiled);

    /// <summary>The same-player sites (docs/03's table, "untouched"), by file and the code of the line, trimmed.</summary>
    private static readonly (string File, string Code)[] s_samePlayer =
    {
        ("CombatSystem.cs", "if (j == i || !u.Alive[j] || u.Owner[j] != owner || u.State[j] != UnitState.Attacking) continue;"),
        ("CombatSystem.cs", "if (j == i || !u.Alive[j] || u.Owner[j] != owner || u.State[j] != UnitState.Attacking || u.TargetIsBuilding[j] || u.Target[j] != target) continue;"),
        ("SurroundRule.cs", "if (!u.Alive[h] || u.Owner[h] != u.Owner[i] || world.SurroundClaim[h] != key || !u.TargetIsBuilding[h]) return false;"),
        ("SurroundRule.cs", "if (u.Owner[j] != owner && !(u.TargetIsBuilding[j] && u.Target[j] == u.Target[i])) continue;"),
        // Vision: the player's own eyes (no shared vision in slice 1).
        ("FogStore.cs", "if (_units.Owner[slot] == player) return true;"),
        ("FogStore.cs", "return _buildings.Owner[slot] == player || SeesBuildingCells(player, slot);"),
        ("FogStore.cs", "if (!u.Alive[i] || u.Owner[i] != p) continue;"),
        ("FogStore.cs", "if (!bAlive[j] || bOwner[j] != p) continue;"),
        // Ai: "not the AI's own" gates whose bodies ask View.IsEnemy next (the census), and own-only answers.
        ("AiPlayer.cs", "if (u.Owner != Player)"),
        ("AiPlayer.cs", "if (b.Owner != Player)"),
        ("AiPlayer.cs", "if (!View.TryGetBuilding(p.Slot, out SeenBuilding b) || b.Owner != Player || b.Handle.Generation != p.Generation) return false;"),
        // An expansion keeps clear of every other player's hall, an ally's too (allied-as-other in docs/03's table).
        ("BuildOrderExecutor.cs", "if (b.Owner != ai.Player && ai.Data.Buildings[b.TypeId].Slot == BuildingSlot.TownHall && Near(b.Cell, x, y, reach, w)) return true;"),
        ("PlayerView.cs", "if ((uint)slot >= (uint)_units.Capacity || !_units.Alive[slot] || _units.Owner[slot] != _player)"),
        ("PlayerView.cs", "if ((uint)slot >= (uint)_units.Capacity || !_units.Alive[slot] || _units.Owner[slot] != _player) return false;"),
        ("PlayerView.cs", "private bool IsOwnUnit(int slot) => (uint)slot < (uint)_units.Capacity && _units.Alive[slot] && _units.Owner[slot] == _player;"),
        ("PlayerView.cs", "(uint)slot < (uint)_buildings.Capacity && _buildings.Alive[slot] && _buildings.Owner[slot] == _player;"),
        ("PlayerView.cs", "_buildings.Owner[k] == _player || _fog.CanSeeBuilding(_player, k) || _fog.Ghosts(_player)[k].Generation == _buildings.Generation[k];"),
        ("ScoutManager.cs", "if (!ai.View.TryGetUnit(slot, out SeenUnit u) || u.Owner != ai.Player || u.Handle.Generation != board.ScoutGeneration)"),
        // World.Defeat: a collapse destroys the player's own buildings.
        ("World.Defeat.cs", "if (!b.Alive[k] || b.Owner[k] != player) continue;"),
    };

    /// <summary>The scanned folders under <c>sim/Rts.Sim/</c> (every <c>*.cs</c>), besides the root's <c>World*.cs</c>.</summary>
    private static readonly string[] s_scannedDirs = { "Combat", "Abilities", "Vision", "Ai" };

    /// <summary>The raw owner compares in the code (comments stripped) of every <paramref name="pattern"/> file in <paramref name="dir"/>, as (file, trimmed line).</summary>
    internal static List<(string File, string Code)> RawOwnerCompares(string dir, string pattern = "*.cs")
    {
        var found = new List<(string, string)>();
        foreach (string path in Directory.GetFiles(dir, pattern).OrderBy(f => f, StringComparer.Ordinal))
            foreach (string line in File.ReadAllLines(path))
            {
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                string code = (comment >= 0 ? line[..comment] : line).Trim();
                if (code.Length == 0 || !s_rawOwnerCompare.IsMatch(code)) continue;
                // The same line asks the predicate: the compare is its fast path for the player itself.
                if (code.Contains("AreAllied(", StringComparison.Ordinal) || code.Contains("IsEnemy(", StringComparison.Ordinal)) continue;
                found.Add((Path.GetFileName(path), code));
            }
        return found;
    }

    [Fact]
    public void NoRawOwnerCompareMeaningEnemy_InTheEnemySiteFolders()
    {
        string root = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim");
        var found = new List<(string File, string Code)>(RawOwnerCompares(root, "World*.cs"));
        foreach (string d in s_scannedDirs) found.AddRange(RawOwnerCompares(Path.Combine(root, d)));
        var unexpected = found.Where(f => !s_samePlayer.Contains(f)).ToList();
        Assert.True(unexpected.Count == 0, "a raw owner compare (ask World.AreAllied / IsEnemy, or list it as same-player in docs/03 and here):\n"
            + string.Join("\n", unexpected.Select(f => $"{f.File}: {f.Code}")));
        // The allow-list is live: each listed site still exists, so a removed one is struck from it.
        Assert.All(s_samePlayer, s => Assert.Contains(s, found));
    }

    /// <summary>The scan catches what it should: a planted "u.Owner[j] == owner" enemy skip, a local compare, a "!= b.Owner[k]".</summary>
    [Fact]
    public void TheScan_CatchesAPlantedOwnerCompare()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-team-arch-" + Environment.ProcessId + "-" + Environment.CurrentManagedThreadId);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "Planted.cs"), new[]
            {
                "if (!u.Alive[j] || u.Owner[j] == owner) continue;",
                "bool friend = attackerOwner == victimOwner;",
                "if (x != b.Owner[k]) return;",
                "// u.Owner[j] == owner in a comment is not code",
                "if (world.AreAllied(owner, u.Owner[j])) continue;",
                "if (!u.Alive[j] || u.Owner[j] == owner || (world.HasAlliances && world.AreAllied(owner, u.Owner[j]))) continue;",
            });
            Assert.Equal(3, RawOwnerCompares(dir).Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
