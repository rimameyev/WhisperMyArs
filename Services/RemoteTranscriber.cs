using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using WhisperMyAss.Models;

namespace WhisperMyAss.Services;

/// <summary>
/// Remote engine: uploads audio to an OpenAI-compatible audio/transcriptions
/// endpoint (Groq Whisper by default) and returns the text. The active API
/// profile is resolved lazily at call time so settings changes take effect
/// without rebuilding the transcriber.
/// </summary>
public sealed class RemoteTranscriber : ITranscriber
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private readonly Func<ApiProfile?> _profile;

    public RemoteTranscriber(Func<ApiProfile?> profileProvider) => _profile = profileProvider;

    public async Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken ct)
    {
        var profile = _profile()
            ?? throw new InvalidOperationException("No active API profile.");
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
            throw new InvalidOperationException("No API key set for the active profile.");

        var wav = WavEncoder.Encode(samples, sampleRate);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(wav);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "audio.wav");
        form.Add(new StringContent(profile.Model), "model");
        form.Add(new StringContent("text"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post, profile.TranscriptionUrl)
        {
            Content = form
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {Trim(body)}");

        return ExtractText(body);
    }

    private static string ExtractText(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("text", out var t))
                    return t.GetString()?.Trim() ?? "";
            }
            catch { /* fall through */ }
        }
        return trimmed;
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
