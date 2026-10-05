using System.Reflection;

namespace Rts.Sim.Tests;

/// <summary>Guards BUG-0024's fix: every measuring test runs in <see cref="SerialCollection"/>, never next to the parallel batch.</summary>
public class SerialCollectionTests
{
    private static bool InSerialCollection(Type t) =>
        t.GetCustomAttributesData().Any(a => a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Count == 1
            && (string?)a.ConstructorArguments[0].Value == SerialCollection.Name);

    [Fact]
    public void EveryPerfTest_IsInTheSerialCollection()
    {
        var offenders = new List<string>();
        int perf = 0;
        foreach (Type t in typeof(SerialCollectionTests).Assembly.GetTypes())
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                bool isPerf = m.GetCustomAttributesData().Any(a => a.AttributeType == typeof(TraitAttribute)
                    && (string?)a.ConstructorArguments[0].Value == "Category"
                    && (string?)a.ConstructorArguments[1].Value == "Perf");
                if (!isPerf) continue;
                perf++;
                if (!InSerialCollection(t)) offenders.Add($"{t.FullName}.{m.Name}");
            }
        }
        Assert.True(perf > 20, $"found only {perf} Perf tests; is the trait scan broken?");
        Assert.True(offenders.Count == 0, "Perf tests outside the serial collection:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryFileThatMeasuresAllocation_PutsItsMeasuringClassInTheSerialCollection()
    {
        // Source-level check: a file that reads the per-thread allocation counter, directly or via
        // AllocationProbe, must declare the serial collection somewhere (whole class or nested Serial).
        string dir = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests");
        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            string name = Path.GetFileName(file);
            if (name is "AllocationProbe.cs" or "SerialCollection.cs" or "SerialCollectionTests.cs") continue;
            string src = File.ReadAllText(file);
            bool measures = src.Contains("AllocationProbe.") || src.Contains("GetAllocatedBytesForCurrentThread");
            if (measures && !src.Contains("[Collection(SerialCollection.Name)]")) offenders.Add(name);
        }
        Assert.True(offenders.Count == 0, "allocation measured outside the serial collection: " + string.Join(", ", offenders));
    }
}
