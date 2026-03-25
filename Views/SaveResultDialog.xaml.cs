using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;

namespace FxTradeConfirmation.Views;

public partial class SaveResultDialog : Window
{
    public SaveResultDialog(
        IReadOnlyList<SaveResultItem> items,
        int successCount,
        int failCount)
    {
        InitializeComponent();

        // Summary line
        if (failCount == 0)
        {
            SummaryLabel.Text = $"✓ All {successCount} trade(s) saved successfully";
            SummaryLabel.Foreground = (Brush)FindResource("PositiveGreenBrush");
        }
        else if (successCount == 0)
        {
            SummaryLabel.Text = $"✗ All {failCount} trade(s) failed";
            SummaryLabel.Foreground = (Brush)FindResource("NegativeRedBrush");
        }
        else
        {
            SummaryLabel.Text = $"⚠ {successCount} saved, {failCount} failed";
            SummaryLabel.Foreground = (Brush)FindResource("WarningAmberBrush");
        }

        ResultsList.ItemsSource = items;

        Loaded += (_, _) => Keyboard.Focus(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}

/// <summary>
/// View model for a single row in the save results dialog.
/// </summary>
public class SaveResultItem
{
    public string StatusIcon { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public Brush DetailBrush { get; init; } = Brushes.White;

    public static SaveResultItem FromResult(
        TradeSubmitResult result,
        string label,
        Brush successBrush,
        Brush failBrush)
    {
        if (result.Success)
        {
            return new SaveResultItem
            {
                StatusIcon = "✓",
                Label = label,
                Detail = result.MessageInId.HasValue
                    ? $"MessageInId: {result.MessageInId}"
                    : "Saved",
                DetailBrush = successBrush
            };
        }

        return new SaveResultItem
        {
            StatusIcon = "✗",
            Label = label,
            Detail = result.ErrorMessage ?? "Unknown error",
            DetailBrush = failBrush
        };
    }
}