using System.IO;
using System.Text.Json;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Lightweight JSON-backed user settings, stored in the same AppData folder
/// as <c>MainWindowPos.json</c>.
/// </summary>
public sealed class AppSettings
{
    // ── Storage ──────────────────────────────────────────────────────────────

    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FxTradeConfirmation",
        "AppSettings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether passive clipboard monitoring is enabled.
    /// Persisted across restarts. Default: <see langword="true"/>.
    /// </summary>
    public bool ClipboardMonitorEnabled { get; set; } = true;

    // ── Load / Save ──────────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* corrupt file — fall back to defaults */ }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, _jsonOptions));
        }
        catch { /* non-critical — swallow silently */ }
    }
}
