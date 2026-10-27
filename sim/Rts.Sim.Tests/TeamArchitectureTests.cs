using System.Text.RegularExpressions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S4: every "is this an enemy" site in <c>sim/Rts.Sim/Combat/</c> asks the one alliance predicate
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
    };

    /// <summary>The raw owner compares in the code (comments stripped) of every <c>Combat/</c> file, as (file, trimmed line).</summary>
    internal static List<(string File, string Code)> RawOwnerCompares(string dir)
    {
        var found = new List<(string, string)>();
        foreach (string path in Directory.GetFiles(dir, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
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
    public void NoRawOwnerCompareMeaningEnemy_InCombat()
    {
        string dir = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim", "Combat");
        List<(string File, string Code)> found = RawOwnerCompares(dir);
        var unexpected = found.Where(f => !s_samePlayer.Contains(f)).ToList();
        Assert.True(unexpected.Count == 0, "a raw owner compare in Combat/ (ask World.AreAllied / IsEnemy, or list it as same-player in docs/03 and here):\n"
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
