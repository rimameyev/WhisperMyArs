using System.Drawing;
using System.Windows;
using WhisperMyAss.Models;
using WhisperMyAss.Services;
using WhisperMyAss.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace WhisperMyAss;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    private readonly SettingsStore _store = new();
    private AppSettings _settings = new();

    private HotkeyManager? _hotkeys;
    private DictationController? _controller;
    private OverlayWindow? _overlay;
    private System.Windows.Forms.NotifyIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance — second launch just exits.
        _singleInstance = new Mutex(initiallyOwned: true, "WhisperMyAss.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        _settings = _store.Load();

        _overlay = new OverlayWindow();
        _controller = new DictationController(_overlay, () => _settings, ShowBalloon);

        _hotkeys = new HotkeyManager();
        _hotkeys.TogglePressed += () => _controller.Toggle();
        _hotkeys.CancelPressed += () => _controller.Cancel();
        _hotkeys.UpdateBindings(_settings.ToggleHotkey, _settings.UseMiddleMouse);
        _hotkeys.Install();

        BuildTray();

        if (_settings.ActiveProfile is null || string.IsNullOrWhiteSpace(_settings.ActiveProfile.ApiKey))
            ShowBalloon("Add your Groq API key in Settings to get started.");
    }

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Visible = true,
            Text = "WhisperMyAss",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _hotkeys!, ApplySettings);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Called by the settings window after the user saves.</summary>
    private void ApplySettings(AppSettings updated)
    {
        _settings = updated;
        _store.Save(_settings);
        _hotkeys!.UpdateBindings(_settings.ToggleHotkey, _settings.UseMiddleMouse);
        StartupManager.Set(_settings.RunOnStartup);
    }

    private void ShowBalloon(string message)
    {
        if (Dispatcher.CheckAccess())
            _tray?.ShowBalloonTip(3000, "WhisperMyAss", message, System.Windows.Forms.ToolTipIcon.Info);
        else
            Dispatcher.Invoke(() => ShowBalloon(message));
    }

    private void QuitApp()
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _controller?.Dispose();
        _overlay?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Builds a small tray icon at runtime (no .ico asset to ship).</summary>
internal static class TrayIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(0x6E, 0x9B, 0xFF));
            g.FillEllipse(bg, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 3f);
            // a simple little "waveform"
            g.DrawLine(pen, 10, 16, 10, 16);
            g.DrawLine(pen, 13, 11, 13, 21);
            g.DrawLine(pen, 16, 8, 16, 24);
            g.DrawLine(pen, 19, 11, 19, 21);
            g.DrawLine(pen, 22, 14, 22, 18);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
