# Cuts short SFX clips out of long source WAV files (docs/04 "Import process", audio steps). PowerShell + .NET only.
#
# A clip is a byte copy of whole sample frames (block align) from the source's data chunk, written with a fresh, minimal
# header (RIFF, a plain fmt chunk, data): PCM 8/16/24/32-bit or 32-bit float, any channel count, the source's sample rate.
# No decoding to another format and no resampling: Godot's importer makes each clip mono, at most 44,100 Hz and 16-bit
# (the clip's .import file). The only sample arithmetic is optional: -Normalize scales the segment so its peak is -1 dBFS,
# and -FadeMs (default 5) ramps the first and last few milliseconds so a cut mid-waveform doesn't click.
#
# Usage:
#   powershell -File tools/audio/trim-wav.ps1                          # rebuild every clip in tools/audio/sfx-manifest.json
#   powershell -File tools/audio/trim-wav.ps1 -Manifest <file.json>    # the same from another manifest
#   (sources are read from <repo>/<sourceRoot>, or from the main checkout's when run in a git worktree, or -SourceRoot <dir>)
#   powershell -File tools/audio/trim-wav.ps1 -In <src.wav> -Out <dst.wav> -StartSec 0.12 -LengthSec 0.6 [-Normalize] [-FadeMs 5]
#   powershell -File tools/audio/trim-wav.ps1 -Info <src.wav> [-WindowMs 50]   # header + a peak envelope, for picking a cut
#
# Manifest (paths relative to the repo root; "source" relative to "sourceRoot"):
#   { "sourceRoot": "asset-sources", "clips": [ { "event": "Select", "source": "sonniss-gdc-2026/part2/...wav",
#     "destination": "game/assets/audio/sfx/ui/select_1.wav", "start": 0.05, "length": 0.35, "normalize": true, "fadeMs": 5 } ] }
# Each clip must be <= 3 s and <= 2 MB, at most 20 clips, every destination game/assets/audio/sfx/<category>/<event>_<n>.wav;
# a clip that breaks a rule fails the run (exit 1) after the others are written.
param(
    [string]$Manifest = '',
    [string]$SourceRoot = '',
    [string]$In = '',
    [string]$Out = '',
    [double]$StartSec = 0,
    [double]$LengthSec = 0,
    [switch]$Normalize,
    [double]$FadeMs = 5,
    [string]$Info = '',
    [int]$WindowMs = 50
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$MaxSeconds = 3.0
$MaxBytes = 2MB
$MaxClips = 20

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public sealed class WavFormat
{
    public int Tag;            // 1 PCM, 3 IEEE float (WAVE_FORMAT_EXTENSIBLE resolved to its sub-format)
    public int Channels;
    public int SampleRate;
    public int BitsPerSample;
    public int BlockAlign;
    public long DataOffset;
    public long DataBytes;
    public long Frames { get { return BlockAlign > 0 ? DataBytes / BlockAlign : 0; } }
    public double Seconds { get { return SampleRate > 0 ? (double)Frames / SampleRate : 0; } }
}

public static class WavTrim
{
    public static WavFormat Read(string path)
    {
        using (var f = File.OpenRead(path))
        using (var r = new BinaryReader(f))
        {
            if (Tag4(r) != "RIFF") throw new InvalidDataException(path + ": not a RIFF file");
            r.ReadUInt32();
            if (Tag4(r) != "WAVE") throw new InvalidDataException(path + ": not a WAVE file");
            WavFormat w = null;
            long dataOffset = -1, dataBytes = 0;
            while (f.Position + 8 <= f.Length)
            {
                string id = Tag4(r);
                long size = r.ReadUInt32();
                long next = f.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    w = new WavFormat();
                    w.Tag = r.ReadUInt16();
                    w.Channels = r.ReadUInt16();
                    w.SampleRate = (int)r.ReadUInt32();
                    r.ReadUInt32();
                    w.BlockAlign = r.ReadUInt16();
                    w.BitsPerSample = r.ReadUInt16();
                    if (w.Tag == 0xFFFE && size >= 26)
                    {
                        r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32();
                        w.Tag = r.ReadUInt16(); // the sub-format GUID's first two bytes
                    }
                }
                else if (id == "data")
                {
                    dataOffset = f.Position;
                    dataBytes = Math.Min(size, f.Length - f.Position);
                }
                if (dataOffset >= 0 && w != null) break;
                f.Position = next;
            }
            if (w == null) throw new InvalidDataException(path + ": no fmt chunk");
            if (dataOffset < 0) throw new InvalidDataException(path + ": no data chunk");
            if (w.Tag != 1 && w.Tag != 3) throw new InvalidDataException(path + ": format tag " + w.Tag + " is not PCM (1) or float (3)");
            if (w.Tag == 3 && w.BitsPerSample != 32) throw new InvalidDataException(path + ": float with " + w.BitsPerSample + " bits");
            if (w.Tag == 1 && w.BitsPerSample != 8 && w.BitsPerSample != 16 && w.BitsPerSample != 24 && w.BitsPerSample != 32)
                throw new InvalidDataException(path + ": PCM with " + w.BitsPerSample + " bits");
            if (w.BlockAlign != w.Channels * w.BitsPerSample / 8) throw new InvalidDataException(path + ": block align " + w.BlockAlign);
            w.DataOffset = dataOffset;
            w.DataBytes = dataBytes - dataBytes % w.BlockAlign;
            return w;
        }
    }

    // Copies [startSec, startSec + lengthSec) on frame boundaries; returns the frames written.
    public static long Trim(string inPath, string outPath, double startSec, double lengthSec, bool normalize, double fadeMs)
    {
        WavFormat w = Read(inPath);
        long start = (long)Math.Round(startSec * w.SampleRate);
        long frames = (long)Math.Round(lengthSec * w.SampleRate);
        if (start < 0 || frames <= 0 || start >= w.Frames) throw new ArgumentException(inPath + ": the cut " + startSec + " s + " + lengthSec + " s is outside its " + w.Seconds.ToString("0.000") + " s");
        frames = Math.Min(frames, w.Frames - start);
        byte[] data = new byte[frames * w.BlockAlign];
        using (var f = File.OpenRead(inPath))
        {
            f.Position = w.DataOffset + start * w.BlockAlign;
            int got = 0;
            while (got < data.Length)
            {
                int n = f.Read(data, got, data.Length - got);
                if (n <= 0) throw new EndOfStreamException(inPath);
                got += n;
            }
        }
        if (normalize || fadeMs > 0) Shape(w, data, frames, normalize, fadeMs);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using (var f = File.Create(outPath))
        using (var o = new BinaryWriter(f))
        {
            bool isFloat = w.Tag == 3;
            int fmtSize = isFloat ? 18 : 16;
            long riff = 4 + (8 + fmtSize) + (isFloat ? 12 : 0) + 8 + data.Length + (data.Length & 1);
            o.Write(Encoding.ASCII.GetBytes("RIFF")); o.Write((uint)riff); o.Write(Encoding.ASCII.GetBytes("WAVE"));
            o.Write(Encoding.ASCII.GetBytes("fmt ")); o.Write((uint)fmtSize);
            o.Write((ushort)w.Tag); o.Write((ushort)w.Channels); o.Write((uint)w.SampleRate);
            o.Write((uint)(w.SampleRate * w.BlockAlign)); o.Write((ushort)w.BlockAlign); o.Write((ushort)w.BitsPerSample);
            if (isFloat)
            {
                o.Write((ushort)0);
                o.Write(Encoding.ASCII.GetBytes("fact")); o.Write((uint)4); o.Write((uint)frames);
            }
            o.Write(Encoding.ASCII.GetBytes("data")); o.Write((uint)data.Length); o.Write(data);
            if ((data.Length & 1) != 0) o.Write((byte)0);
        }
        return frames;
    }

    // Peak per window in dBFS (all channels), for -Info.
    public static double[] Envelope(string path, int windowMs)
    {
        WavFormat w = Read(path);
        long per = Math.Max(1, (long)w.SampleRate * windowMs / 1000);
        int windows = (int)((w.Frames + per - 1) / per);
        var peaks = new double[windows];
        int bytes = w.BitsPerSample / 8;
        using (var f = File.OpenRead(path))
        {
            f.Position = w.DataOffset;
            byte[] buf = new byte[per * w.BlockAlign];
            for (int k = 0; k < windows; k++)
            {
                int want = (int)Math.Min(buf.Length, w.DataBytes - (long)k * buf.Length);
                int got = 0;
                while (got < want) { int n = f.Read(buf, got, want - got); if (n <= 0) break; got += n; }
                double peak = 0;
                for (int at = 0; at + bytes <= got; at += bytes) peak = Math.Max(peak, Math.Abs(Sample(w, buf, at)));
                peaks[k] = peak > 0 ? 20 * Math.Log10(peak) : -120;
            }
        }
        return peaks;
    }

    private static void Shape(WavFormat w, byte[] data, long frames, bool normalize, double fadeMs)
    {
        int bytes = w.BitsPerSample / 8;
        double gain = 1.0;
        if (normalize)
        {
            double peak = 0;
            for (int at = 0; at < data.Length; at += bytes) peak = Math.Max(peak, Math.Abs(Sample(w, data, at)));
            if (peak > 0) gain = Math.Pow(10, -1.0 / 20) / peak;
        }
        long fade = Math.Min(frames / 2, (long)Math.Round(fadeMs * w.SampleRate / 1000.0));
        for (long fr = 0; fr < frames; fr++)
        {
            double g = gain;
            if (fade > 0 && fr < fade) g *= (double)fr / fade;
            if (fade > 0 && frames - 1 - fr < fade) g *= (double)(frames - 1 - fr) / fade;
            if (g == 1.0) continue;
            for (int c = 0; c < w.Channels; c++)
            {
                int at = (int)(fr * w.BlockAlign) + c * bytes;
                Write(w, data, at, Sample(w, data, at) * g);
            }
        }
    }

    // One sample in [-1, 1).
    private static double Sample(WavFormat w, byte[] d, int at)
    {
        if (w.Tag == 3) return BitConverter.ToSingle(d, at);
        switch (w.BitsPerSample)
        {
            case 8: return (d[at] - 128) / 128.0;
            case 16: return (short)(d[at] | d[at + 1] << 8) / 32768.0;
            case 24: return ((d[at] | d[at + 1] << 8 | d[at + 2] << 16) << 8 >> 8) / 8388608.0;
            default: return BitConverter.ToInt32(d, at) / 2147483648.0;
        }
    }

    private static void Write(WavFormat w, byte[] d, int at, double v)
    {
        if (w.Tag == 3)
        {
            byte[] b = BitConverter.GetBytes((float)v);
            Buffer.BlockCopy(b, 0, d, at, 4);
            return;
        }
        v = Math.Max(-1.0, Math.Min(v, 1.0));
        switch (w.BitsPerSample)
        {
            case 8: d[at] = (byte)Math.Max(0, Math.Min(255, Math.Round(v * 128 + 128))); return;
            case 16:
            {
                int s = (int)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v * 32768)));
                d[at] = (byte)s; d[at + 1] = (byte)(s >> 8); return;
            }
            case 24:
            {
                int s = (int)Math.Max(-8388608, Math.Min(8388607, Math.Round(v * 8388608)));
                d[at] = (byte)s; d[at + 1] = (byte)(s >> 8); d[at + 2] = (byte)(s >> 16); return;
            }
            default:
            {
                long s = (long)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(v * 2147483648.0)));
                byte[] b = BitConverter.GetBytes((int)s);
                Buffer.BlockCopy(b, 0, d, at, 4); return;
            }
        }
    }

    private static string Tag4(BinaryReader r) { return Encoding.ASCII.GetString(r.ReadBytes(4)); }
}
'@

