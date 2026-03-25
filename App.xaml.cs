using System.Windows;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using System.IO;

namespace FxTradeConfirmation;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dbService = new DatabaseService();
        await dbService.InitializeAsync();   // ← credentials loaded BEFORE anything else

        var emailService = new EmailService();

        // Create the STP ingest service using the same connection string as DatabaseService
        ITradeIngestService? ingestService = null;
        try
        {
            var connectionString = FxSharedConfig.AppDbConfig.GetConnectionString("trade_stp");
            ingestService = new TradeIngestService(connectionString);
        }
        catch { }

        // Create the recent trade service for Open Recent functionality
        IRecentTradeService? recentTradeService = null;
        try
        {
            const string recentTradesPath = @"\\nas-se11.fspa.myntet.se\MUREX\PROD\FX\FxTrades";
            recentTradeService = new RecentTradeService(recentTradesPath);
        }
        catch { }

        // Clipboard watcher — restricted to Bloomberg IB Manager only
        var clipboardWatcher = new ClipboardWatcher
        {
            SourceFilter = ["bplus64", "bplus"],
            WindowTitleFilter = ["IB Manager"]
        };

        const string settingsRoot = @"\\nas-se11.fspa.myntet.se\MUREX\PROD\FX\Settings\FxTradeConfirmation";
        var optionQueryFilter = new OptionQueryFilter(Path.Combine(settingsRoot, "Keywords.json"));
        var regexParser       = new OvmlBuilderAP3();
        var aiParser          = new OvmlBuilder(Path.Combine(settingsRoot, "Prompt.txt"));

        var viewModel = new MainViewModel(
            dbService, emailService, ingestService, recentTradeService,
            clipboardWatcher, optionQueryFilter, regexParser, aiParser);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}
