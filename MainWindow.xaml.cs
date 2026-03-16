using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FxTradeConfirmation.ViewModels;

namespace FxTradeConfirmation;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    // --- Distributor event handlers ---

    private void DistCounterpart_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string val)
            ViewModel.SetAllCounterpartCommand.Execute(val);
    }

    private void DistCurrencyPair_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string val)
            ViewModel.SetAllCurrencyPairCommand.Execute(val);
    }

    private void DistCut_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string val)
            ViewModel.SetAllCutCommand.Execute(val);
    }

    private void DistStrike_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
            ViewModel.SetAllStrikeCommand.Execute(tb.Text);
    }

    private void DistExpiry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
            ViewModel.SetAllExpiryCommand.Execute(tb.Text);
    }

    private void DistNotional_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
            ViewModel.SetAllNotionalCommand.Execute(tb.Text);
    }
}
