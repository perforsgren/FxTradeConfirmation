using System.Globalization;
using System.Windows;

namespace FxTradeConfirmation.Views;

public partial class SolvingDialog : Window
{
    public string PayReceive { get; private set; } = string.Empty;
    public decimal TargetAmount { get; private set; }

    public SolvingDialog()
    {
        InitializeComponent();
        TargetAmountBox.Focus();
    }

    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseAmount())
        {
            PayReceive = "Pay";
            DialogResult = true;
        }
    }

    private void Zero_Click(object sender, RoutedEventArgs e)
    {
        TargetAmount = 0;
        PayReceive = "ZeroCost";
        DialogResult = true;
    }

    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseAmount())
        {
            PayReceive = "Receive";
            DialogResult = true;
        }
    }

    private bool TryParseAmount()
    {
        if (decimal.TryParse(TargetAmountBox.Text, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var amount))
        {
            TargetAmount = amount;
            return true;
        }
        TargetAmountBox.BorderBrush = FindResource("BorderErrorBrush") as System.Windows.Media.Brush;
        return false;
    }
}
