using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhisperMyAss.Models;

namespace WhisperMyAss.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON under %APPDATA%\WhisperMyAss.
/// API keys are encrypted at rest with DPAPI (CurrentUser scope) so they are
/// never written to disk in plaintext and are unreadable by other users.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhisperMyAss");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    // Tag so we know an on-disk key value is ciphertext we produced.
    private const string EncPrefix = "dpapi:";

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            foreach (var p in settings.Profiles)
                p.ApiKey = Unprotect(p.ApiKey);

            return settings;
        }
        catch
        {
            // Corrupt/unreadable settings should never crash startup.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);

        // Encrypt keys on a shallow copy so the live in-memory object keeps plaintext.
        var toWrite = new AppSettings
        {
            ToggleHotkey = settings.ToggleHotkey,
            UseMiddleMouse = settings.UseMiddleMouse,
            RunOnStartup = settings.RunOnStartup,
            PlaySounds = settings.PlaySounds,
            Profiles = settings.Profiles.Select(p => new ApiProfile
            {
                Id = p.Id,
                Name = p.Name,
                ApiKey = Protect(p.ApiKey),
                TranscriptionUrl = p.TranscriptionUrl,
                Model = p.Model,
                Enabled = p.Enabled
            }).ToList()
        };

        var json = JsonSerializer.Serialize(toWrite, JsonOpts);
        File.WriteAllText(FilePath, json);
    }

    private static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return "";

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return EncPrefix + Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return "";

        if (!stored.StartsWith(EncPrefix, StringComparison.Ordinal))
            return stored; // Pre-existing plaintext (e.g. hand-edited) — accept as-is.

        try
        {
            var bytes = Convert.FromBase64String(stored[EncPrefix.Length..]);
            var clear = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch
        {
            return "";
        }
    }
}
