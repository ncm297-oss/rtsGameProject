using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Rts.Sim.Entities;

namespace Rts.Sim.Save;

/// <summary>
/// Writes a save's bytes (M6-S2): a <see cref="BinaryWriter"/> (little-endian) over a growing buffer, then the total length
/// patched into the header and the FNV-1a 64 checksum appended (<see cref="Finish"/>). The stores' <c>WriteTo</c> methods
/// call it; a save is not a tick, so it may allocate.
/// </summary>
internal sealed class SaveWriter
{
    private readonly MemoryStream _buffer = new();
    private readonly BinaryWriter _w;

    public SaveWriter()
    {
        // Whole arrays go out as their raw bytes, which are little-endian only on a little-endian machine (every Windows
        // desktop the game ships for); a big-endian one would need per-element writes.
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("The save format is written on little-endian machines only.");
        _w = new BinaryWriter(_buffer, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>Bytes written so far.</summary>
    public long Length => _buffer.Length;

    public void Write(byte value) => _w.Write(value);

    public void Write(bool value) => _w.Write(value ? (byte)1 : (byte)0);

    public void Write(int value) => _w.Write(value);

    public void Write(long value) => _w.Write(value);

    public void Write(ulong value) => _w.Write(value);

    /// <summary>A float by its exact bit pattern (NaN payloads and -0 kept).</summary>
    public void Write(float value) => _w.Write(BitConverter.SingleToInt32Bits(value));

    public void Write(Vector2 value)
    {
        Write(value.X);
        Write(value.Y);
    }

    public void Write(EntityHandle value)
    {
        _w.Write(value.Index);
        _w.Write(value.Generation);
    }

    /// <summary>A string as its UTF-8 byte count (int32) and bytes.</summary>
    public void WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        _w.Write(bytes.Length);
        _w.Write(bytes);
    }

    /// <summary>A section tag (four ASCII letters, <see cref="SaveFormat"/>).</summary>
    public void Tag(uint tag) => _w.Write(tag);

    /// <summary>
    /// A whole array of a type without references: its length (int32), then its elements' raw bytes. Only for element
    /// types without padding (primitives, enums, <see cref="Vector2"/>, <see cref="EntityHandle"/> and other all-int
    /// structs), so the bytes are the values and nothing else (a struct with a bool goes field by field).
    /// </summary>
    public void WriteArray<T>(T[] values) where T : unmanaged => WriteSpan<T>(values);

    /// <summary>As <see cref="WriteArray{T}"/>, for a span.</summary>
    public void WriteSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        _w.Write(values.Length);
        _w.Write(MemoryMarshal.AsBytes(values));
    }

    /// <summary>The bytes written so far, as they are (no length patched, no checksum): a section on its own, for tests.</summary>
    public byte[] Raw()
    {
        _w.Flush();
        return _buffer.ToArray();
    }

    /// <summary>The finished file: the total length patched in at <see cref="SaveFormat.LengthOffset"/>, then the checksum of every byte before it.</summary>
    public byte[] Finish()
    {
        _w.Flush();
        long total = _buffer.Length + SaveFormat.ChecksumLength;
        _buffer.Position = SaveFormat.LengthOffset;
        _w.Write(total);
        _w.Flush();
        _buffer.Position = _buffer.Length;
        ulong sum = SaveFormat.Checksum(_buffer.GetBuffer().AsSpan(0, (int)_buffer.Length));
        _w.Write(sum);
        _w.Flush();
        return _buffer.ToArray();
    }
}
