# WhisperMyArs

A lightweight, tray-resident voice-dictation app for Windows 11. Toggle a
hotkey, speak, and the transcribed text is pasted into whatever field has
focus. No local models — audio is sent to an OpenAI-compatible Whisper
endpoint (Groq by default).

## How it works

1. **Toggle hotkey** (default `Ctrl+Alt+Space`, or the mouse middle button if
   enabled) starts recording. A soft cue plays and a small pill appears above
   the taskbar with animated bars that react to your voice.
2. Press the **same hotkey** again to finish (or **Esc** to cancel). The pill
   shows a pulsing "Transcribing…".
3. The recording (16 kHz mono WAV) is uploaded to your active API profile, the
   returned text is placed on the clipboard, and **Ctrl+V** is emulated into
   the focused field. The pill disappears.

## Settings (tray → Settings…)

- **API profiles** — add one or more endpoints (name, API key, transcription
  URL, model). Exactly one is active at a time. Keys are stored
  DPAPI-encrypted under `%APPDATA%\WhisperMyArs\settings.json` (never
  plaintext).
- **Activation** — capture a toggle hotkey; optionally enable the middle mouse
  button.
- **General** — run on Windows startup (hidden to tray); start/stop sounds.

The app uses the default system microphone and speaker.

## Build & run

```
dotnet build -c Release
bin/Release/net8.0-windows/WhisperMyArs.exe
```

### Default Groq profile values

- URL: `https://api.groq.com/openai/v1/audio/transcriptions`
- Model: `whisper-large-v3-turbo`

## Building the shareable installer

The app ships **framework-dependent** (the build is a single ~0.7 MB exe). The
installer is a ~2 MB native `WhisperMyArs-Setup.exe` that checks for the .NET 8
Desktop Runtime and, if it's missing, downloads and installs it before
installing the app — so friends don't need to install anything by hand.

```
# 1. Publish the small framework-dependent single-file app
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o C:\Users\rimae\LocalBuilds\WhisperMyArs\publish\app

# 2. Compile the installer (requires Inno Setup 6.1+)
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\WhisperMyArs.iss
```

Output: `C:\Users\rimae\LocalBuilds\WhisperMyArs\publish\WhisperMyArs-Setup.exe`.
The runtime is only downloaded on machines that don't already have it (link:
`aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe`).

> **Build output is redirected off Google Drive.** `Directory.Build.props` sends
> `bin`/`obj` to `C:\Users\rimae\LocalBuilds\WhisperMyArs`, and the publish/installer
> paths point there too, so regenerated artifacts don't churn the synced source folder.
