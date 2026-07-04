using System.Runtime.InteropServices;
using System.Windows;
using WhisperMyArs.Models;
using WhisperMyArs.UI;
using Application = System.Windows.Application;
using Timer = System.Threading.Timer;

namespace WhisperMyArs.Services;

public enum DictationState { Idle, Recording, Transcribing }

/// <summary>
/// Owns the Idle → Recording → Transcribing → Idle lifecycle and coordinates
/// the recorder, transcription engine, overlay, sounds and paste. All public
/// entry points marshal onto the WPF dispatcher so they're safe to call from
/// the hotkey hook thread.
/// </summary>
public sealed class DictationController : IDisposable
{
    private readonly AudioRecorder _recorder = new();
    private readonly RemoteTranscriber _remote;
    private readonly LocalTranscriber _local = new();
    private readonly ToastWindow _toast = new();
    private readonly OverlayWindow _overlay;

    // Remote-offline state: once a connection failure is detected we stop
    // hitting the API on every dictation (avoids the per-call delay and repeated
    // messages) and instead probe it every 15 minutes in the background.
    private volatile bool _remoteOffline;
    private volatile bool _justRecovered;
    private Timer? _probeTimer;
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(15);
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _notify;

    private CancellationTokenSource? _cts;

    /// <summary>The window that had focus when recording started — where we paste.</summary>
    private IntPtr _targetWindow;

    public DictationState State { get; private set; } = DictationState.Idle;

    public DictationController(OverlayWindow overlay, Func<AppSettings> settings, Action<string> notify)
    {
        _overlay = overlay;
        _settings = settings;
        _notify = notify;
        _remote = new RemoteTranscriber(() => _settings().ActiveProfile);
        _recorder.LevelChanged += level => _overlay.SetLevel(level);
    }

    /// <summary>Picks the engine for this dictation based on settings. Local is
    /// used when selected and installed; otherwise remote. (Auto-fallback when
    /// a remote request fails arrives in Phase 4.)</summary>
    private ITranscriber ResolveTranscriber()
    {
        if (_settings().Engine == EngineMode.Local && _local.IsInstalled)
            return _local;
        return _remote;
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
        // Local mode (installed) needs no API profile; otherwise require one.
        bool localReady = _settings().Engine == EngineMode.Local && _local.IsInstalled;
        if (!localReady)
        {
            var profile = _settings().ActiveProfile;
            if (profile is null || string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                _notify(_settings().Engine == EngineMode.Local
                    ? "Local model not installed. Open Settings to download it."
                    : "No active API profile. Open Settings to add one.");
                return;
            }
        }

        // Remember where the caret is now — that's where we'll paste later.
        _targetWindow = GetForegroundWindow();

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

        float[]? samples = _recorder.Stop();
        if (samples is null || samples.Length < AudioRecorder.OutputSampleRate / 10) // < ~0.1s
        {
            _notify("Nothing recorded.");
            SetIdle();
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            string text = await TranscribeWithFallbackAsync(samples, _cts.Token);
            if (!string.IsNullOrWhiteSpace(text))
                // Trailing space so the next dictation doesn't run into this one.
                PasteService.SetClipboardAndPaste(text + " ", _targetWindow);
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

    /// <summary>Runs the selected engine with auto-fallback to local. Handles
    /// the offline state machine: skip the API while known-offline, fall back to
    /// local on a connection failure (once), and announce recovery.</summary>
    private async Task<string> TranscribeWithFallbackAsync(float[] samples, CancellationToken ct)
    {
        int rate = AudioRecorder.OutputSampleRate;
        bool canFallback = _settings().Engine == EngineMode.Remote
                           && _settings().AutoFallbackToLocal
                           && _local.IsInstalled;

        // Known-offline: go straight to local, no API attempt, no repeated message.
        if (canFallback && _remoteOffline)
            return await _local.TranscribeAsync(samples, rate, ct);

        var primary = ResolveTranscriber();
        try
        {
            var text = await primary.TranscribeAsync(samples, rate, ct);

            // First success after the probe cleared the offline flag — announce it.
            if (primary == _remote && _justRecovered)
            {
                _justRecovered = false;
                _toast.Show("Main model back online");
            }
            return text;
        }
        catch (RemoteApiException) when (canFallback && !ct.IsCancellationRequested)
        {
            // Server responded with an error (rate limit, auth, …) — not a
            // connectivity problem, so fall back for this one without entering
            // offline mode (next dictation retries the API).
            return await _local.TranscribeAsync(samples, rate, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   && canFallback
                                   && !ct.IsCancellationRequested)
        {
            // Connection failure → enter offline mode, notify once, start probe.
            _remoteOffline = true;
            _toast.Show("Main model offline — switching to local");
            StartReconnectProbe();
            return await _local.TranscribeAsync(samples, rate, ct);
        }
    }

    private void StartReconnectProbe()
    {
        _probeTimer ??= new Timer(_ => _ = ProbeOnceAsync(), null, ProbeInterval, ProbeInterval);
    }

    private async Task ProbeOnceAsync()
    {
        try
        {
            if (await _remote.ProbeAsync(CancellationToken.None))
            {
                _remoteOffline = false;
                _justRecovered = true;     // next dictation will use the API + announce
                _probeTimer?.Dispose();
                _probeTimer = null;
            }
        }
        catch { /* stay offline; try again next interval */ }
    }

    private void SetIdle()
    {
        State = DictationState.Idle;
        _overlay.HideOverlay();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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
        _probeTimer?.Dispose();
        _recorder.Dispose();
        _local.Dispose();
    }
}
