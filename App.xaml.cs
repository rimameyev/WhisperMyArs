using System.Drawing;
using System.IO;
using System.Windows;
using WhisperMyAss.Models;
using WhisperMyAss.Services;
using WhisperMyAss.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace WhisperMyAss;

public partial class App : Application
{
    private const string ShowSettingsSignal = "WhisperMyAss.ShowSettings";

    private static Mutex? _singleInstance;
    private EventWaitHandle? _showSettings;
    private volatile bool _shuttingDown;

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

        // Never vanish silently: log unhandled exceptions and tell the user.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash(ex.Exception);
            MessageBox.Show($"WhisperMyAss hit an error:\n\n{ex.Exception.Message}\n\nDetails saved to crash.log.",
                "WhisperMyAss", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true; // keep the tray app alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception crash) LogCrash(crash);
        };

        // Single instance — a second launch tells the running instance to show
        // Settings (so clicking the Start Menu/desktop icon surfaces the app),
        // then exits.
        _singleInstance = new Mutex(initiallyOwned: true, "WhisperMyAss.SingleInstance", out bool isNew);
        if (!isNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowSettingsSignal, out var ev))
                    ev.Set();
            }
            catch { /* best effort */ }
            Shutdown();
            return;
        }

        // Listen for "show settings" signals from later launches.
        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignal);

        _settings = _store.Load();

        _overlay = new OverlayWindow();
        _controller = new DictationController(_overlay, () => _settings, ShowBalloon);

        _hotkeys = new HotkeyManager();
        _hotkeys.TogglePressed += () => _controller.Toggle();
        _hotkeys.CancelPressed += () => _controller.Cancel();
        _hotkeys.UpdateBindings(_settings.ToggleHotkey, _settings.UseMiddleMouse);
        _hotkeys.Install();

        BuildTray();

        // Hide-to-tray applies only to the Windows boot launch, which passes
        // "--tray" (see StartupManager). Any manual launch — the installer's
        // "Launch now", Start Menu, or the desktop icon — opens Settings so the
        // user sees the app is running and learns it lives in the tray.
        bool launchedAtBoot = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        if (launchedAtBoot)
        {
            if (_settings.ActiveProfile is null || string.IsNullOrWhiteSpace(_settings.ActiveProfile.ApiKey))
                ShowBalloon("Add your Groq API key in Settings to get started.");
        }
        else
        {
            OpenSettings();
        }

        StartShowSettingsListener();
    }

    /// <summary>Background wait loop: a second launch sets the named event, and
    /// we surface Settings on the UI thread.</summary>
    private void StartShowSettingsListener()
    {
        if (_showSettings is null) return;
        var thread = new Thread(() =>
        {
            while (_showSettings.WaitOne())
            {
                if (_shuttingDown) return;
                try { Dispatcher.Invoke(OpenSettings); }
                catch { /* app may be shutting down */ }
            }
        })
        {
            IsBackground = true,
            Name = "ShowSettingsListener"
        };
        thread.Start();
    }

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(0x26, 0x26, 0x2C),
            ForeColor = Color.FromArgb(0xED, 0xED, 0xF2),
            RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional,
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false }
        };
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
        // Left-click (or double-click) opens Settings; right-click shows the menu.
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                OpenSettings();
        };
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

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhisperMyAss");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }

    private void QuitApp()
    {
        ReleaseListener();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _controller?.Dispose();
        _overlay?.Close();
        Shutdown();
    }

    /// <summary>Wake the listener thread so it can exit instead of blocking.</summary>
    private void ReleaseListener()
    {
        _shuttingDown = true;
        try { _showSettings?.Set(); } catch { /* already disposed */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseListener();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _controller?.Dispose();
        _showSettings?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Dark palette for the tray context menu.</summary>
internal sealed class DarkMenuColors : System.Windows.Forms.ProfessionalColorTable
{
    private static readonly Color Bg = Color.FromArgb(0x26, 0x26, 0x2C);
    private static readonly Color Hover = Color.FromArgb(0x3A, 0x3A, 0x44);
    private static readonly Color Line = Color.FromArgb(0x3A, 0x3A, 0x44);

    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Bg;
    public override Color ImageMarginGradientMiddle => Bg;
    public override Color ImageMarginGradientEnd => Bg;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Hover;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
}

/// <summary>Loads the app's embedded waveform icon for the tray.</summary>
internal static class TrayIcon
{
    public static Icon Create()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("app.ico");
        // Pick the size Windows wants for the notification area.
        return stream is not null
            ? new Icon(stream, SystemInformation.SmallIconSize)
            : SystemIcons.Application;
    }
}
