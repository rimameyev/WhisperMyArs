using System.IO;

namespace WhisperMyArs.Services;

/// <summary>
/// Builds a 16-bit PCM mono WAV (in memory) from float samples — used by the
/// remote engine, which uploads a WAV file.
/// </summary>
public static class WavEncoder
{
    public static byte[] Encode(float[] samples, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int dataBytes = samples.Length * 2;
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;

        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + dataBytes);
        bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray());
        bw.Write(16);                 // fmt chunk size
        bw.Write((short)1);           // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8)); // block align
        bw.Write(bitsPerSample);
        bw.Write("data".ToCharArray());
        bw.Write(dataBytes);

        foreach (var f in samples)
        {
            int s = (int)(f * 32767f);
            bw.Write((short)Math.Clamp(s, short.MinValue, short.MaxValue));
        }

        bw.Flush();
        return ms.ToArray();
    }
}
