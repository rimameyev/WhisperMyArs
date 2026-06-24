# WhisperMyAss

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
  DPAPI-encrypted under `%APPDATA%\WhisperMyAss\settings.json` (never
  plaintext).
- **Activation** — capture a toggle hotkey; optionally enable the middle mouse
  button.
- **General** — run on Windows startup (hidden to tray); start/stop sounds.

The app uses the default system microphone and speaker.

## Build & run

```
dotnet build -c Release
bin/Release/net8.0-windows/WhisperMyAss.exe
```

### Default Groq profile values

- URL: `https://api.groq.com/openai/v1/audio/transcriptions`
- Model: `whisper-large-v3-turbo`
