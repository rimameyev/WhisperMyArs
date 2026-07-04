using System.IO;
using System.Media;

namespace WhisperMyArs.Services;

/// <summary>
/// Synthesises two soft "warm pop" cues (a short rising bubble for start, a
/// falling one for stop) and plays them on the default output device. No asset
/// files, so nothing to ship or load. Low amplitude with a smooth Hann
/// envelope, so there's no hard click/attack.
/// </summary>
public static class SoundCues
{
    private const int SampleRate = 44100;

    private static readonly Lazy<MemoryStream> StartWav = new(() => BuildPop(330, 540));
    private static readonly Lazy<MemoryStream> StopWav = new(() => BuildPop(540, 330));

    public static void PlayStart() => Play(StartWav.Value);
    public static void PlayStop() => Play(StopWav.Value);

    private static void Play(MemoryStream wav)
    {
        try
        {
            wav.Position = 0;
            using var player = new SoundPlayer(wav);
            player.Play(); // async on the default device
        }
        catch { /* audio is non-critical */ }
    }

    /// <summary>A short sine that glides from <paramref name="f0"/> to
    /// <paramref name="f1"/> Hz under a Hann window — a soft, rounded blip.</summary>
    private static MemoryStream BuildPop(double f0, double f1, double seconds = 0.13, double amp = 0.24)
    {
        int n = (int)(SampleRate * seconds);
        var samples = new List<short>(n);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double frac = i / (double)n;
            double freq = f0 + (f1 - f0) * frac;
            double env = 0.5 - 0.5 * Math.Cos(2 * Math.PI * frac); // Hann
            double v = Math.Sin(2 * Math.PI * freq * t) * env * amp;
            samples.Add((short)(v * short.MaxValue));
        }
        return WriteWav(samples);
    }

    private static MemoryStream WriteWav(List<short> samples)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        int dataBytes = samples.Count * 2;
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = SampleRate * channels * bitsPerSample / 8;

        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + dataBytes);
        bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write("data".ToCharArray());
        bw.Write(dataBytes);
        foreach (var s in samples)
            bw.Write(s);

        bw.Flush();
        ms.Position = 0;
        return ms;
    }
}
