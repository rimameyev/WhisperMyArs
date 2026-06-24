using System.IO;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace WhisperMyAss.Services;

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

    /// <summary>Stops capture and returns the recording as a 16 kHz mono WAV,
    /// or null if nothing usable was captured.</summary>
    public byte[]? Stop()
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

            return ResampleToWhisperWav(rawBytes, _captureFormat);
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

    private static byte[] ResampleToWhisperWav(byte[] rawBytes, WaveFormat sourceFormat)
    {
        var target = new WaveFormat(16000, 16, 1);
        using var source = new RawSourceWaveStream(new MemoryStream(rawBytes), sourceFormat);
        using var resampler = new MediaFoundationResampler(source, target) { ResamplerQuality = 60 };

        using var outStream = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(outStream, resampler);
        return outStream.ToArray();
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
        // Light shaping so quiet speech still moves the bars.
        return (float)Math.Clamp(rms * 3.5, 0, 1);
    }

    public void Dispose() => Cancel();
}
