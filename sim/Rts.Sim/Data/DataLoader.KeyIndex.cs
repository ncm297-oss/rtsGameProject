using System;

namespace Rts.Sim.Data;

public static partial class DataLoader
{
    /// <summary>
    /// M7-3b (BUG-1172): a load-time id table with a lookup. The table (the indexer) is the keys in id order,
    /// which since M7-3 / M7-3b is faction by faction, so it is not sorted and a binary search on it is wrong; beside it the
    /// same keys sorted ordinally with their ids, which a lookup binary-searches. Built once per table; the sorted copy is
    /// only ever searched, never iterated, so it cannot leak an order into ids or hashes. Load time only.
    /// </summary>
    private sealed class KeyIndex
    {
        private readonly string[] _table;
        private readonly string[] _sorted;
        private readonly int[] _ids;

        /// <summary>Indexes <paramref name="table"/> (keys in id order, unique: the loader drops a repeated id first).</summary>
        public KeyIndex(string[] table)
        {
            _table = table;
            _sorted = (string[])table.Clone();
            _ids = new int[table.Length];
            for (int i = 0; i < _ids.Length; i++) _ids[i] = i;
            // Unique keys, so the unstable sort still gives one result.
            Array.Sort(_sorted, _ids, StringComparer.Ordinal);
        }

        /// <summary>The number of ids.</summary>
        public int Length => _table.Length;

        /// <summary>The key of id <paramref name="id"/>.</summary>
        public string this[int id] => _table[id];

        /// <summary>The id of <paramref name="key"/>, or -1.</summary>
        public int IdOf(string key)
        {
            int i = Array.BinarySearch(_sorted, key, StringComparer.Ordinal);
            return i < 0 ? -1 : _ids[i];
        }
    }
}
