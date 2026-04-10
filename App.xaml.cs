using FxSharedConfig;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using System.IO;
using System.Windows;

namespace FxTradeConfirmation;

public partial class App : Application
{
    private static readonly string _diagLogPath =
        Path.Combine(AppContext.BaseDirectory, "startup-diag.log");

    private static void DiagLog(string message)
    {
        try
        {
            File.AppendAllText(
                _diagLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* if even this fails, there is nothing we can do */ }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DiagLog($"--- Startup begin  user={Environment.UserName}  machine={Environment.MachineName} ---");
        DiagLog($"BaseDirectory : {AppContext.BaseDirectory}");

        try
        {
            DiagLog("Resolving AppPaths.Settings...");
            string settingsRoot = Path.Combine(AppPaths.Settings, "FxTradeConfirmation");
            DiagLog($"settingsRoot  : {settingsRoot}");

            DiagLog("Initializing FileLogger...");
            FileLogger.Initialize(Path.Combine(settingsRoot, "Logs"));
            FileLogger.Instance?.Info("App", "Application starting");
            DiagLog("FileLogger OK.");

            // ── Kick off all slow network I/O in parallel, off the UI thread ──
            var dbService = new DatabaseService();

            var dbInitTask = dbService.InitializeAsync();

            var ingestTask = Task.Run<ITradeIngestService?>(() =>
            {
                try { return new TradeIngestService(AppDbConfig.GetConnectionString("trade_stp")); }
                catch (Exception ex) { FileLogger.Instance?.Error("App", "TradeIngestService unavailable", ex); return null; }
            });

            var aiParserTask = Task.Run<IOvmlParser>(() =>
            {
                try { return new OvmlBuilder(Path.Combine(settingsRoot, "Prompt.txt")); }
                catch (Exception ex) { FileLogger.Instance?.Error("App", "OvmlBuilder (AI parser) unavailable", ex); return new OvmlBuilderAP3(); }
            });

            // ── Things that don't need the network can be created immediately ──
            var emailService = new EmailService();
            var regexParser = new OvmlBuilderAP3();
            var bloombergPaster = new BloombergPaster();
            var clipboardWatcher = new ClipboardWatcher
            {
                SourceFilter = ["bplus64", "bplus"],
                WindowTitleFilter = ["IB Manager"]
            };
            var optionQueryFilter = new OptionQueryFilter(Path.Combine(settingsRoot, "Keywords.json"));

            IRecentTradeService? recentTradeService = null;
            try
            {
                recentTradeService = new RecentTradeService(Path.Combine(settingsRoot, "FxTrades"));
            }
            catch (Exception ex)
            {
                FileLogger.Instance?.Error("App", "RecentTradeService unavailable", ex);
            }

            // ── Await all parallel tasks together ──
            await Task.WhenAll(dbInitTask, ingestTask, aiParserTask);

            var ingestService = await ingestTask;
            var aiParser = await aiParserTask;

            var viewModel = new MainViewModel(
                dbService, emailService, ingestService, recentTradeService,
                clipboardWatcher, optionQueryFilter, regexParser, aiParser,
                bloombergPaster);

            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            clipboardWatcher.Start();

            FileLogger.Instance?.Info("App", "Application started successfully");
            DiagLog("--- Startup complete ---");
        }
        catch (Exception ex)
        {
            DiagLog($"FATAL: {ex.GetType().Name}: {ex.Message}");
            DiagLog($"       {ex.StackTrace}");

            FileLogger.Instance?.Error("App", "Fatal startup error", ex);

            MessageBox.Show(
                $"A fatal error occurred during startup and the application cannot start:\n\n{ex.Message}",
                "FxTradeConfirmation — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(exitCode: 1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DatabaseService.FlushPendingNetworkWrite();
        FileLogger.Instance?.Info("App", $"Application exiting (exit code {e.ApplicationExitCode}).");
        DiagLog($"--- Exit (code {e.ApplicationExitCode}) ---");
        base.OnExit(e);
    }
}
