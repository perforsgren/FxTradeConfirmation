using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Locates the Bloomberg Terminal main window (class "wdmm-Win32Window"),
/// activates it, opens a new tab (Ctrl+T), pastes the given OVML text
/// (Ctrl+V) and presses Enter — all via Win32 P/Invoke + SendInput.
/// </summary>
public sealed class BloombergPaster : IBloombergPaster
{
    private static readonly HashSet<string> ForbiddenFirstWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IB", "MSG", "CHAT", "SETTINGS", "HELP", "ALERT"
    };

    /// <summary>
    /// Callback invoked around clipboard writes so the app's own
    /// <see cref="ClipboardWatcher"/> can suppress events.
    /// </summary>
    public Action<bool>? SuppressClipboardEvents { get; set; }

    /// <summary>Cached struct size — computed once at startup.</summary>
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public async Task<bool> PasteOvmlAsync(string ovmlText)
    {
        if (string.IsNullOrWhiteSpace(ovmlText))
            return false;

        // ── 1. Find the Bloomberg window ─────────────────────────────────────
        var hwnd = FindBloombergWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        // ── 2. Activate Bloomberg ────────────────────────────────────────────
        ActivateWindow(hwnd);

        // Wait until Bloomberg actually has foreground focus (up to 2 s)
        if (!await WaitForForegroundAsync(hwnd, timeoutMs: 2000))
            return false;

        // ── 3. Open new tab: Ctrl+T ──────────────────────────────────────────
        SendKeyCombo(VK_CONTROL, VK_T);
        await Task.Delay(150);

        // ── 4. Write OVML to clipboard ───────────────────────────────────────
        SuppressClipboardEvents?.Invoke(true);
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                SetClipboardTextRobust(ovmlText));
        }
        finally
        {
            await Task.Delay(50);
            SuppressClipboardEvents?.Invoke(false);
        }

        await Task.Delay(200);

        // ── 5. Paste: Ctrl+V ─────────────────────────────────────────────────
        SendKeyCombo(VK_CONTROL, VK_V);
        await Task.Delay(150);

        // ── 6. Execute: Enter ────────────────────────────────────────────────
        SendKey(VK_RETURN);

        return true;
    }

    // ── Bloomberg window discovery ───────────────────────────────────────────

    private static IntPtr FindBloombergWindow()
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var cls = GetWindowClassString(hWnd);
            if (cls != "wdmm-Win32Window")
                return true;

            var title = GetWindowTitleString(hWnd);

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0 && ForbiddenFirstWords.Contains(words[0]))
                return true;

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_APPWINDOW) == 0)
                return true;

            found = hWnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    // ── Window activation ────────────────────────────────────────────────────

    private static void ActivateWindow(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint targetPid);
        AllowSetForegroundWindow(targetPid);

        ShowWindow(hWnd, SW_RESTORE);

        // ALT trick: unlock the foreground lock that Windows imposes
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KBD_KEYUP, UIntPtr.Zero);

        uint currentThreadId = GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

        bool attached = false;
        if (currentThreadId != targetThreadId)
            attached = AttachThreadInput(currentThreadId, targetThreadId, true);

        try
        {
            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    private static async Task<bool> WaitForForegroundAsync(IntPtr targetHwnd, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (GetForegroundWindow() == targetHwnd)
                return true;

            await Task.Delay(50);
        }
        return false;
    }

    // ── Clipboard with retry ─────────────────────────────────────────────────

    private static void SetClipboardTextRobust(string text, int maxRetries = 5)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException) when (i < maxRetries - 1)
            {
                Thread.Sleep(50);
            }
        }
    }

    // ── SendInput helpers ────────────────────────────────────────────────────
    //
    //  We use keybd_event for the actual keystroke injection instead of
    //  SendInput. keybd_event is simpler, does not require struct packing,
    //  and is proven reliable for inter-process key injection on Bloomberg.
    //

    private static void SendKeyCombo(byte modifier, byte key)
    {
        keybd_event(modifier, 0, 0, UIntPtr.Zero);                  // modifier down
        keybd_event(key, 0, 0, UIntPtr.Zero);                  // key down
        keybd_event(key, 0, KEYEVENTF_KBD_KEYUP, UIntPtr.Zero); // key up
        keybd_event(modifier, 0, KEYEVENTF_KBD_KEYUP, UIntPtr.Zero); // modifier up
    }

    private static void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KBD_KEYUP, UIntPtr.Zero);
    }

    // ── Win32 string helpers ─────────────────────────────────────────────────

    private static string GetWindowTitleString(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowClassString(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ── Constants ────────────────────────────────────────────────────────────

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_KBD_KEYUP = 0x0002;

    private const byte VK_MENU = 0x12;  // ALT
    private const byte VK_CONTROL = 0x11;
    private const byte VK_T = 0x54;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Legacy keystroke injection API. Unlike SendInput, keybd_event does not
    /// require correct INPUT struct sizing and is reliable for cross-process
    /// key injection to Bloomberg Terminal.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // Keep SendInput available for future use but unused for now
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ── SendInput structures (kept for potential future use) ──────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
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
}