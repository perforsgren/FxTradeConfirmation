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
        var viewModel = new MainViewModel(dbService, emailService);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}
