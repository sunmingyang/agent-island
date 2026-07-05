using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Synthesizes the alarm sound palette. The macOS build leans on the
/// system's classic alert sounds (Basso, Blow, Bottle, Frog, Glass, …);
/// those files can't ship with a Windows port, so we generate short chimes
/// with the same character — one WAV per preset, written once under
/// %APPDATA%\AgentIsland\sounds and reused after that.
public static class SoundSynth
{
    private const int SampleRate = 44_100;

    public static string? EnsurePreset(string key)
    {
        try
        {
            var dir = Path.Combine(IslandPaths.AppSupportDir, "sounds");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, key + ".wav");
            if (File.Exists(path)) return path;
            var samples = key switch
            {
                "Basso" => Basso(),
                "Blow" => Blow(),
                "Bottle" => Bottle(),
                "Frog" => Frog(),
                "Glass" => Glass(),
                "Hero" => Hero(),
                "Ping" => Ping(),
                "Submarine" => Submarine(),
                _ => Glass(),
            };
            WriteWav(path, samples);
            return path;
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Palette

    /// Deep, short thud — two low partials with a fast pitch dip.
    private static float[] Basso()
    {
        return Render(0.7, t =>
        {
            var f = 98.0 * (1 - 0.15 * Math.Min(1, t * 6));
            var env = Math.Exp(-t * 6);
            return (Math.Sin(Tau(f) * t) * 0.8 + Math.Sin(Tau(f * 2.01) * t) * 0.3) * env;
        });
    }

    /// Breathy noise burst through a falling band.
    private static float[] Blow()
    {
        var random = new Random(3);
        double lowpass = 0;
        return Render(0.45, t =>
        {
            var cutoff = 0.55 - 0.45 * Math.Min(1, t * 3);
            var noise = random.NextDouble() * 2 - 1;
            lowpass += (noise - lowpass) * cutoff;
            var env = Math.Min(1, t * 30) * Math.Exp(-t * 7);
            return lowpass * env * 1.6;
        });
    }

    /// Hollow blown-bottle tone.
    private static float[] Bottle()
    {
        var random = new Random(5);
        return Render(0.5, t =>
        {
            var env = Math.Min(1, t * 40) * Math.Exp(-t * 7);
            var breath = (random.NextDouble() * 2 - 1) * 0.08 * Math.Exp(-t * 12);
            return (Math.Sin(Tau(587) * t) * 0.75
                + Math.Sin(Tau(1174) * t) * 0.12
                + breath) * env;
        });
    }

    /// Croak: low tone with fast amplitude wobble.
    private static float[] Frog()
    {
        return Render(0.4, t =>
        {
            var env = Math.Min(1, t * 50) * Math.Exp(-t * 7);
            var wobble = 0.55 + 0.45 * Math.Sin(Tau(26) * t);
            var body = Math.Sin(Tau(196) * t) + 0.4 * Math.Sin(Tau(392) * t) + 0.2 * Math.Sin(Tau(588) * t);
            return body * wobble * env * 0.6;
        });
    }

    /// Bright glass strike — inharmonic bell partials, long shimmer.
    private static float[] Glass()
    {
        return Render(1.2, t =>
        {
            var strike = Math.Exp(-t * 3.2);
            return (Math.Sin(Tau(1568) * t) * 0.55
                + Math.Sin(Tau(2637) * t) * 0.30 * Math.Exp(-t * 4.5)
                + Math.Sin(Tau(3951) * t) * 0.18 * Math.Exp(-t * 6)) * strike;
        });
    }

    /// Rising major triad flourish.
    private static float[] Hero()
    {
        return Render(0.9, t =>
        {
            double Tone(double f, double start) =>
                t < start ? 0 : Math.Sin(Tau(f) * (t - start)) * Math.Exp(-(t - start) * 5);
            return (Tone(523.25, 0) * 0.5 + Tone(659.25, 0.12) * 0.5 + Tone(783.99, 0.24) * 0.6) * 0.8;
        });
    }

    /// Single clean ping.
    private static float[] Ping()
    {
        return Render(0.8, t =>
        {
            var env = Math.Exp(-t * 5);
            return (Math.Sin(Tau(1318.5) * t) * 0.7 + Math.Sin(Tau(2637) * t) * 0.2) * env;
        });
    }

    /// Slow underwater warble.
    private static float[] Submarine()
    {
        return Render(1.0, t =>
        {
            var env = Math.Min(1, t * 12) * Math.Exp(-t * 3);
            var f = 300 + 18 * Math.Sin(Tau(4.5) * t);
            return Math.Sin(Tau(f) * t) * env * 0.8;
        });
    }

    // MARK: - Engine

    private static double Tau(double frequency) => 2 * Math.PI * frequency;

    private static float[] Render(double seconds, Func<double, double> voice)
    {
        var count = (int)(seconds * SampleRate);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)SampleRate;
            samples[i] = (float)Math.Clamp(voice(t), -1, 1);
        }
        // Gentle fade-out on the tail to avoid clicks.
        var fade = Math.Min(count, SampleRate / 50);
        for (var i = 0; i < fade; i++)
        {
            samples[count - 1 - i] *= i / (float)fade;
        }
        return samples;
    }

    private static void WriteWav(string path, float[] samples)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);            // PCM
        writer.Write((short)1);            // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);      // byte rate
        writer.Write((short)2);            // block align
        writer.Write((short)16);           // bits
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write((short)(sample * short.MaxValue * 0.9));
        }
    }
}
