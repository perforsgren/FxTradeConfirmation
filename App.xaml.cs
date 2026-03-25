using System.Windows;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;

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
        catch
        {
            // Ingest service unavailable — Save will show an error but the app still starts
        }

        // Create the recent trade service for Open Recent functionality
        IRecentTradeService? recentTradeService = null;
        try
        {
            const string recentTradesPath = @"\\nas-se11.fspa.myntet.se\MUREX\PROD\FX\FxTrades";
            recentTradeService = new RecentTradeService(recentTradesPath);
        }
        catch
        {
            // Recent trade service unavailable — Open Recent will show an error
        }

        var viewModel = new MainViewModel(dbService, emailService, ingestService, recentTradeService);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}
