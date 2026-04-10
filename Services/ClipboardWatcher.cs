using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(200);
    public bool IsListening { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Orthogonal to the <c>_suppressClipboardEvents</c> flag in MainViewModel:
    /// that flag suppresses individual write-back operations; this one disables
    /// passive listening entirely so Ctrl+V can be used instead.
    /// </remarks>
    private volatile bool _suppressAll = false;

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get => !_suppressAll;
        set => _suppressAll = !value;
    }

    private IReadOnlyList<string> _sourceFilter = Array.Empty<string>();
    public IReadOnlyList<string> SourceFilter
    {
        get => _sourceFilter;
        set => _sourceFilter = value ?? Array.Empty<string>();
    }

    private IReadOnlyList<string> _windowTitleFilter = Array.Empty<string>();
    public IReadOnlyList<string> WindowTitleFilter
    {
        get => _windowTitleFilter;
        set => _windowTitleFilter = value ?? Array.Empty<string>();
    }

    private HwndSource? _hwndSource;
    private DateTime _lastEventUtc = DateTime.MinValue;

    /// <summary>
    /// SHA-256 hex digest of the last clipboard text (or a sentinel for non-text/error).
    /// Fixed 64-char string — prevents retaining arbitrarily large clipboard content.
    /// </summary>
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

    public void ResetLastSignature() => _lastSignature = null;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            // IsEnabled == false → discard silently; hook stays registered for instant re-enable.
            if (!_suppressAll)
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

                // Hash the text so _lastSignature is always a fixed-size 64-char hex string.
                // Using SHA-256 avoids the non-determinism of GetHashCode() in .NET 6+.
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
                signature = Convert.ToHexString(bytes);
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
            var exeName = Path.GetFileNameWithoutExtension(source.SourceExe);
            if (!SourceFilter.Any(f => string.Equals(f, exeName, StringComparison.OrdinalIgnoreCase)))
                return;
        }

        // Filter on window title substring, case-insensitive
        if (WindowTitleFilter.Count > 0)
        {
            var title = source.ForegroundWindowTitle;
            if (!WindowTitleFilter.Any(f => title.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return;
        }

        // Re-check suppress flag to close the TOCTOU gap between the initial
        // check in WndProc and the event invocation here.
        if (_suppressAll)
            return;

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

        return new ClipboardSourceInfo(
            exe, fgTitle, fgClass, ownerTitle,
            (int)pid, fgHwnd, confidence);
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

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
