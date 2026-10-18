using System.Reflection;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S2): a reflection walk over two simulations' whole object graphs (every instance field, private ones too, into
/// every reachable object of the sim's types) that lists the field paths whose values differ. A save that leaves a field
/// out shows up here even when nothing in the hash or the direct compares reads it.
/// </summary>
internal static class SaveDeepDiff
{
    /// <summary>The differing paths (an array reports its first differing index and how many differ), sorted.</summary>
    internal static List<string> Diff(object a, object b, int maxDepth = 12)
    {
        var diffs = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(a, b, "sim", diffs, seen, 0, maxDepth);
        diffs.Sort(StringComparer.Ordinal);
        return diffs;
    }

    private static void Walk(object? a, object? b, string path, List<string> diffs, HashSet<object> seen, int depth, int maxDepth)
    {
        if (a is null || b is null)
        {
            if (!(a is null && b is null)) diffs.Add($"{path}: null vs non-null");
            return;
        }
        if (ReferenceEquals(a, b)) return;
        Type t = a.GetType();
        if (t != b.GetType())
        {
            diffs.Add($"{path}: type {t.Name} vs {b.GetType().Name}");
            return;
        }
        if (!t.IsValueType && !seen.Add(a)) return;
        if (depth > maxDepth) return;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
        {
            if (!SameScalar(a, b)) diffs.Add($"{path}: {a} vs {b}");
            return;
        }
        if (typeof(Delegate).IsAssignableFrom(t) || typeof(Stream).IsAssignableFrom(t) || t == typeof(Data.GameData)) return;
        if (a is Array xa)
        {
            var xb = (Array)b;
            if (xa.Length != xb.Length)
            {
                diffs.Add($"{path}: length {xa.Length} vs {xb.Length}");
                return;
            }
            Type? e = t.GetElementType();
            if (e != null && (e.IsPrimitive || e.IsEnum))
            {
                Array pa = e.IsEnum ? ToUnderlying(xa) : xa, pb = e.IsEnum ? ToUnderlying(xb) : xb;
                int len = Buffer.ByteLength(pa);
                var ba = new byte[len];
                var bb = new byte[len];
                Buffer.BlockCopy(pa, 0, ba, 0, len);
                Buffer.BlockCopy(pb, 0, bb, 0, len);
                if (!ba.AsSpan().SequenceEqual(bb))
                {
                    int size = len / Math.Max(1, xa.Length), first = -1, count = 0;
                    for (int i = 0; i < xa.Length; i++)
                        if (!ba.AsSpan(i * size, size).SequenceEqual(bb.AsSpan(i * size, size)))
                        {
                            if (first < 0) first = i;
                            count++;
                        }
                    diffs.Add($"{path}[]: {count} of {xa.Length} differ, first [{first}] {xa.GetValue(first)} vs {xb.GetValue(first)}");
                }
                return;
            }
            int firstDiff = -1, diffCount = 0;
            string? firstPath = null;
            for (int i = 0; i < xa.Length; i++)
            {
                var sub = new List<string>();
                Walk(xa.GetValue(i), xb.GetValue(i), $"{path}[{i}]", sub, seen, depth + 1, maxDepth);
                if (sub.Count > 0)
                {
                    if (firstDiff < 0)
                    {
                        firstDiff = i;
                        firstPath = sub[0];
                    }
                    diffCount++;
                }
            }
            if (diffCount > 0) diffs.Add($"{path}[]: {diffCount} of {xa.Length} differ, first {firstPath}");
            return;
        }
        for (Type? cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
        {
            foreach (FieldInfo f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Walk(f.GetValue(a), f.GetValue(b), $"{path}.{Clean(f.Name)}", diffs, seen, depth + 1, maxDepth);
        }
    }

    private static Array ToUnderlying(Array enums)
    {
        Type u = Enum.GetUnderlyingType(enums.GetType().GetElementType()!);
        var copy = Array.CreateInstance(u, enums.Length);
        for (int i = 0; i < enums.Length; i++) copy.SetValue(Convert.ChangeType(enums.GetValue(i), u), i);
        return copy;
    }

    private static bool SameScalar(object a, object b) => a switch
    {
        float x => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits((float)b),
        double x => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits((double)b),
        _ => a.Equals(b),
    };

    // "<Name>k__BackingField" -> "Name".
    private static string Clean(string name) =>
        name.StartsWith('<') && name.Contains(">k__BackingField", StringComparison.Ordinal) ? name[1..name.IndexOf('>')] : name;
}
