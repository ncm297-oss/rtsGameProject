using System;
using Godot;

namespace Rts.Game;

/// <summary>
/// A pool of <see cref="GroundDisc"/> nodes keyed by a small int (M5-V1, BUG-0520): a caster's unit slot for the cast-point
/// rings, a flash slot for the resolve flashes, a cue slot for the lost-cast cues. A key keeps its node from frame to frame
/// while it is taken, so a disc whose centre doesn't move is never rebuilt; a key not taken in a frame gives its node back
/// (hidden) for any other key. The pool starts with <c>initial</c> nodes and doubles when a frame needs more (a new high
/// mark: the only time it allocates), up to one node per key. View state only.
/// </summary>
/// <remarks>Each frame: <see cref="Begin"/>, then <see cref="Take"/> per drawn key (place and colour the node it returns, then show it), then <see cref="End"/>.</remarks>
public sealed class GroundDiscPool
{
    private readonly Node _parent;
    private readonly string _name;
    private readonly int[] _keyNode;
    private GroundDisc[] _nodes = Array.Empty<GroundDisc>();
    private int[] _nodeKey = Array.Empty<int>(), _free = Array.Empty<int>();
    private bool[] _used = Array.Empty<bool>();
    private int _count, _freeCount;

    /// <summary>A pool under <paramref name="parent"/> for keys 0 to <paramref name="keys"/> - 1, made with <paramref name="initial"/> hidden nodes named <paramref name="name"/> + index.</summary>
    public GroundDiscPool(Node parent, string name, int keys, int initial)
    {
        if (keys < 1) throw new ArgumentOutOfRangeException(nameof(keys));
        _parent = parent;
        _name = name;
        _keyNode = new int[keys];
        Array.Fill(_keyNode, -1);
        Grow(Math.Clamp(initial, 1, keys));
    }

    /// <summary>Nodes made so far (the pool's high mark).</summary>
    public int Nodes => _count;

    /// <summary>Times the pool grew past its starting nodes.</summary>
    public int Grown { get; private set; }

    /// <summary>Keys holding a node now.</summary>
    public int InUse => _count - _freeCount;

    /// <summary>Node <paramref name="n"/> (0 to <see cref="Nodes"/> - 1).</summary>
    public GroundDisc Node(int n) => _nodes[n];

    /// <summary>The node <paramref name="key"/> holds, or null.</summary>
    public GroundDisc? NodeOf(int key) => (uint)key < (uint)_keyNode.Length && _keyNode[key] >= 0 ? _nodes[_keyNode[key]] : null;

    /// <summary>Starts a frame: no key is taken yet.</summary>
    public void Begin() => Array.Clear(_used, 0, _count);

    /// <summary>The node for <paramref name="key"/> this frame: the one it held last frame, else a free one (the pool grows if none is free).</summary>
    public GroundDisc Take(int key)
    {
        int n = _keyNode[key];
        if (n < 0)
        {
            if (_freeCount == 0) Grow(Math.Min(_keyNode.Length, _count * 2));
            n = _free[--_freeCount];
            _keyNode[key] = n;
            _nodeKey[n] = key;
        }
        _used[n] = true;
        return _nodes[n];
    }

    /// <summary>Ends a frame: every node no key took is hidden and freed.</summary>
    public void End()
    {
        for (int n = 0; n < _count; n++)
        {
            if (_used[n]) continue;
            if (_nodes[n].Visible) _nodes[n].Visible = false;
            int key = _nodeKey[n];
            if (key < 0) continue;
            _keyNode[key] = -1;
            _nodeKey[n] = -1;
            _free[_freeCount++] = n;
        }
    }

    /// <summary>Frees every node (a re-bind makes a new pool).</summary>
    public void Free()
    {
        for (int n = 0; n < _count; n++) _nodes[n].QueueFree();
        _count = 0;
        _freeCount = 0;
        Array.Fill(_keyNode, -1);
    }

    // Makes nodes up to `to` (more than now), all free.
    private void Grow(int to)
    {
        if (to <= _count) throw new InvalidOperationException($"{_name} pool is full ({_count} nodes for {_keyNode.Length} keys)");
        if (_count > 0) Grown++;
        Array.Resize(ref _nodes, to);
        Array.Resize(ref _nodeKey, to);
        Array.Resize(ref _used, to);
        Array.Resize(ref _free, to);
        // Free nodes are taken from the top of the stack: push the new ones highest first, so they are used in index order.
        for (int n = to - 1; n >= _count; n--)
        {
            _nodes[n] = new GroundDisc { Name = $"{_name}{n}" };
            _parent.AddChild(_nodes[n]);
            _nodeKey[n] = -1;
            _free[_freeCount++] = n;
        }
        _count = to;
    }
}
