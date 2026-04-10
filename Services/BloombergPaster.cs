using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.IO;

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
    private const string Tag = nameof(BloombergPaster);

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
    /// volatile ensures write visibility across threads without a lock —
    /// IntPtr reads/writes are atomic on x64.
    /// </summary>
    private static volatile IntPtr _cachedTerminalHwnd;

    public BloombergPaster(BloombergPasterOptions? options = null)
        => _options = options ?? new BloombergPasterOptions();

    public async Task<bool> PasteOvmlAsync(string ovmlText)
    {
        if (string.IsNullOrWhiteSpace(ovmlText))
            return false;

        // ── 1. Find the Bloomberg window ─────────────────────────────────────
        var hwnd = FindBloombergWindow();
        if (hwnd == IntPtr.Zero)
        {
            FileLogger.Instance?.Warn(Tag, "Bloomberg Terminal window not found — paste aborted.");
            return false;
        }

        // ── 2. Activate Bloomberg (restores if minimised/hidden behind others) 
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return false;

        await dispatcher.InvokeAsync(() =>
        {
            GetWindowThreadProcessId(hwnd, out uint bbPid);
            AllowSetForegroundWindow(bbPid);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        });

        // Wait until Bloomberg actually has foreground focus (up to 3 s)
        if (!await WaitForForegroundAsync(hwnd, timeoutMs: 3000))
        {
            FileLogger.Instance?.Warn(Tag, "Bloomberg Terminal did not reach foreground within 3 s — paste aborted.");
            return false;
        }

        // ── 3. Open new tab: Ctrl+T ──────────────────────────────────────────
        if (!SendKeyCombo(VK_CONTROL, VK_T))
        {
            FileLogger.Instance?.Warn(Tag, "SendInput(Ctrl+T) blocked — likely UIPI integrity level mismatch.");
            return false;
        }

        await Task.Delay(_options.NewTabDelayMs);

        // ── 4. Write OVML to clipboard ───────────────────────────────────────
        SuppressClipboardEvents?.Invoke(true);
        bool clipboardOk;
        try
        {
            clipboardOk = await SetClipboardTextRobustAsync(ovmlText);
        }
        finally
        {
            var suppress = SuppressClipboardEvents;
            if (suppress is not null)
            {
                var d = Application.Current?.Dispatcher;
                if (d is not null)
                    d.BeginInvoke(suppress, false);
                else
                    suppress(false);
            }
        }

        if (!clipboardOk)
        {
            FileLogger.Instance?.Warn(Tag, "Failed to write OVML to clipboard after retries — paste aborted.");
            return false;
        }

        await Task.Delay(_options.ClipboardSettleMs);

        // ── 5. Paste: Ctrl+V ─────────────────────────────────────────────────
        if (!SendKeyCombo(VK_CONTROL, VK_V))
        {
            FileLogger.Instance?.Warn(Tag, "SendInput(Ctrl+V) blocked — likely UIPI integrity level mismatch.");
            return false;
        }

        await Task.Delay(_options.AfterPasteDelayMs);

        // ── 6. Execute: Enter ────────────────────────────────────────────────
        if (!SendKey(VK_RETURN))
        {
            FileLogger.Instance?.Warn(Tag, "SendInput(Enter) blocked — likely UIPI integrity level mismatch.");
            return false;
        }

        FileLogger.Instance?.Info(Tag, $"OVML pasted successfully ({ovmlText.Length} chars).");

        if (_options.VerifyPasteAsync is { } verify)
            return await verify(CancellationToken.None);

        return true;
    }

    // ── Bloomberg window discovery ───────────────────────────────────────────

    /// <summary>
    /// Finds the Bloomberg Terminal window. Strategy:
    /// 1. Check cached handle — if still a valid terminal candidate, reuse it.
    /// 2. Enumerate top-level "wdmm-Win32Window" windows and score them via
    ///    <see cref="IsTerminalCandidate"/>. Stops as soon as the first window
    ///    exceeding <see cref="MinTerminalChildCount"/> is confirmed (Fix 1).
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

            FileLogger.Instance?.Info(Tag, $"Cached Bloomberg HWND 0x{_cachedTerminalHwnd:X} is no longer valid — rescanning.");
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

                // Fix 1: stop scanning — no other window will have more children
                // than the fully-initialised terminal (confirmed 46 vs. max 3).
                return false;
            }

            var exStyle = GetWindowLongSafe(hWnd, GWL_EXSTYLE);
            var style = GetWindowLongSafe(hWnd, GWL_STYLE);
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

            return true;
        }, IntPtr.Zero);

        var result = bestHwnd != IntPtr.Zero ? bestHwnd : fallbackHwnd;

        if (result == IntPtr.Zero)
            FileLogger.Instance?.Warn(Tag, "No Bloomberg Terminal window found during full window scan.");
        else if (bestHwnd == IntPtr.Zero)
            FileLogger.Instance?.Info(Tag, $"Bloomberg Terminal not fully initialised — using fallback HWND 0x{result:X} ({fallbackChildCount} children).");
        else
            FileLogger.Instance?.Info(Tag, $"Bloomberg Terminal found: HWND 0x{result:X} ({bestChildCount} children).");

        _cachedTerminalHwnd = result;
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
    /// Fix 2: child count is computed via <c>GetWindow</c> traversal instead of
    /// <c>EnumChildWindows</c>, removing one P/Invoke callback delegate.
    /// </summary>
    private static bool IsTerminalCandidate(IntPtr hWnd, out int childCount)
    {
        childCount = 0;

        var exStyle = GetWindowLongSafe(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_APPWINDOW) == 0)
            return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        var style = GetWindowLongSafe(hWnd, GWL_STYLE);
        if ((style & WS_THICKFRAME) == 0)
            return false;

        var title = GetWindowTitleString(hWnd);
        var firstWord = title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (KnownNonTerminalFirstWords.Contains(firstWord))
            return false;

        // Fix 2: count children via GetWindow traversal — no callback delegate,
        // no EnumChildWindows. We only need to know if count >= MinTerminalChildCount,
        // so we stop counting as soon as the threshold is reached.
        int count = 0;
        var child = GetWindow(hWnd, GW_CHILD);
        while (child != IntPtr.Zero)
        {
            count++;
            if (count >= MinTerminalChildCount)
                break;
            child = GetWindow(child, GW_HWNDNEXT);
        }
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

    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard, retrying up to
    /// <paramref name="maxRetries"/> times on <see cref="COMException"/>
    /// (clipboard locked by another process).
    /// Uses <see cref="Task.Delay"/> between retries so the UI thread is
    /// never blocked — unlike the previous <see cref="Thread.Sleep"/> version.
    /// Must be called from the UI/STA thread (Clipboard requires STA).
    /// Returns <see langword="false"/> if <see cref="Application.Current"/>
    /// is null (application shutting down).
    /// </summary>
    private static async Task<bool> SetClipboardTextRobustAsync(string text, int maxRetries = 5)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return false;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await dispatcher.InvokeAsync(() => Clipboard.SetText(text));
                return true;
            }
            catch (COMException) when (i < maxRetries - 1)
            {
                await Task.Delay(50);
            }
        }

        return false;
    }

    // ── SendInput helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Sends a modifier+key combo via SendInput.
    /// Returns <see langword="false"/> if SendInput reports that zero events
    /// were injected — which happens when UIPI blocks the call because the
    /// target process runs at a higher integrity level.
    /// </summary>
    private static bool SendKeyCombo(byte modifier, byte key)
    {
        INPUT[] inputs =
        [
            MakeKeyInput(modifier, 0),
            MakeKeyInput(key,      0),
            MakeKeyInput(key,      KEYEVENTF_KEYUP),
            MakeKeyInput(modifier, KEYEVENTF_KEYUP),
        ];
        return SendInput((uint)inputs.Length, inputs, InputSize) == (uint)inputs.Length;
    }

    /// <summary>
    /// Sends a single key press+release via SendInput.
    /// Returns <see langword="false"/> if SendInput reports that zero events
    /// were injected.
    /// </summary>
    private static bool SendKey(byte vk)
    {
        INPUT[] inputs =
        [
            MakeKeyInput(vk, 0),
            MakeKeyInput(vk, KEYEVENTF_KEYUP),
        ];
        return SendInput((uint)inputs.Length, inputs, InputSize) == (uint)inputs.Length;
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

    private const nint WS_THICKFRAME = 0x00040000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_APPWINDOW = 0x00040000;

    private const uint GW_CHILD = 5;
    private const uint GW_HWNDNEXT = 2;

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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

    // Fix 2: GetWindow replaces EnumChildWindows for child counting.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    // GetWindowLongPtr — korrekt 64-bit P/Invoke (returnerar IntPtr, ej int)
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    /// <summary>
    /// Plattformssäker wrapper för GetWindowLongPtr.
    /// Returnerar <see cref="nint"/> så att bitmaskning mot
    /// <c>nint</c>-konstanter fungerar utan cast.
    /// </summary>
    private static nint GetWindowLongSafe(IntPtr hWnd, int nIndex)
        => (nint)GetWindowLongPtr(hWnd, nIndex);

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
    /// Optional async verification callback invoked after Enter is sent.
    /// Return <see langword="true"/> if the paste was confirmed successful.
    /// </summary>
    public Func<CancellationToken, Task<bool>>? VerifyPasteAsync { get; init; }
}
