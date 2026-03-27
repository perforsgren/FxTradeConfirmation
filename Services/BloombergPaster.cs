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
    /// <summary>
    /// The terminal window must have at least this many child windows.
    /// In practice the terminal has 20+ children; all other APPWIN Bloomberg
    /// windows have ≤ 3.  A threshold of 10 provides a safe margin.
    /// </summary>
    private const int MinTerminalChildCount = 10;

    /// <summary>
    /// First words that identify known non-terminal Bloomberg APPWIN windows.
    /// Used as a lightweight guard before the child-count check.
    /// </summary>
    private static readonly HashSet<string> KnownNonTerminalFirstWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IB", "BLRT", "BLOOMBERG:",
    };

    /// <summary>
    /// Callback invoked around clipboard writes so the app's own
    /// <see cref="ClipboardWatcher"/> can suppress events.
    /// </summary>
    public Action<bool>? SuppressClipboardEvents { get; set; }

    /// <summary>Cached struct size — computed once at startup.</summary>
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private readonly BloombergPasterOptions _options;

    /// <summary>
    /// Caches the last known good Bloomberg Terminal window handle.
    /// Re-validated on each call; cleared if the window no longer exists.
    /// </summary>
    private static IntPtr _cachedTerminalHwnd;

    public BloombergPaster(BloombergPasterOptions? options = null)
        => _options = options ?? new BloombergPasterOptions();

    public async Task<bool> PasteOvmlAsync(string ovmlText)
    {
        if (string.IsNullOrWhiteSpace(ovmlText))
            return false;

        // ── 1. Find the Bloomberg window ─────────────────────────────────────
        var hwnd = FindBloombergWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        // ── 2. Activate Bloomberg (restores if minimised/hidden behind others) 
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            GetWindowThreadProcessId(hwnd, out uint bbPid);
            AllowSetForegroundWindow(bbPid);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        });

        // Wait until Bloomberg actually has foreground focus (up to 3 s)
        if (!await WaitForForegroundAsync(hwnd, timeoutMs: 3000))
            return false;

        // ── 3. Open new tab: Ctrl+T ──────────────────────────────────────────
        SendKeyCombo(VK_CONTROL, VK_T);
        await Task.Delay(_options.NewTabDelayMs);

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

        await Task.Delay(_options.ClipboardSettleMs);

        // ── 5. Paste: Ctrl+V ─────────────────────────────────────────────────
        SendKeyCombo(VK_CONTROL, VK_V);
        await Task.Delay(_options.AfterPasteDelayMs);

        // ── 6. Execute: Enter ────────────────────────────────────────────────
        SendKey(VK_RETURN);

        if (_options.VerifyPasteAsync is { } verify)
            return await verify(CancellationToken.None);

        return true;
    }

    // ── Bloomberg window discovery ───────────────────────────────────────────

    /// <summary>
    /// Finds the Bloomberg Terminal window. Strategy:
    /// 1. Check cached handle — if still a valid terminal candidate, reuse it.
    /// 2. Enumerate all top-level "wdmm-Win32Window" windows and score them via
    ///    <see cref="IsTerminalCandidate"/>. Pick the one with the most children.
    /// 3. If no window clears <see cref="MinTerminalChildCount"/>, fall back to
    ///    the highest-child-count structural match (handles terminal startup).
    /// </summary>
    private static IntPtr FindBloombergWindow()
    {
        // ── Try cached handle first ──────────────────────────────────────────
        if (_cachedTerminalHwnd != IntPtr.Zero)
        {
            if (IsWindow(_cachedTerminalHwnd) &&
                GetWindowClassString(_cachedTerminalHwnd) == "wdmm-Win32Window" &&
                IsTerminalCandidate(_cachedTerminalHwnd, out _))
                return _cachedTerminalHwnd;

            _cachedTerminalHwnd = IntPtr.Zero;
        }

        // ── Full scan ────────────────────────────────────────────────────────
        // bestHwnd     = highest child count passing ALL criteria (incl. floor)
        // fallbackHwnd = highest child count passing structural criteria only,
        //                used if terminal hasn't fully initialised yet
        IntPtr bestHwnd = IntPtr.Zero;
        int bestChildCount = 0;
        IntPtr fallbackHwnd = IntPtr.Zero;
        int fallbackChildCount = 0;

        EnumWindows((hWnd, _) =>
        {
            if (GetWindowClassString(hWnd) != "wdmm-Win32Window")
                return true;

            var title = GetWindowTitleString(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            if (IsTerminalCandidate(hWnd, out int childCount))
            {
                if (childCount > bestChildCount)
                {
                    bestChildCount = childCount;
                    bestHwnd = hWnd;
                }
            }
            else
            {
                // Passes structural checks but below the child-count floor —
                // track as fallback in case terminal hasn't fully initialised.
                var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                var style = GetWindowLong(hWnd, GWL_STYLE);
                bool structuralMatch = (exStyle & WS_EX_APPWINDOW) != 0
                    && (exStyle & WS_EX_TOOLWINDOW) == 0
                    && (style & WS_THICKFRAME) != 0;

                if (structuralMatch)
                {
                    var firstWord = title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (!KnownNonTerminalFirstWords.Contains(firstWord) && childCount > fallbackChildCount)
                    {
                        fallbackChildCount = childCount;
                        fallbackHwnd = hWnd;
                    }
                }
            }

            return true;
        }, IntPtr.Zero);

        _cachedTerminalHwnd = bestHwnd != IntPtr.Zero ? bestHwnd : fallbackHwnd;
        return _cachedTerminalHwnd;
    }

    /// <summary>
    /// Determines whether <paramref name="hWnd"/> has the structural
    /// properties of the Bloomberg Terminal main window:
    /// <list type="bullet">
    ///   <item><c>WS_EX_APPWINDOW</c> — appears in the taskbar.</item>
    ///   <item>NOT <c>WS_EX_TOOLWINDOW</c> — rules out panels/graphs.</item>
    ///   <item><c>WS_THICKFRAME</c> — resizable main window.</item>
    ///   <item>Title does not start with a known non-terminal first word (e.g. "IB").</item>
    ///   <item>Child count ≥ <see cref="MinTerminalChildCount"/>.</item>
    /// </list>
    /// </summary>
    private static bool IsTerminalCandidate(IntPtr hWnd, out int childCount)
    {
        childCount = 0;

        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_APPWINDOW) == 0)
            return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        var style = GetWindowLong(hWnd, GWL_STYLE);
        if ((style & WS_THICKFRAME) == 0)
            return false;

        var title = GetWindowTitleString(hWnd);
        var firstWord = title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (KnownNonTerminalFirstWords.Contains(firstWord))
            return false;

        int count = 0;
        EnumChildWindows(hWnd, (child, lp) => { count++; return true; }, IntPtr.Zero);
        childCount = count;

        return childCount >= MinTerminalChildCount;
    }

    // ── Wait for foreground ──────────────────────────────────────────────────

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

    private static void SendKeyCombo(byte modifier, byte key)
    {
        INPUT[] inputs =
        [
            MakeKeyInput(modifier, 0),
            MakeKeyInput(key,      0),
            MakeKeyInput(key,      KEYEVENTF_KEYUP),
            MakeKeyInput(modifier, KEYEVENTF_KEYUP),
        ];
        SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static void SendKey(byte vk)
    {
        INPUT[] inputs =
        [
            MakeKeyInput(vk, 0),
            MakeKeyInput(vk, KEYEVENTF_KEYUP),
        ];
        SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static INPUT MakeKeyInput(byte vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, 0),
                dwFlags = flags,
            }
        }
    };

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

    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_T = 0x54;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;
    private const uint INPUT_KEYBOARD = 1;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    // ── Structs ───────────────────────────────────────────────────────────────

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

public sealed class BloombergPasterOptions
{
    /// <summary>Milliseconds to wait after opening a new tab (Ctrl+T).</summary>
    public int NewTabDelayMs { get; init; } = 150;

    /// <summary>Milliseconds to wait after writing to the clipboard.</summary>
    public int ClipboardSettleMs { get; init; } = 200;

    /// <summary>Milliseconds to wait after Ctrl+V before pressing Enter.</summary>
    public int AfterPasteDelayMs { get; init; } = 150;

    /// <summary>
    /// Optional verification: called after Enter is sent.
    /// Return true if the paste is confirmed, false to signal failure.
    /// If null, the method always returns true (fire-and-forget).
    /// </summary>
    public Func<CancellationToken, Task<bool>>? VerifyPasteAsync { get; init; }
}