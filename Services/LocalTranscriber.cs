using SherpaOnnx;

namespace WhisperMyAss.Services;

/// <summary>
/// Local engine: runs NVIDIA Parakeet-TDT v3 (int8) entirely offline on the CPU
/// via sherpa-onnx. The recognizer is created lazily on first use and kept warm
/// for subsequent dictations.
/// </summary>
public sealed class LocalTranscriber : ITranscriber, IDisposable
{
    private OfflineRecognizer? _recognizer;
    private readonly object _gate = new();

    public bool IsInstalled => LocalEngine.Installed;

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var rec = EnsureRecognizer();
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(sampleRate, samples);
            rec.Decode(stream);
            return stream.Result.Text?.Trim() ?? "";
        }, ct);
    }

    private OfflineRecognizer EnsureRecognizer()
    {
        lock (_gate)
        {
            if (_recognizer is not null) return _recognizer;

            if (!LocalEngine.Installed)
                throw new InvalidOperationException("Local model is not installed.");

            LocalEngine.EnsureNativeLoading();

            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder = LocalEngine.Encoder;
            config.ModelConfig.Transducer.Decoder = LocalEngine.Decoder;
            config.ModelConfig.Transducer.Joiner = LocalEngine.Joiner;
            config.ModelConfig.Tokens = LocalEngine.Tokens;
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.NumThreads = Math.Max(2, Environment.ProcessorCount / 2);
            config.ModelConfig.Debug = 0;
            config.DecodingMethod = "greedy_search";

            _recognizer = new OfflineRecognizer(config);
            return _recognizer;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}
