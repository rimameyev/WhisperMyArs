using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WhisperMyAss.Models;

namespace WhisperMyAss.Services;

/// <summary>
/// Installs low-level keyboard and mouse hooks so the toggle hotkey and the
/// optional middle-mouse trigger work globally, regardless of focus. Using
/// WH_KEYBOARD_LL (rather than RegisterHotKey) lets us capture arbitrary
/// combos during the settings "press a key" capture flow and avoids clashing
/// with other apps' registered hotkeys.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    // ----- raised on the hook thread; marshal to UI before touching WPF -----
    public event Action? TogglePressed;
    public event Action? CancelPressed;

    /// <summary>When set, the next key-down is reported here instead of matching.
    /// Used by the settings UI to capture a new combo.</summary>
    public Action<HotkeyCombo>? CaptureNext { get; set; }

    private HotkeyCombo _combo = new();
    private bool _useMiddleMouse;

    private IntPtr _kbHook;
    private IntPtr _mouseHook;
    // Keep delegates alive for the lifetime of the hooks (GC would collect them otherwise).
    private readonly LowLevelProc _kbProc;
    private readonly LowLevelProc _mouseProc;

    public HotkeyManager()
    {
        _kbProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public void UpdateBindings(HotkeyCombo combo, bool useMiddleMouse)
    {
        _combo = combo;
        _useMiddleMouse = useMiddleMouse;
    }

    public void Install()
    {
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        var hMod = GetModuleHandle(module.ModuleName);

        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                // KBDLLHOOKSTRUCT: vkCode @0, scanCode @4, flags @8.
                int flags = Marshal.ReadInt32(lParam, 8);
                bool injected = (flags & LLKHF_INJECTED) != 0;
                if (!injected) // never react to our own emulated paste keystrokes
                {
                    int vk = Marshal.ReadInt32(lParam);
                    if (HandleKeyDown(vk))
                        return 1; // swallow the key so it doesn't leak to the focused app
                }
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private bool HandleKeyDown(int vk)
    {
        // Capture mode for the settings UI.
        if (CaptureNext is { } capture)
        {
            // Ignore lone modifier presses; wait for a real main key.
            if (IsModifier(vk))
                return false;

            var combo = new HotkeyCombo
            {
                VirtualKey = vk,
                Ctrl = IsDown(VK_CONTROL),
                Alt = IsDown(VK_MENU),
                Shift = IsDown(VK_SHIFT),
                Win = IsDown(VK_LWIN) || IsDown(VK_RWIN)
            };
            CaptureNext = null;
            capture(combo);
            return true;
        }

        // Escape always cancels an active session.
        if (vk == VK_ESCAPE)
        {
            CancelPressed?.Invoke();
            // Don't swallow Escape — let it pass through to whatever else wants it.
            return false;
        }

        if (!_combo.IsSet || vk != _combo.VirtualKey)
            return false;

        if (_combo.Ctrl != IsDown(VK_CONTROL)) return false;
        if (_combo.Alt != IsDown(VK_MENU)) return false;
        if (_combo.Shift != IsDown(VK_SHIFT)) return false;
        bool win = IsDown(VK_LWIN) || IsDown(VK_RWIN);
        if (_combo.Win != win) return false;

        TogglePressed?.Invoke();
        return true;
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _useMiddleMouse && (int)wParam == WM_MBUTTONDOWN)
        {
            TogglePressed?.Invoke();
            return 1; // swallow the middle click
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool IsModifier(int vk) =>
        vk is VK_CONTROL or VK_MENU or VK_SHIFT or VK_LWIN or VK_RWIN
            or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU or VK_LSHIFT or VK_RSHIFT;

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _kbHook = _mouseHook = IntPtr.Zero;
    }

    // ----------------------------- Win32 -----------------------------
    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int LLKHF_INJECTED = 0x10;

    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
