using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using WhisperMyAss.Models;

namespace WhisperMyAss.Services;

/// <summary>
/// Thin client over an OpenAI-compatible audio/transcriptions endpoint
/// (Groq Whisper by default). One shared <see cref="HttpClient"/> for the
/// process lifetime.
/// </summary>
public sealed class GroqClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    /// <summary>
    /// Uploads a WAV and returns the transcribed text. Honours <paramref name="ct"/>
    /// so an in-flight request can be cancelled (Esc).
    /// </summary>
    public async Task<string> TranscribeAsync(ApiProfile profile, byte[] wav, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
            throw new InvalidOperationException("No API key set for the active profile.");

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

        // response_format=text returns the raw transcript; but if a server still
        // hands back JSON, pull the "text" field out of it.
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
