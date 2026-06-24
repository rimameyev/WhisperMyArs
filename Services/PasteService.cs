using System.Runtime.InteropServices;
using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace WhisperMyAss.Services;

/// <summary>
/// Places text on the clipboard and emulates Ctrl+V into a target window.
///
/// Two subtleties this handles:
///  * The clipboard is set on the calling (STA/UI) thread, but the actual key
///    injection runs on a background thread so the UI thread stays free to
///    service our low-level keyboard hook — otherwise the injected keystrokes
///    can stall, since that hook is serviced on the UI thread.
///  * We re-assert the target as foreground right before pasting so the keys
///    land in the field the user was in when they started dictating.
/// </summary>
public static class PasteService
{
    /// <param name="text">Text to paste.</param>
    /// <param name="targetWindow">Foreground window captured when recording began.</param>
    public static void SetClipboardAndPaste(string text, IntPtr targetWindow)
    {
        if (string.IsNullOrEmpty(text))
            return;

        SetClipboardText(text); // must be on the STA UI thread (the caller is)

        // Inject off the UI thread so the keyboard hook can be serviced.
        var thread = new Thread(() =>
        {
            if (targetWindow != IntPtr.Zero)
            {
                SetForegroundWindow(targetWindow);
                Thread.Sleep(80); // let focus settle in the target
            }
            else
            {
                Thread.Sleep(60);
            }
            SendCtrlV();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private static void SetClipboardText(string text)
    {
        // Retry: the clipboard can be transiently locked by other apps.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (Exception)
            {
                Thread.Sleep(40);
            }
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown(VK_CONTROL),
            KeyDown(VK_V),
            KeyUp(VK_V),
            KeyUp(VK_CONTROL),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // ----------------------------- Win32 -----------------------------
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    private static INPUT KeyDown(ushort vk) => MakeKey(vk, false);
    private static INPUT KeyUp(ushort vk) => MakeKey(vk, true);

    private static INPUT MakeKey(ushort vk, bool up)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        // MOUSEINPUT is the largest member; including it sizes the union (and
        // therefore INPUT) correctly so SendInput's cbSize check passes on x64.
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
