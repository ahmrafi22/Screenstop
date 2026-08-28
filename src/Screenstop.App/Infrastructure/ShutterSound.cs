using System.IO;
using System.Media;

namespace Screenstop.App.Infrastructure;

/// Camera shutter click (mac `playSounds` parity). The sound is synthesized
/// in-memory — a short noise burst with a fast decay — so the app ships no
/// audio assets.
internal static class ShutterSound
{
    private static readonly Lazy<SoundPlayer> Player = new(CreatePlayer);

    public static void Play()
    {
        try
        {
            Player.Value.Play();
        }
        catch (Exception ex)
        {
            TraceLog.Write($"shutter sound failed: {ex.Message}");
        }
    }

    private static SoundPlayer CreatePlayer()
    {
        const int sampleRate = 44100;
        const double duration = 0.09;
        int sampleCount = (int)(sampleRate * duration);
        var random = new Random(20260828);

        var samples = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double t = i / (double)sampleRate;
            double envelope = Math.Exp(-t * 60);
            double click = Math.Sin(2 * Math.PI * 2600 * t) * 0.35;
            double noise = (random.NextDouble() * 2 - 1) * 0.65;
            double value = (click + noise) * envelope * 0.7;
            samples[i] = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int dataSize = samples.Length * sizeof(short);
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataSize);
            foreach (short sample in samples)
            {
                writer.Write(sample);
            }
        }

        stream.Position = 0;
        return new SoundPlayer(stream);
    }
}
