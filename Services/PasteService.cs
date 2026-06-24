using System.Runtime.InteropServices;
using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace WhisperMyAss.Services;

/// <summary>
/// Places text on the clipboard and emulates Ctrl+V into the focused control.
/// </summary>
public static class PasteService
{
    public static void SetClipboardAndPaste(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Clipboard must be set on an STA thread; the app's UI thread is STA.
        SetClipboardText(text);

        // Small delay so the target app settles focus before we inject keys.
        Thread.Sleep(60);
        SendCtrlV();
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
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];

        inputs[0] = KeyDown(VK_CONTROL);
        inputs[1] = KeyDown(VK_V);
        inputs[2] = KeyUp(VK_V);
        inputs[3] = KeyUp(VK_CONTROL);

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // ----------------------------- Win32 -----------------------------
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static INPUT KeyDown(ushort vk) => MakeKey(vk, 0);
    private static INPUT KeyUp(ushort vk) => MakeKey(vk, KEYEVENTF_KEYUP);

    private static INPUT MakeKey(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

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
}
