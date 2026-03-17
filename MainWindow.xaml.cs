using System.Windows;
using FxTradeConfirmation.ViewModels;

namespace FxTradeConfirmation;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }
}
