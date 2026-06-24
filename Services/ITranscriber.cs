namespace WhisperMyAss.Services;

/// <summary>
/// A speech-to-text engine. Implementations take 16 kHz mono float samples
/// (the canonical form produced by <see cref="AudioRecorder"/>) and return the
/// transcribed text. Two engines exist: <see cref="RemoteTranscriber"/> (Groq
/// API) and, later, a local Parakeet engine.
/// </summary>
public interface ITranscriber
{
    Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken ct);
}