function Show-Info([string]$path) {
    $w = [WavTrim]::Read($path)
    $kind = if ($w.Tag -eq 3) { 'float' } else { 'PCM' }
    Write-Host ("{0}: {1} {2}-bit, {3} ch, {4} Hz, {5:0.000} s, {6} frames" -f $path, $kind, $w.BitsPerSample, $w.Channels, $w.SampleRate, $w.Seconds, $w.Frames)
    $env = [WavTrim]::Envelope($path, $WindowMs)
    for ($k = 0; $k -lt $env.Length; $k++) {
        $db = $env[$k]
        $bar = '#' * [Math]::Max(0, [int](($db + 60) / 2))
        Write-Host ("{0,8:0.000} s {1,7:0.0} dB {2}" -f ($k * $WindowMs / 1000.0), $db, $bar)
    }
}

function Cut([string]$src, [string]$dst, [double]$start, [double]$length, [bool]$norm, [double]$fade) {
    $frames = [WavTrim]::Trim($src, $dst, $start, $length, $norm, $fade)
    $w = [WavTrim]::Read($dst)
    return [pscustomobject]@{ Frames = $frames; Seconds = $w.Seconds; Bytes = (Get-Item $dst).Length; Rate = $w.SampleRate; Bits = $w.BitsPerSample; Channels = $w.Channels }
}

