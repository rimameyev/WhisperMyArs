using System.IO;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace WhisperMyArs.Services;

/// <summary>
/// Captures the default system microphone via WASAPI and produces a compact
/// 16 kHz mono 16-bit WAV in memory — exactly what Whisper wants, so uploads
/// stay tiny and fast. Raises <see cref="LevelChanged"/> (0..1 RMS) while
/// recording so the overlay can animate.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    /// <summary>Normalised input level (0..1), raised on the capture thread.</summary>
    public event Action<float>? LevelChanged;

    private WasapiCapture? _capture;
    private MemoryStream? _raw;
    private WaveFormat? _captureFormat;
    private readonly object _gate = new();

    static AudioRecorder()
    {
        // Needed for MediaFoundationResampler used at Stop().
        MediaFoundationApi.Startup();
    }

    public bool IsRecording { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRecording) return;

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            _capture = new WasapiCapture(device);
            _captureFormat = _capture.WaveFormat;
            _raw = new MemoryStream();

            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            IsRecording = true;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _raw?.Write(e.Buffer, 0, e.BytesRecorded);
        LevelChanged?.Invoke(ComputeRms(e.Buffer, e.BytesRecorded, _captureFormat!));
    }

    /// <summary>Sample rate of the audio returned by <see cref="Stop"/>.</summary>
    public const int OutputSampleRate = 16000;

    /// <summary>Stops capture and returns the recording as 16 kHz mono float
    /// samples, or null if nothing usable was captured.</summary>
    public float[]? Stop()
    {
        lock (_gate)
        {
            if (!IsRecording || _capture is null || _raw is null)
                return null;

            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
            IsRecording = false;

            var rawBytes = _raw.ToArray();
            _raw.Dispose();
            _raw = null;

            if (rawBytes.Length == 0 || _captureFormat is null)
                return null;

            return ResampleToMono16k(rawBytes, _captureFormat);
        }
    }

    /// <summary>Discards an in-progress recording without producing output.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!IsRecording) return;
            _capture!.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
            _raw?.Dispose();
            _raw = null;
            IsRecording = false;
        }
    }

    private static float[] ResampleToMono16k(byte[] rawBytes, WaveFormat sourceFormat)
    {
        var target = new WaveFormat(OutputSampleRate, 16, 1);
        using var source = new RawSourceWaveStream(new MemoryStream(rawBytes), sourceFormat);
        using var resampler = new MediaFoundationResampler(source, target) { ResamplerQuality = 60 };

        // Read the resampled 16-bit PCM, then convert to normalised float[-1,1].
        using var pcmStream = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = resampler.Read(buf, 0, buf.Length)) > 0)
            pcmStream.Write(buf, 0, n);

        var pcm = pcmStream.GetBuffer();
        int count = (int)pcmStream.Length / 2;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }
        return samples;
    }

    private static float ComputeRms(byte[] buffer, int bytes, WaveFormat fmt)
    {
        // WASAPI capture is typically 32-bit IEEE float; handle 16-bit PCM too.
        double sum = 0;
        int count = 0;

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= bytes; i += 4)
            {
                float s = BitConverter.ToSingle(buffer, i);
                sum += s * s;
                count++;
            }
        }
        else if (fmt.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= bytes; i += 2)
            {
                short s = BitConverter.ToInt16(buffer, i);
                float f = s / 32768f;
                sum += f * f;
                count++;
            }
        }
        else
        {
            return 0;
        }

        if (count == 0) return 0;
        double rms = Math.Sqrt(sum / count);
        // Perceptual curve: a sqrt mapping lifts quiet speech a lot more than a
        // linear gain, so the bars stay lively at normal talking volume.
        return (float)Math.Clamp(Math.Sqrt(rms) * 1.7, 0, 1);
    }

    public void Dispose() => Cancel();
}
