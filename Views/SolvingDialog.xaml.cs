using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace FxTradeConfirmation.Views;

public partial class SolvingDialog : Window
{
    public string PayReceive { get; private set; } = string.Empty;
    public decimal TargetAmount { get; private set; }

    /// <summary>
    /// Callback that attempts to solve. Returns null on success, or an error message string.
    /// When set, the dialog stays open on validation errors instead of closing.
    /// </summary>
    public Func<decimal, string, string?>? SolveCallback { get; set; }

    /// <summary>
    /// Creates a SolvingDialog with a contextual title and unit label.
    /// </summary>
    /// <param name="isByAmount">True when solving for Premium Amount, false for Premium.</param>
    /// <param name="unitDisplay">Display string for the unit, e.g. "SEK pips" or "SEK".</param>
    public SolvingDialog(bool isByAmount, string unitDisplay)
    {
        InitializeComponent();

        TitleLabel.Text = isByAmount ? "Solve Premium Amount" : "Solve Premium";
        UnitLabel.Text = unitDisplay;

        Loaded += (_, _) =>
        {
            TargetAmountBox.Focus();
            Keyboard.Focus(TargetAmountBox);
        };
    }

    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseAmount())
        {
            PayReceive = "Pay";
            TrySubmit();
        }
    }

    private void Zero_Click(object sender, RoutedEventArgs e)
    {
        TargetAmount = 0;
        PayReceive = "ZeroCost";
        TrySubmit();
    }

    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseAmount())
        {
            PayReceive = "Receive";
            TrySubmit();
        }
    }

    /// <summary>
    /// Attempts to solve via the callback. If validation fails, shows the error
    /// in the dialog without closing. On success, closes with DialogResult = true.
    /// </summary>
    private void TrySubmit()
    {
        ClearError();

        if (SolveCallback != null)
        {
            var error = SolveCallback(TargetAmount, PayReceive);
            if (error != null)
            {
                ShowError(error);
                return;
            }
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorMessage.Text = string.Empty;
        ErrorMessage.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Comma → dot replacement for the target amount input.
    /// </summary>
    private void TargetAmountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "," && sender is System.Windows.Controls.TextBox tb)
        {
            e.Handled = true;
            int pos = tb.SelectionStart;
            int len = tb.SelectionLength;
            tb.Text = tb.Text[..pos] + "." + tb.Text[(pos + len)..];
            tb.CaretIndex = pos + 1;
        }
    }

    private bool TryParseAmount()
    {
        ClearError();
        var text = TargetAmountBox.Text.Replace(",", ".").Replace(" ", "");
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            TargetAmount = amount;
            return true;
        }
        TargetAmountBox.BorderBrush = FindResource("BorderErrorBrush") as System.Windows.Media.Brush;
        return false;
    }
}