if ($Info) { Show-Info (Resolve-Path $Info).Path; exit 0 }

if ($In) {
    if (-not $Out -or $LengthSec -le 0) { Write-Host "FAIL: -In needs -Out and -LengthSec"; exit 1 }
    $r = Cut (Resolve-Path $In).Path ([IO.Path]::GetFullPath($Out)) $StartSec $LengthSec $Normalize.IsPresent $FadeMs
    Write-Host ("{0}: {1:0.000} s, {2} bytes, {3} Hz {4}-bit {5} ch" -f $Out, $r.Seconds, $r.Bytes, $r.Rate, $r.Bits, $r.Channels)
    exit 0
}

if (-not $Manifest) { $Manifest = Join-Path $PSScriptRoot 'sfx-manifest.json' }
$m = Get-Content -Raw -Encoding UTF8 $Manifest | ConvertFrom-Json
if (-not $SourceRoot) {
    $SourceRoot = Join-Path $root $m.sourceRoot
    # asset-sources/ is gitignored, so a git worktree (the studio's) has none: use the main checkout's.
    if (-not (Test-Path -LiteralPath $SourceRoot)) {
        $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null)
        if ($common) {
            $alt = Join-Path (Split-Path $common -Parent) $m.sourceRoot
            if (Test-Path -LiteralPath $alt) { $SourceRoot = $alt }
        }
    }
}
$sourceRoot = $SourceRoot
Write-Host "sources: $sourceRoot"
$clips = @($m.clips)
$problems = @()
if ($clips.Count -gt $MaxClips) { $problems += "$($clips.Count) clips, at most $MaxClips" }
$seen = @{}
foreach ($c in $clips) {
    $dst = [string]$c.destination
    if ($dst -notmatch '^game/assets/audio/sfx/[a-z0-9_]+/[a-z0-9_]+_[1-9]\.wav$') { $problems += "$($dst): not game/assets/audio/sfx/<category>/<event>_<n>.wav"; continue }
    if ($seen.ContainsKey($dst)) { $problems += "$($dst): listed twice"; continue }
    $seen[$dst] = $true
    $src = Join-Path $sourceRoot ([string]$c.source)
    if (-not (Test-Path -LiteralPath $src)) { $problems += "$($dst): source missing: $src"; continue }
    $norm = $false; if ($null -ne $c.normalize) { $norm = [bool]$c.normalize }
    $fade = $FadeMs; if ($null -ne $c.fadeMs) { $fade = [double]$c.fadeMs }
    try {
        $r = Cut $src (Join-Path $root $dst) ([double]$c.start) ([double]$c.length) $norm $fade
    } catch {
        $problems += "$($dst): $($_.Exception.Message)"; continue
    }
    $flag = ''
    if ($r.Seconds -gt $MaxSeconds) { $problems += "$($dst): $($r.Seconds) s, at most $MaxSeconds"; $flag = ' TOO LONG' }
    if ($r.Bytes -gt $MaxBytes) { $problems += "$($dst): $($r.Bytes) bytes, at most $MaxBytes"; $flag += ' TOO BIG' }
    Write-Host ("{0,-14} {1,-48} {2,6:0.000} s {3,9} B  {4} Hz {5}-bit {6} ch{7}" -f $c.event, $dst, $r.Seconds, $r.Bytes, $r.Rate, $r.Bits, $r.Channels, $flag)
}
Write-Host ""
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host "TRIM PASS: $($clips.Count) clips"
exit 0
