using Rts.Sim.Save;

namespace Rts.Sim.Commands;

// M6-S2: the pending commands in a save: count, then each command field by field in queue order (a command carries a bool,
// so its raw bytes would hold padding).
public sealed partial class CommandQueue
{
    /// <summary>Writes the count and every pending command, every field, in queue order (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Write(_count);
        for (int i = 0; i < _count; i++)
        {
            ref readonly Command c = ref _items[i];
            w.Write((int)c.Kind);
            w.Write(c.Player);
            w.Write(c.Tick);
            w.Write(c.Sequence);
            w.Write(c.TypeId);
            w.Write(c.Position);
            w.Write(c.Unit);
            w.Write(c.Flags);
            w.Write(c.Target);
            w.Write(c.TargetIsBuilding);
        }
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this empty queue: at most its capacity, each command well formed
    /// (<see cref="Command.IsWellFormed"/>, as <c>Enqueue</c> accepts) for a player below <paramref name="players"/>.
    /// </summary>
    internal void ReadFrom(SaveReader r, int players)
    {
        int n = r.ReadInt32In(0, _items.Length);
        for (int i = 0; i < n && !r.Failed; i++)
        {
            var c = new Command
            {
                Kind = (CommandKind)r.ReadInt32(),
                Player = r.ReadInt32(),
                Tick = r.ReadInt32(),
                Sequence = r.ReadInt32(),
                TypeId = r.ReadInt32(),
                Position = r.ReadVector2(),
                Unit = r.ReadHandle(),
                Flags = r.ReadInt32(),
                Target = r.ReadHandle(),
                TargetIsBuilding = r.ReadBool(),
            };
            if (!r.Check(c.IsWellFormed() && (uint)c.Player < (uint)players && c.Sequence >= 0)) return;
            _items[_count++] = c;
        }
    }
}
