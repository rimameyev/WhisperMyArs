using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;

namespace WhisperMyAss.UI;

/// <summary>
/// A small, click-through "toast" that fades in just above the dictation pill,
/// holds for a moment, then fades out. Used to tell the user when the engine
/// falls back to local or comes back online.
/// </summary>
public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Show a message for ~3 seconds. Safe to call from any thread.</summary>
    public void Show(string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageText.Text = message;
            // Let it size to the new text, then position above the dictation pill.
            UpdateLayout();
            PositionAbovePill();
            Show();
            Animate();
        });
    }

    private void Animate()
    {
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2700))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3000))));
        anim.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, anim);
    }

    private void PositionAbovePill()
    {
        var area = SystemParameters.WorkArea;
        // The dictation pill sits ~12px above the taskbar and is ~52px tall;
        // place this toast comfortably above it.
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 84;
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
