using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Rts.Sim.Entities;

namespace Rts.Sim.Save;

/// <summary>
/// Reads a save's bytes (M6-S2) without ever throwing: a read past the end, an array of the wrong length or a value a
/// caller rejects sets <see cref="Failed"/>, and every later read returns a default value, so a store's <c>ReadFrom</c> can
/// read straight through and the loader checks <see cref="Failed"/> once per section. Little-endian, as
/// <see cref="SaveWriter"/> writes.
/// </summary>
internal sealed class SaveReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _pos;

    /// <summary>A reader over <paramref name="data"/>[<paramref name="start"/>, <paramref name="end"/>).</summary>
    public SaveReader(byte[] data, int start, int end)
    {
        _data = data;
        _pos = start;
        _end = end;
    }

    /// <summary>True once any read ran past the end or any check failed; nothing read after it means anything.</summary>
    public bool Failed { get; private set; }

    /// <summary>Bytes left before the end.</summary>
    public int Remaining => _end - _pos;

    /// <summary>Marks the read as failed (a value out of range).</summary>
    public void Fail()
    {
        Failed = true;
        _pos = _end;
    }

    /// <summary>Fails unless <paramref name="ok"/>; returns it.</summary>
    public bool Check(bool ok)
    {
        if (!ok) Fail();
        return ok && !Failed;
    }

    private bool Take(int bytes, out int at)
    {
        at = _pos;
        if (Failed || bytes < 0 || _end - _pos < bytes)
        {
            Fail();
            return false;
        }
        _pos += bytes;
        return true;
    }

    public byte ReadByte() => Take(1, out int at) ? _data[at] : (byte)0;

    /// <summary>A bool, which must be byte 0 or 1.</summary>
    public bool ReadBool()
    {
        byte b = ReadByte();
        if (b > 1) Fail();
        return b == 1;
    }

    public int ReadInt32() => Take(4, out int at) ? BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at, 4)) : 0;

    public uint ReadUInt32() => Take(4, out int at) ? BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at, 4)) : 0u;

    public long ReadInt64() => Take(8, out int at) ? BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(at, 8)) : 0L;

    public ulong ReadUInt64() => Take(8, out int at) ? BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(at, 8)) : 0UL;

    public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt32());

    public Vector2 ReadVector2()
    {
        float x = ReadFloat();
        return new Vector2(x, ReadFloat());
    }

    public EntityHandle ReadHandle()
    {
        int index = ReadInt32();
        return new EntityHandle(index, ReadInt32());
    }

    /// <summary>An int that must lie in [<paramref name="min"/>, <paramref name="max"/>]; else fails and returns <paramref name="min"/>.</summary>
    public int ReadInt32In(int min, int max)
    {
        int v = ReadInt32();
        if (v < min || v > max)
        {
            Fail();
            return min;
        }
        return v;
    }

    /// <summary>
    /// A string of at most <see cref="SaveFormat.MaxString"/> printable ASCII bytes (the sim version, snake_case data ids);
    /// null (and failed) past that length or on any other byte.
    /// </summary>
    public string? ReadString()
    {
        int n = ReadInt32();
        if (n < 0 || n > SaveFormat.MaxString || !Take(n, out int at))
        {
            Fail();
            return null;
        }
        for (int i = at; i < at + n; i++)
            if (_data[i] < 0x20 || _data[i] > 0x7E)
            {
                Fail();
                return null;
            }
        return Encoding.ASCII.GetString(_data, at, n);
    }

    /// <summary>Fails unless the next word is section tag <paramref name="tag"/>.</summary>
    public bool Expect(uint tag) => Check(ReadUInt32() == tag);

    /// <summary>The length prefix of an array: fails unless it is exactly <paramref name="expected"/>.</summary>
    public bool ExpectLength(int expected) => Check(ReadInt32() == expected);

    /// <summary>
    /// Fills <paramref name="dest"/> from a <see cref="SaveWriter.WriteArray{T}"/> record, which must hold exactly
    /// <paramref name="dest"/>'s length (the arrays are sized from the header's capacities before they are read).
    /// </summary>
    public void ReadArray<T>(T[] dest) where T : unmanaged => ReadSpan<T>(dest);

    /// <summary>As <see cref="ReadArray{T}"/>, into a span.</summary>
    public void ReadSpan<T>(Span<T> dest) where T : unmanaged
    {
        if (!ExpectLength(dest.Length)) return;
        Span<byte> bytes = MemoryMarshal.AsBytes(dest);
        if (!Take(bytes.Length, out int at)) return;
        _data.AsSpan(at, bytes.Length).CopyTo(bytes);
    }

    /// <summary>A <see cref="SaveWriter.WriteArray{T}"/> record of at most <paramref name="maxLength"/> elements, into a new array (empty, and failed, past it).</summary>
    public T[] ReadNewArray<T>(int maxLength) where T : unmanaged
    {
        int n = ReadInt32();
        if (n < 0 || n > maxLength)
        {
            Fail();
            return Array.Empty<T>();
        }
        var values = new T[n];
        Span<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
        if (!Take(bytes.Length, out int at)) return Array.Empty<T>();
        _data.AsSpan(at, bytes.Length).CopyTo(bytes);
        return values;
    }

    /// <summary>A bool array (each byte 0 or 1), as <see cref="ReadArray{T}"/>.</summary>
    public void ReadBools(bool[] dest)
    {
        if (!ExpectLength(dest.Length)) return;
        if (!Take(dest.Length, out int at)) return;
        for (int i = 0; i < dest.Length; i++)
        {
            byte b = _data[at + i];
            if (b > 1)
            {
                Fail();
                return;
            }
            dest[i] = b == 1;
        }
    }

    /// <summary>A byte-backed enum array whose every value must be at most <paramref name="max"/>, as <see cref="ReadArray{T}"/>.</summary>
    public void ReadByteEnums<T>(T[] dest, byte max) where T : unmanaged
    {
        ReadArray(dest);
        if (Failed) return;
        foreach (byte b in MemoryMarshal.AsBytes(dest.AsSpan()))
            if (b > max)
            {
                Fail();
                return;
            }
    }

    /// <summary>An int-backed enum array whose every value must lie in [0, <paramref name="max"/>], as <see cref="ReadArray{T}"/>.</summary>
    public void ReadIntEnums<T>(T[] dest, int max) where T : unmanaged
    {
        ReadArray(dest);
        if (Failed) return;
        foreach (int v in MemoryMarshal.Cast<T, int>(dest.AsSpan()))
            if (v < 0 || v > max)
            {
                Fail();
                return;
            }
    }
}
