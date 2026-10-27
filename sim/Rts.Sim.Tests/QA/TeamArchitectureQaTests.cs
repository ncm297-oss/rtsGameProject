using System.Text.RegularExpressions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S4 round 1 (BUG-1044): <see cref="TeamArchitectureTests"/>' scan matches an owner compare only when the word
/// "owner" sits right next to the operator, or as a capitalised <c>Owner[..]</c>. A local alias indexed in lower case
/// (<c>int[] owner = u.Owner; ... owner[j] != p</c>) slips past it; <c>FogStore.UpdateGhosts</c>' <c>bool enemy = ... owner[j] != p</c>
/// is one (documented allied-as-other in docs/03, but the scan never sees it). This wider scan pins the aliased compares in
/// the same folders to the two known today, so a new one (an enemy test written through an alias) fails here.
/// </summary>
public class TeamArchitectureQaTests
{
    // An indexed owner array of any case compared with == / != on either side.
    private static readonly Regex s_aliasedOwnerCompare =
        new(@"\b[A-Za-z_]*[Oo]wner[A-Za-z]*\s*\[[^\]]*\]\s*[!=]=|[!=]=\s*[A-Za-z_.]*[Oo]wner[A-Za-z]*\s*\[", RegexOptions.Compiled);

    /// <summary>Aliased compares the developer's scan misses today, each reviewed by QA: both are the player's own or allied-as-other by docs/03.</summary>
    private static readonly (string File, string Code)[] s_knownMissed =
    {
        ("FogStore.cs", "if (owner[s] == player) return s;"),            // NextSeenUnit: the player's own unit (own eyes)
        ("FogStore.cs", "bool enemy = alive[j] && owner[j] != p;"),     // UpdateGhosts: allied-as-other (an ally's building is remembered)
    };

    private static readonly string[] s_dirs = { "Combat", "Abilities", "Vision", "Ai" };

    private static List<(string File, string Code)> Scan(Regex rx, string dir, string pattern)
    {
        var found = new List<(string, string)>();
        foreach (string path in Directory.GetFiles(dir, pattern).OrderBy(f => f, StringComparer.Ordinal))
            foreach (string line in File.ReadAllLines(path))
            {
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                string code = (comment >= 0 ? line[..comment] : line).Trim();
                if (code.Length == 0 || !rx.IsMatch(code)) continue;
                if (code.Contains("AreAllied(", StringComparison.Ordinal) || code.Contains("IsEnemy(", StringComparison.Ordinal)) continue;
                found.Add((Path.GetFileName(path), code));
            }
        return found;
    }

    [Fact]
    public void AliasedOwnerCompares_TheDeveloperScanMisses_AreOnlyTheTwoReviewedOnes()
    {
        string root = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim");
        var wide = new List<(string File, string Code)>(Scan(s_aliasedOwnerCompare, root, "World*.cs"));
        var dev = new List<(string File, string Code)>(TeamArchitectureTests.RawOwnerCompares(root, "World*.cs"));
        foreach (string d in s_dirs)
        {
            wide.AddRange(Scan(s_aliasedOwnerCompare, Path.Combine(root, d), "*.cs"));
            dev.AddRange(TeamArchitectureTests.RawOwnerCompares(Path.Combine(root, d)));
        }
        var missed = wide.Where(w => !dev.Contains(w)).ToList();
        Assert.True(missed.Count == s_knownMissed.Length && s_knownMissed.All(missed.Contains),
            "an aliased owner compare the developer's scan misses (route it through World.AreAllied / IsEnemy, or review and list it):\n"
            + string.Join("\n", missed.Select(f => $"{f.File}: {f.Code}")));
    }

    /// <summary>The blind spot itself: a planted aliased enemy skip is invisible to the developer's scan and caught by the wide one.</summary>
    [Fact]
    public void APlantedAliasedEnemySkip_EscapesTheDeveloperScan_ButNotTheWideOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-team-arch-qa-" + Environment.ProcessId + "-" + Environment.CurrentManagedThreadId);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "Planted.cs"), new[] { "if (!alive[j] || owner[j] == p) continue;" });
            Assert.Empty(TeamArchitectureTests.RawOwnerCompares(dir));
            Assert.Single(Scan(s_aliasedOwnerCompare, dir, "*.cs"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
