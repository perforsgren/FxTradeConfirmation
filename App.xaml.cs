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

        try
        {
            var dbService = new DatabaseService();
            await dbService.InitializeAsync();

            var emailService = new EmailService();

            ITradeIngestService? ingestService = null;
            try
            {
                var connectionString = FxSharedConfig.AppDbConfig.GetConnectionString("trade_stp");
                ingestService = new TradeIngestService(connectionString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] TradeIngestService unavailable: {ex.Message}");
            }

            IRecentTradeService? recentTradeService = null;
            try
            {
                const string recentTradesPath = @"\\nas-se11.fspa.myntet.se\MUREX\PROD\FX\FxTrades";
                recentTradeService = new RecentTradeService(recentTradesPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] RecentTradeService unavailable: {ex.Message}");
            }

            var clipboardWatcher = new ClipboardWatcher
            {
                SourceFilter = ["bplus64", "bplus"],
                WindowTitleFilter = ["IB Manager"]
            };

            const string settingsRoot = @"\\nas-se11.fspa.myntet.se\MUREX\PROD\FX\Settings\FxTradeConfirmation";
            var optionQueryFilter = new OptionQueryFilter(Path.Combine(settingsRoot, "Keywords.json"));
            var regexParser       = new OvmlBuilderAP3();
            var aiParser          = new OvmlBuilder(Path.Combine(settingsRoot, "Prompt.txt"));
            var bloombergPaster   = new BloombergPaster();

            var viewModel = new MainViewModel(
                dbService, emailService, ingestService, recentTradeService,
                clipboardWatcher, optionQueryFilter, regexParser, aiParser,
                bloombergPaster);

            var window = new MainWindow { DataContext = viewModel };
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ett fel uppstod vid uppstart och applikationen kan inte starta:\n\n{ex.Message}",
                "FxTradeConfirmation — startfel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(exitCode: 1);
        }
    }
}
