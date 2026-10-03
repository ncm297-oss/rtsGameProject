using System;

namespace Rts.Sim.Commands;

/// <summary>Fixed-capacity buffer of pending commands, sorted in place and drained one tick at a time.</summary>
public sealed class CommandQueue
{
    private readonly Command[] _items;
    private int _count;

    /// <summary>Creates a queue that holds at most <paramref name="capacity"/> pending commands.</summary>
    public CommandQueue(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new Command[capacity];
    }

    /// <summary>Number of pending commands.</summary>
    public int Count => _count;

    /// <summary>Pending command at <paramref name="index"/> (insertion order until sorted).</summary>
    public ref readonly Command this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _items[index];
        }
    }

    /// <summary>Appends a command; throws when full rather than growing or dropping it.</summary>
    public void Add(in Command command)
    {
        if (_count == _items.Length)
            throw new InvalidOperationException($"CommandQueue is full (capacity {_items.Length}).");
        _items[_count++] = command;
    }

    /// <summary>Sorts pending commands by (tick, player, sequence) with an in-place insertion sort.</summary>
    /// <remarks>Insertion sort: no allocation, stable, and fast on the small, mostly sorted batches we get.</remarks>
    public void Sort()
    {
        for (int i = 1; i < _count; i++)
        {
            Command item = _items[i];
            int j = i - 1;
            while (j >= 0 && Compare(in _items[j], in item) > 0)
            {
                _items[j + 1] = _items[j];
                j--;
            }
            _items[j + 1] = item;
        }
    }

    /// <summary>After <see cref="Sort"/>: how many leading commands are due on or before <paramref name="tick"/>.</summary>
    public int CountDue(int tick)
    {
        int n = 0;
        while (n < _count && _items[n].Tick <= tick)
            n++;
        return n;
    }

    /// <summary>Removes the first <paramref name="n"/> commands, keeping the rest in order.</summary>
    public void RemoveFront(int n)
    {
        if ((uint)n > (uint)_count) throw new ArgumentOutOfRangeException(nameof(n));
        Array.Copy(_items, n, _items, 0, _count - n);
        Array.Clear(_items, _count - n, n);
        _count -= n;
    }

    private static int Compare(in Command a, in Command b)
    {
        if (a.Tick != b.Tick) return a.Tick < b.Tick ? -1 : 1;
        if (a.Player != b.Player) return a.Player < b.Player ? -1 : 1;
        if (a.Sequence != b.Sequence) return a.Sequence < b.Sequence ? -1 : 1;
        return 0;
    }
}
