using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using WhisperMyAss.Models;

namespace WhisperMyAss.Services;

/// <summary>The server responded with an error status (auth, rate limit, bad
/// request, …) — as opposed to a connectivity failure. Distinguishing the two
/// lets the controller treat "no network" differently from "server said no".</summary>
public sealed class RemoteApiException : Exception
{
    public int StatusCode { get; }
    public RemoteApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// Remote engine: uploads audio to an OpenAI-compatible audio/transcriptions
/// endpoint (Groq Whisper by default) and returns the text. The active API
/// profile is resolved lazily at call time so settings changes take effect
/// without rebuilding the transcriber.
/// </summary>
public sealed class RemoteTranscriber : ITranscriber
{
    // ConnectTimeout caps connection setup so an offline machine fails fast
    // (~3s) instead of hanging on the OS connect timeout. The overall Timeout
    // still allows a slow transcription once connected.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3)
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private readonly Func<ApiProfile?> _profile;

    public RemoteTranscriber(Func<ApiProfile?> profileProvider) => _profile = profileProvider;

    /// <summary>Lightweight reachability check: any HTTP response (even an
    /// error status) means the endpoint is reachable. Used by the background
    /// reconnect probe.</summary>
    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        var url = _profile()?.TranscriptionUrl;
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

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
            throw new RemoteApiException((int)resp.StatusCode, $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {Trim(body)}");

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
