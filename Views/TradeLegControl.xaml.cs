using FxTradeConfirmation.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FxTradeConfirmation.Views;

public partial class TradeLegControl : UserControl
{
    public TradeLegControl()
    {
        InitializeComponent();
    }

    private TradeLegViewModel? VM => DataContext as TradeLegViewModel;

    private static void ReplaceCommaWithDot(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ",")
        {
            e.Handled = true;
            if (sender is TextBox tb)
            {
                int selStart = tb.SelectionStart;
                int selLen = tb.SelectionLength;
                string before = tb.Text[..selStart];
                string after = tb.Text[(selStart + selLen)..];
                tb.Text = before + "." + after;
                tb.CaretIndex = selStart + 1;
            }
        }
    }

    // --- Strike ---
    private void StrikeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void StrikeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyStrikeInput(tb.Text);
    }
    private void StrikeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyStrikeInput(tb.Text);
    }

    // --- Notional ---
    private void NotionalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void NotionalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyNotionalInput(tb.Text);
    }
    private void NotionalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyNotionalInput(tb.Text);
    }

    // --- Premium ---
    private void PremiumBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void PremiumBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyPremiumInput(tb.Text);
    }
    private void PremiumBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyPremiumInput(tb.Text);
    }

    // --- Premium Amount ---
    private void PremiumAmountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void PremiumAmountBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyPremiumAmountInput(tb.Text);
    }
    private void PremiumAmountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyPremiumAmountInput(tb.Text);
    }

    // --- Expiry ---
    private void ExpiryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyExpiryInput(tb.Text);
    }
    private void ExpiryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyExpiryInput(tb.Text);
    }

    // --- Margin ---
    private void MarginBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void MarginBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyMarginInput(tb.Text);
    }
    private void MarginBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyMarginInput(tb.Text);
    }

    // --- Execution Time ---
    private void ExecutionTimeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyExecutionTimeInput(tb.Text);
    }
    private void ExecutionTimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyExecutionTimeInput(tb.Text);
    }

    // --- Hedge Notional ---
    private void HedgeNotionalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void HedgeNotionalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyHedgeNotionalInput(tb.Text);
    }
    private void HedgeNotionalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyHedgeNotionalInput(tb.Text);
    }

    // --- Hedge Rate ---
    private void HedgeRateBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => ReplaceCommaWithDot(sender, e);
    private void HedgeRateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) VM?.ApplyHedgeRateInput(tb.Text);
    }
    private void HedgeRateBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb) VM?.ApplyHedgeRateInput(tb.Text);
    }
}
