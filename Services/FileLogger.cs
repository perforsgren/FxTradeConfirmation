using System.Diagnostics;
using System.IO;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Simple file-based logger that writes timestamped lines to a daily log file.
/// Each user gets their own sub-folder and log file, keyed by <see cref="Environment.UserName"/>.
/// Thread-safe via lock. Falls back to <see cref="Debug.WriteLine"/> if the
/// network share is unreachable.
/// </summary>
public sealed class FileLogger
{
    private static FileLogger? _instance;
    private static readonly object _lock = new();

    private readonly string _userLogDirectory;
    private readonly string _userName;

    private FileLogger(string logDirectory)
    {
        _userName = Environment.UserName.ToUpper();
        _userLogDirectory = Path.Combine(logDirectory, _userName);
    }

    /// <summary>
    /// Initialise the singleton. Call once at startup.
    /// </summary>
    public static void Initialize(string logDirectory)
    {
        lock (_lock)
        {
            _instance = new FileLogger(logDirectory);
        }
    }

    /// <summary>
    /// The singleton instance. Returns <see langword="null"/> if not initialised.
    /// </summary>
    public static FileLogger? Instance
    {
        get { lock (_lock) { return _instance; } }
    }

    /// <summary>
    /// Write an informational message.
    /// </summary>
    public void Info(string tag, string message) => Write("INFO", tag, message);

    /// <summary>
    /// Write a warning message.
    /// </summary>
    public void Warn(string tag, string message) => Write("WARN", tag, message);

    /// <summary>
    /// Write an error message, optionally with an exception.
    /// </summary>
    public void Error(string tag, string message, Exception? ex = null)
    {
        var text = ex is null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", tag, text);
    }

    private void Write(string level, string tag, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{level}] [{tag}] {message}";

        // Always echo to VS Output window during development
        Debug.WriteLine(line);

        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_userLogDirectory);
                var fileName = $"FxTradeConfirmation_{_userName}_{DateTime.Now:yyyyMMdd}.log";
                var filePath = Path.Combine(_userLogDirectory, fileName);
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Network share unreachable — don't crash the app
            Debug.WriteLine($"[FileLogger] Failed to write log: {ex.Message}");
        }
    }
}
