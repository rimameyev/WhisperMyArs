using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WhisperMyAss.Models;
using WhisperMyAss.Services;

namespace WhisperMyAss.UI;

public partial class SettingsWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyDarkTitleBar()
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Windows 10 2004+/11)
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
    }

    private readonly HotkeyManager _hotkeys;
    private readonly Action<AppSettings> _onSave;

    private readonly List<ApiProfile> _profiles;
    private HotkeyCombo _hotkey;
    private ApiProfile? _selected;
    private bool _loading;

    public SettingsWindow(AppSettings settings, HotkeyManager hotkeys, Action<AppSettings> onSave)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        _onSave = onSave;

        // Work on copies so "Close" discards unsaved edits.
        _profiles = settings.Profiles.Select(Clone).ToList();
        _hotkey = CloneHotkey(settings.ToggleHotkey);

        MiddleMouseCheck.IsChecked = settings.UseMiddleMouse;
        StartupCheck.IsChecked = settings.RunOnStartup;
        SoundsCheck.IsChecked = settings.PlaySounds;

        RefreshProfiles();
        UpdateHotkeyText();

        if (_profiles.Count > 0)
            ProfilesList.SelectedIndex = 0;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    // ---------------- profiles ----------------

    private void RefreshProfiles()
    {
        int sel = ProfilesList.SelectedIndex;
        ProfilesList.Items.Clear();
        foreach (var p in _profiles)
        {
            var label = p.Enabled ? $"{p.Name}  ● active" : p.Name;
            ProfilesList.Items.Add(new ListBoxItem { Content = label, Tag = p });
        }
        if (sel >= 0 && sel < ProfilesList.Items.Count)
            ProfilesList.SelectedIndex = sel;
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = (ProfilesList.SelectedItem as ListBoxItem)?.Tag as ApiProfile;
        LoadEditor();
    }

    private void LoadEditor()
    {
        if (_selected is null)
        {
            ProfileEditor.IsEnabled = false;
            return;
        }

        _loading = true;
        ProfileEditor.IsEnabled = true;
        NameBox.Text = _selected.Name;
        KeyBox.Password = _selected.ApiKey;
        UrlBox.Text = _selected.TranscriptionUrl;
        ModelBox.Text = _selected.Model;
        ActiveCheck.IsChecked = _selected.Enabled;
        _loading = false;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var p = new ApiProfile { Name = "New profile", Enabled = _profiles.Count == 0 };
        _profiles.Add(p);
        RefreshProfiles();
        ProfilesList.SelectedIndex = _profiles.Count - 1;
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _profiles.Remove(_selected);
        _selected = null;
        RefreshProfiles();
        if (_profiles.Count > 0) ProfilesList.SelectedIndex = 0;
        else ProfileEditor.IsEnabled = false;
    }

    private void ProfileField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected is null) return;
        _selected.Name = NameBox.Text;
        _selected.TranscriptionUrl = UrlBox.Text;
        _selected.Model = ModelBox.Text;
        RefreshProfiles();
    }

    private void KeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected is null) return;
        _selected.ApiKey = KeyBox.Password;
    }

    private void ActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected is null) return;

        bool active = ActiveCheck.IsChecked == true;
        _selected.Enabled = active;
        if (active)
        {
            // Exactly one active profile.
            foreach (var p in _profiles)
                if (!ReferenceEquals(p, _selected))
                    p.Enabled = false;
        }
        RefreshProfiles();
    }

    // ---------------- hotkey capture ----------------

    private void CaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        HotkeyText.Text = "Press a combination…";
        CaptureBtn.IsEnabled = false;
        _hotkeys.CaptureNext = combo =>
        {
            Dispatcher.Invoke(() =>
            {
                _hotkey = combo;
                UpdateHotkeyText();
                CaptureBtn.IsEnabled = true;
            });
        };
    }

    private void UpdateHotkeyText() => HotkeyText.Text = Describe(_hotkey);

    private static string Describe(HotkeyCombo c)
    {
        if (!c.IsSet) return "—";
        var parts = new List<string>();
        if (c.Ctrl) parts.Add("Ctrl");
        if (c.Alt) parts.Add("Alt");
        if (c.Shift) parts.Add("Shift");
        if (c.Win) parts.Add("Win");
        parts.Add(KeyName(c.VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string KeyName(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key == Key.None ? $"0x{vk:X2}" : key.ToString();
    }

    // ---------------- commit ----------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var updated = new AppSettings
        {
            Profiles = _profiles,
            ToggleHotkey = _hotkey,
            UseMiddleMouse = MiddleMouseCheck.IsChecked == true,
            RunOnStartup = StartupCheck.IsChecked == true,
            PlaySounds = SoundsCheck.IsChecked == true
        };
        _onSave(updated);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _hotkeys.CaptureNext = null; // in case a capture was pending
        Close();
    }

    // ---------------- helpers ----------------

    private static ApiProfile Clone(ApiProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ApiKey = p.ApiKey,
        TranscriptionUrl = p.TranscriptionUrl,
        Model = p.Model,
        Enabled = p.Enabled
    };

    private static HotkeyCombo CloneHotkey(HotkeyCombo c) => new()
    {
        VirtualKey = c.VirtualKey,
        Ctrl = c.Ctrl,
        Alt = c.Alt,
        Shift = c.Shift,
        Win = c.Win
    };
}
