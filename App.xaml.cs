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

        var viewModel = new MainViewModel(dbService, emailService, ingestService);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}
