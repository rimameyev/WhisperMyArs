using System.IO;
using System.Media;

namespace WhisperMyAss.Services;

/// <summary>
/// Synthesises two short, soft sine "blips" (a rising pair for start, a falling
/// pair for stop) and plays them on the default output device. No asset files,
/// so nothing to ship or load.
/// </summary>
public static class SoundCues
{
    private const int SampleRate = 44100;

    private static readonly Lazy<MemoryStream> StartWav =
        new(() => BuildBlip(new[] { (660.0, 0.10), (880.0, 0.12) }));

    private static readonly Lazy<MemoryStream> StopWav =
        new(() => BuildBlip(new[] { (880.0, 0.10), (660.0, 0.12) }));

    public static void PlayStart() => Play(StartWav.Value);
    public static void PlayStop() => Play(StopWav.Value);

    private static void Play(MemoryStream wav)
    {
        try
        {
            wav.Position = 0;
            // SoundPlayer copies/loads the stream synchronously, then plays async.
            using var player = new SoundPlayer(wav);
            player.Play();
        }
        catch { /* audio is non-critical */ }
    }

    private static MemoryStream BuildBlip((double freq, double seconds)[] tones)
    {
        var samples = new List<short>();
        foreach (var (freq, seconds) in tones)
            AppendTone(samples, freq, seconds);

        return WriteWav(samples);
    }

    private static void AppendTone(List<short> samples, double freq, double seconds)
    {
        int n = (int)(SampleRate * seconds);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            // Short attack/release envelope to avoid clicks.
            double env = Math.Min(1.0, Math.Min(i, n - i) / (SampleRate * 0.01));
            double v = Math.Sin(2 * Math.PI * freq * t) * env * 0.25;
            samples.Add((short)(v * short.MaxValue));
        }
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
        bw.Write(16);                 // fmt chunk size
        bw.Write((short)1);           // PCM
        bw.Write(channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8)); // block align
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
