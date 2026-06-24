using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WhisperMyAss.UI;

/// <summary>
/// Small always-on-top status pill shown just above the taskbar. Two states:
/// "Listening" (animated bars driven by mic level) and "Transcribing"
/// (pulsing label). Click-through so it never steals input from the user.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 7;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _current = new double[BarCount];
    private readonly double[] _target = new double[BarCount];
    private readonly Random _rng = new();

    private readonly DispatcherTimer _animTimer;
    private volatile float _level;
    private const double MinBar = 4;
    private const double MaxBar = 34;

    public OverlayWindow()
    {
        InitializeComponent();
        BuildBars();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 fps
        };
        _animTimer.Tick += (_, _) => StepBars();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void BuildBars()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0x6E, 0x9B, 0xFF),
            Color.FromRgb(0xB8, 0x86, 0xFF),
            new Point(0, 0), new Point(0, 1));

        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 4,
                Height = MinBar,
                RadiusX = 2,
                RadiusY = 2,
                Fill = brush,
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _bars[i] = bar;
            _current[i] = MinBar;
            _target[i] = MinBar;
            BarsPanel.Children.Add(bar);
        }
    }

    // ---------------- public state transitions ----------------

    public void ShowListening()
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Visibility = Visibility.Collapsed;
            BarsPanel.Visibility = Visibility.Visible;
            PositionAboveTaskbar();
            Show();
            _animTimer.Start();
        });
    }

    public void ShowTranscribing()
    {
        Dispatcher.Invoke(() =>
        {
            _animTimer.Stop();
            BarsPanel.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Visible;
            StartPulse();
        });
    }

    public void HideOverlay()
    {
        Dispatcher.Invoke(() =>
        {
            _animTimer.Stop();
            StatusText.BeginAnimation(OpacityProperty, null);
            Hide();
        });
    }

    /// <summary>Feed the latest mic level (0..1). Safe to call from any thread.</summary>
    public void SetLevel(float level) => _level = level;

    // ---------------- bar animation ----------------

    private void StepBars()
    {
        float level = _level;
        for (int i = 0; i < BarCount; i++)
        {
            // Centre bars react strongest; add jitter so it looks alive.
            double centerBias = 1.0 - Math.Abs(i - (BarCount - 1) / 2.0) / BarCount;
            double jitter = 0.6 + _rng.NextDouble() * 0.8;
            double targetNorm = Math.Clamp(level * centerBias * jitter, 0, 1);
            _target[i] = MinBar + targetNorm * (MaxBar - MinBar);

            // Ease toward target (snappier on the way up).
            double ease = _target[i] > _current[i] ? 0.5 : 0.25;
            _current[i] += (_target[i] - _current[i]) * ease;
            _bars[i].Height = _current[i];
        }
    }

    private void StartPulse()
    {
        var pulse = new DoubleAnimation
        {
            From = 0.45,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(700),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        StatusText.BeginAnimation(OpacityProperty, pulse);
    }

    // ---------------- positioning & click-through ----------------

    private void OnLoaded(object sender, RoutedEventArgs e) => PositionAboveTaskbar();

    private void PositionAboveTaskbar()
    {
        var area = SystemParameters.WorkArea; // excludes the taskbar
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 12;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        int ex = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(helper.Handle, GWL_EXSTYLE, ex);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
