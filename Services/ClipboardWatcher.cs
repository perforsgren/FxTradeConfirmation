using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace FxTradeConfirmation.Services;

/// <summary>
/// WPF-native clipboard monitor using <see cref="HwndSource"/> and WM_CLIPBOARDUPDATE.
/// Must be created and started on the UI (STA) thread.
/// </summary>
public sealed class ClipboardWatcher : IClipboardWatcher
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_GETTEXT = 0x000D;
    private const int WM_GETTEXTLENGTH = 0x000E;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SendMessageTimeoutMs = 200;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(200);
    public bool IsListening { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> SourceFilter { get; set; } = Array.Empty<string>();

    /// <inheritdoc/>
    public IReadOnlyList<string> WindowTitleFilter { get; set; } = Array.Empty<string>();

    private HwndSource? _hwndSource;
    private DateTime _lastEventUtc = DateTime.MinValue;
    private string? _lastSignature;

    public void Start()
    {
        if (IsListening) return;

        var parameters = new HwndSourceParameters("ClipboardWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        if (!AddClipboardFormatListener(_hwndSource.Handle))
            throw new InvalidOperationException("AddClipboardFormatListener failed.");

        IsListening = true;
    }

    public void Stop()
    {
        if (!IsListening || _hwndSource == null) return;

        RemoveClipboardFormatListener(_hwndSource.Handle);
        _hwndSource.RemoveHook(WndProc);
        _hwndSource.Dispose();
        _hwndSource = null;
        IsListening = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastEventUtc) < DebounceInterval)
            return;

        string? text = null;
        string signature;

        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();

                // H12: Do NOT use GetHashCode() — hash randomisation in .NET 6+
                // makes it non-deterministic across runs and can collide within a run.
                // Use the full text as the dedup key (ordinal comparison below).
                signature = text ?? string.Empty;
            }
            else
            {
                signature = "NOTEXT";
            }
        }
        catch
        {
            signature = "ERR";
        }

        // Ordinal string equality — zero collision risk, fully deterministic.
        if (_lastSignature != null &&
            string.Equals(_lastSignature, signature, StringComparison.Ordinal))
        {
            _lastEventUtc = nowUtc;
            return;
        }

        _lastEventUtc = nowUtc;
        _lastSignature = signature;

        var source = ProbeSource();

        // Filter on exe name (no path, no extension), case-insensitive
        if (SourceFilter.Count > 0)
        {
            var exeName = Path.GetFileNameWithoutExtension(source.SourceExe).ToLowerInvariant();
            if (!SourceFilter.Any(f => f.ToLowerInvariant() == exeName))
                return;
        }

        // Filter on foreground window title substring, case-insensitive
        if (WindowTitleFilter.Count > 0)
        {
            var title = source.ForegroundWindowTitle.ToLowerInvariant();
            if (!WindowTitleFilter.Any(f => title.Contains(f.ToLowerInvariant())))
                return;
        }

        ClipboardChanged?.Invoke(this, new ClipboardChangedEventArgs(text, source, nowUtc));
    }

    private static ClipboardSourceInfo ProbeSource()
    {
        var fgHwnd = GetForegroundWindow();
        var fgTitle = GetWindowTitle(fgHwnd);
        var fgClass = GetWindowClass(fgHwnd);
        var ownerHwnd = GetClipboardOwner();
        var ownerTitle = GetWindowTitle(ownerHwnd);

        var exeHwnd = ownerHwnd != IntPtr.Zero ? ownerHwnd : fgHwnd;
        string exe = ResolveExe(exeHwnd);

        var confidence = ownerHwnd != IntPtr.Zero
            ? ClipboardSourceConfidence.Owner
            : ClipboardSourceConfidence.Foreground;

        GetWindowThreadProcessId(exeHwnd, out uint pid);

        string chatName = ProbeChildWindows(fgHwnd, out string debugInfo);

        return new ClipboardSourceInfo(
            exe, fgTitle, fgClass, ownerTitle,
            (int)pid, fgHwnd, confidence,
            chatName, debugInfo);
    }

    /// <summary>
    /// Enumerates ALL child windows of <paramref name="parentHwnd"/> recursively
    /// and collects their HWND, class name and text (via both GetWindowText and
    /// SendMessageTimeout WM_GETTEXT so we catch owner-draw controls too).
    /// The debug dump is written to <paramref name="debugInfo"/>.
    /// The best candidate for a chat name (first non-empty, non-root child title) is returned.
    /// </summary>
    private static string ProbeChildWindows(IntPtr parentHwnd, out string debugInfo)
    {
        var lines = new List<string>();
        string? best = null;
        var rootTitle = GetWindowTitle(parentHwnd);
        var rootClass = GetWindowClass(parentHwnd);

        lines.Add($"Root  hwnd=0x{parentHwnd:X}  class=\"{rootClass}\"  title=\"{rootTitle}\"");

        EnumChildWindows(parentHwnd, (hwnd, _) =>
        {
            try
            {
                var cls = GetWindowClass(hwnd);
                var title = GetWindowTitle(hwnd);
                var msgTxt = GetWindowTextViaSendMessageTimeout(hwnd);

                // Merge — prefer the longer of the two
                var text = msgTxt.Length >= title.Length ? msgTxt : title;

                GetWindowRect(hwnd, out RECT rc);
                var visible = IsWindowVisible(hwnd);
                var w = rc.Right - rc.Left;
                var h = rc.Bottom - rc.Top;

                var line = $"  hwnd=0x{hwnd:X}  cls=\"{cls}\"  vis={visible}  {w}x{h}  \"{text}\"";
                lines.Add(line);

                // First visible child with a non-trivial title that differs from root = candidate
                if (best == null
                    && visible
                    && !string.IsNullOrWhiteSpace(text)
                    && text != rootTitle
                    && text.Length > 2)
                {
                    best = text;
                }
            }
            catch { /* skip */ }

            return true; // continue enumeration
        }, IntPtr.Zero);

        debugInfo = string.Join("\n", lines);
        return best ?? "";
    }

    /// <summary>
    /// Reads a window's text via <c>SendMessageTimeout(WM_GETTEXT)</c> with a
    /// <see cref="SendMessageTimeoutMs"/>ms cap and <c>SMTO_ABORTIFHUNG</c>.
    /// Replaces the former <c>SendMessage</c> overload which could block the UI
    /// thread indefinitely if the target process's message pump was unresponsive.
    /// </summary>
    private static string GetWindowTextViaSendMessageTimeout(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        try
        {
            // First get the length
            if (SendMessageTimeout(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, SendMessageTimeoutMs, out var lenResult) == IntPtr.Zero)
                return "";

            var len = (int)lenResult;
            if (len <= 0) return "";

            var sb = new StringBuilder(len + 2);
            if (SendMessageTimeout(hWnd, WM_GETTEXT, (IntPtr)(len + 1), sb,
                    SMTO_ABORTIFHUNG, SendMessageTimeoutMs, out _) == IntPtr.Zero)
                return "";

            return sb.ToString();
        }
        catch { return ""; }
    }

    private static string ResolveExe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return "";
            using var proc = Process.GetProcessById((int)pid);
            try { return proc.MainModule?.FileName ?? proc.ProcessName; }
            catch { return proc.ProcessName; }
        }
        catch { return ""; }
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        Stop();
        ClipboardChanged = null;
    }

    // --- P/Invoke ---

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // H13: SendMessageTimeout replaces SendMessage for cross-process WM_GETTEXT calls.
    // SMTO_ABORTIFHUNG returns immediately if the target thread is hung.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public void ResetLastSignature()
    {
        _lastSignature = null;
    }
}