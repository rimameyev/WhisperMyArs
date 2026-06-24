using System.Text.Json.Serialization;

namespace WhisperMyAss.Models;

/// <summary>
/// A single transcription endpoint the user can configure. Keys are stored
/// DPAPI-encrypted on disk (see <see cref="Services.SettingsStore"/>), so the
/// in-memory <see cref="ApiKey"/> is plaintext only at runtime.
/// </summary>
public sealed class ApiProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Groq";
    public string ApiKey { get; set; } = "";
    public string TranscriptionUrl { get; set; } = "https://api.groq.com/openai/v1/audio/transcriptions";
    public string Model { get; set; } = "whisper-large-v3-turbo";
    public bool Enabled { get; set; }
}

/// <summary>
/// A captured hotkey combination (modifiers + a single main key).
/// </summary>
public sealed class HotkeyCombo
{
    /// <summary>Win32 virtual-key code of the main key (0 = unset).</summary>
    public int VirtualKey { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsSet => VirtualKey != 0;
}

/// <summary>Which transcription engine to use.</summary>
public enum EngineMode
{
    /// <summary>Remote API (Groq Whisper).</summary>
    Remote,
    /// <summary>Local Parakeet model (offline).</summary>
    Local
}

public sealed class AppSettings
{
    public List<ApiProfile> Profiles { get; set; } = new();

    /// <summary>Selected transcription engine.</summary>
    public EngineMode Engine { get; set; } = EngineMode.Remote;

    /// <summary>When Remote is selected but the request fails (e.g. offline),
    /// transparently retry on the local model if it's installed.</summary>
    public bool AutoFallbackToLocal { get; set; } = true;

    /// <summary>Toggle hotkey to start/stop recording. Defaults to Ctrl+Alt+Space.</summary>
    public HotkeyCombo ToggleHotkey { get; set; } = new()
    {
        Ctrl = true,
        Alt = true,
        VirtualKey = 0x20 // VK_SPACE
    };

    /// <summary>Also allow the mouse middle button to toggle recording.</summary>
    public bool UseMiddleMouse { get; set; }

    /// <summary>Launch on Windows sign-in, hidden to the tray.</summary>
    public bool RunOnStartup { get; set; }

    /// <summary>Play start/stop cues.</summary>
    public bool PlaySounds { get; set; } = true;

    [JsonIgnore]
    public ApiProfile? ActiveProfile => Profiles.FirstOrDefault(p => p.Enabled);
}
