/// <summary>
/// Monitors the system clipboard for changes and identifies the source process.
/// </summary>
public interface IClipboardWatcher : IDisposable
{
    /// <summary>Raised on the UI thread when clipboard content changes.</summary>
    event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    /// <summary>Debounce interval to suppress rapid-fire events.</summary>
    TimeSpan DebounceInterval { get; set; }

    /// <summary>
    /// When non-empty, only events whose source exe filename (no extension, lower-case)
    /// matches one of these values are raised. Empty = accept all sources.
    /// </summary>
    IReadOnlyList<string> SourceFilter { get; set; }

    /// <summary>
    /// When non-empty, only events where the foreground window title contains
    /// one of these substrings (case-insensitive) are raised. Empty = accept all.
    /// </summary>
    IReadOnlyList<string> WindowTitleFilter { get; set; }

    /// <summary>Start listening for clipboard changes. Must be called on the STA/UI thread.</summary>
    void Start();

    /// <summary>Stop listening.</summary>
    void Stop();

    /// <summary>Whether the watcher is currently active.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Clears the last-seen signature so the same text can trigger a new event.
    /// Call this after the user has acted on a captured clipboard entry.
    /// </summary>
    void ResetLastSignature();
}

public sealed class ClipboardChangedEventArgs : EventArgs
{
    public string? Text { get; }
    public ClipboardSourceInfo Source { get; }
    public DateTime TimestampUtc { get; }

    public ClipboardChangedEventArgs(string? text, ClipboardSourceInfo source, DateTime timestampUtc)
    {
        Text = text;
        Source = source;
        TimestampUtc = timestampUtc;
    }
}

public sealed class ClipboardSourceInfo
{
    public string SourceExe { get; }

    /// <summary>Title of the foreground window at the time of copy — e.g. "IB - IB Manager".</summary>
    public string ForegroundWindowTitle { get; }

    /// <summary>Window class of the foreground window — e.g. "wdmm-Win32Window".</summary>
    public string ForegroundWindowClass { get; }

    /// <summary>Title of the clipboard owner window (often empty for background processes).</summary>
    public string OwnerWindowTitle { get; }

    /// <summary>
    /// Chat/panel name resolved via UI Automation from the active Bloomberg IB window.
    /// E.g. "Swedbank FX Options". Empty if not detectable.
    /// </summary>
    public string ChatName { get; }

    /// <summary>
    /// Debug dump of all named UI Automation elements found in the foreground window.
    /// Temporary — used to discover what Bloomberg IB exposes via accessibility.
    /// </summary>
    public string AutomationDebug { get; }

    public int SourcePid { get; }
    public IntPtr SourceHwnd { get; }
    public ClipboardSourceConfidence Confidence { get; }

    public ClipboardSourceInfo(
        string exe,
        string foregroundTitle,
        string foregroundClass,
        string ownerTitle,
        int pid,
        IntPtr hwnd,
        ClipboardSourceConfidence confidence,
        string chatName = "",
        string automationDebug = "")
    {
        SourceExe = exe ?? "";
        ForegroundWindowTitle = foregroundTitle ?? "";
        ForegroundWindowClass = foregroundClass ?? "";
        OwnerWindowTitle = ownerTitle ?? "";
        ChatName = chatName ?? "";
        AutomationDebug = automationDebug ?? "";
        SourcePid = pid;
        SourceHwnd = hwnd;
        Confidence = confidence;
    }

    public static ClipboardSourceInfo Unknown() =>
        new("", "", "", "", 0, IntPtr.Zero, ClipboardSourceConfidence.Unknown);

    public override string ToString() =>
        $"{Confidence}: {SourceExe} (PID {SourcePid}) — \"{ForegroundWindowTitle}\" [{ForegroundWindowClass}] Chat: \"{ChatName}\"";
}

public enum ClipboardSourceConfidence
{
    Unknown = 0,
    Owner = 1,
    Foreground = 2
}