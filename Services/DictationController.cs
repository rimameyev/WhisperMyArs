using System.Windows;
using WhisperMyAss.Models;
using WhisperMyAss.UI;
using Application = System.Windows.Application;

namespace WhisperMyAss.Services;

public enum DictationState { Idle, Recording, Transcribing }

/// <summary>
/// Owns the Idle → Recording → Transcribing → Idle lifecycle and coordinates
/// the recorder, Groq client, overlay, sounds and paste. All public entry
/// points marshal onto the WPF dispatcher so they're safe to call from the
/// hotkey hook thread.
/// </summary>
public sealed class DictationController : IDisposable
{
    private readonly AudioRecorder _recorder = new();
    private readonly GroqClient _groq = new();
    private readonly OverlayWindow _overlay;
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _notify;

    private CancellationTokenSource? _cts;

    public DictationState State { get; private set; } = DictationState.Idle;

    public DictationController(OverlayWindow overlay, Func<AppSettings> settings, Action<string> notify)
    {
        _overlay = overlay;
        _settings = settings;
        _notify = notify;
        _recorder.LevelChanged += level => _overlay.SetLevel(level);
    }

    /// <summary>Hotkey toggle: start if idle, finish if recording.</summary>
    public void Toggle()
    {
        Dispatch(() =>
        {
            switch (State)
            {
                case DictationState.Idle: StartRecording(); break;
                case DictationState.Recording: _ = FinishRecordingAsync(); break;
                case DictationState.Transcribing: break; // ignore while in flight
            }
        });
    }

    /// <summary>Escape: cancel whatever is in progress.</summary>
    public void Cancel()
    {
        Dispatch(() =>
        {
            switch (State)
            {
                case DictationState.Recording:
                    _recorder.Cancel();
                    SetIdle();
                    break;
                case DictationState.Transcribing:
                    _cts?.Cancel();
                    SetIdle();
                    break;
            }
        });
    }

    private void StartRecording()
    {
        var profile = _settings().ActiveProfile;
        if (profile is null || string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            _notify("No active API profile. Open Settings to add one.");
            return;
        }

        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            _notify($"Microphone error: {ex.Message}");
            return;
        }

        State = DictationState.Recording;
        if (_settings().PlaySounds) SoundCues.PlayStart();
        _overlay.ShowListening();
    }

    private async Task FinishRecordingAsync()
    {
        State = DictationState.Transcribing;
        if (_settings().PlaySounds) SoundCues.PlayStop();
        _overlay.ShowTranscribing();

        byte[]? wav = _recorder.Stop();
        if (wav is null || wav.Length < 1024)
        {
            _notify("Nothing recorded.");
            SetIdle();
            return;
        }

        var profile = _settings().ActiveProfile!;
        _cts = new CancellationTokenSource();
        try
        {
            string text = await _groq.TranscribeAsync(profile, wav, _cts.Token);
            if (!string.IsNullOrWhiteSpace(text))
                PasteService.SetClipboardAndPaste(text);
        }
        catch (OperationCanceledException)
        {
            // Cancelled via Esc — nothing to report.
        }
        catch (Exception ex)
        {
            _notify($"Transcription failed: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetIdle();
        }
    }

    private void SetIdle()
    {
        State = DictationState.Idle;
        _overlay.HideOverlay();
    }

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _recorder.Dispose();
    }
}
