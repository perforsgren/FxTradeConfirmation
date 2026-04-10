using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace FxTradeConfirmation.Views;

/// <summary>
/// Dark-themed dialog shown when an expiry date falls on a weekend.
/// <see cref="ChosenAction"/> indicates the user's choice.
/// </summary>
public partial class WeekendRollDialog : Window
{
    public enum RollAction { Keep, Previous, Next }

    public RollAction ChosenAction { get; private set; } = RollAction.Keep;

    public WeekendRollDialog(int legNumber, DateTime expiryDate)
    {
        InitializeComponent();

        var dayName = expiryDate.ToString("dddd", CultureInfo.InvariantCulture);
        LegLine.Text = $"Leg {legNumber}:  Expiry {expiryDate:yyyy-MM-dd} ({dayName})";
        FallsLine.Text = "falls on a weekend and cannot be booked.";
        ChooseLine.Text = "Choose how to roll the date:";
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = RollAction.Previous;
        Close();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = RollAction.Next;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = RollAction.Keep;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ChosenAction = RollAction.Keep;
            Close();
        }
    }
}
