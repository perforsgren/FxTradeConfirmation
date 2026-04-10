using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Views;

public partial class RestoreDraftDialog : Window
{
    /// <summary>True when the user explicitly chose Restore.</summary>
    public bool ShouldRestore { get; private set; }

    /// <summary>
    /// True when the dialog was closed without an explicit choice
    /// (e.g. Alt+F4, window X button, or app shutdown).
    /// In that case the draft file must not be deleted.
    /// </summary>
    public bool WasDismissed { get; private set; } = true;

    /// <summary>
    /// True when the dialog was auto-closed because a clipboard parse cycle
    /// started. The draft should be discarded (deleted) in this case.
    /// </summary>
    public bool ClosedByParsing { get; private set; }

    public RestoreDraftDialog(DraftData draft)
    {
        InitializeComponent();

        TimestampLine.Text = $"Saved at  {draft.SavedAt:HH:mm:ss}  ·  {draft.Legs.Count} leg{(draft.Legs.Count == 1 ? "" : "s")}";

        CounterpartLine.Text = string.IsNullOrWhiteSpace(draft.Counterpart)
            ? string.Empty
            : $"Counterpart  {draft.Counterpart}";

        LegSummaries.ItemsSource = BuildSummaries(draft.Legs);
    }

    /// <summary>
    /// Closes the dialog as if the user chose Discard, but marks the close
    /// as triggered by an incoming parse cycle so the caller can distinguish
    /// the two scenarios.
    /// Must be called on the UI thread.
    /// </summary>
    public void CloseForParsing()
    {
        ShouldRestore = false;
        WasDismissed = false;
        ClosedByParsing = true;
        Close();
    }

    private static List<LegSummaryRow> BuildSummaries(IReadOnlyList<TradeLeg> legs)
    {
        var inv = CultureInfo.InvariantCulture;
        var rows = new List<LegSummaryRow>(legs.Count);

        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];

            string strikeExpiry = leg.Strike.HasValue && leg.ExpiryDate.HasValue
                ? $"@ {leg.Strike.Value.ToString("G6", inv)}  exp {leg.ExpiryDate.Value:dd-MMM-yy}"
                : leg.Strike.HasValue
                    ? $"@ {leg.Strike.Value.ToString("G6", inv)}"
                    : leg.ExpiryDate.HasValue
                        ? $"exp {leg.ExpiryDate.Value:dd-MMM-yy}"
                        : "—";

            string notional = leg.Notional.HasValue
                ? $"{FormatNotional(leg.Notional.Value)} {leg.NotionalCurrency}"
                : string.Empty;

            rows.Add(new LegSummaryRow(
                LegLabel: $"LEG {i + 1}",
                Direction: $"{leg.BuySell} {leg.CallPut}".ToUpperInvariant(),
                IsBuy: leg.BuySell == BuySell.Buy,
                Pair: leg.CurrencyPair,
                StrikeExpiry: strikeExpiry,
                Notional: notional));
        }

        return rows;
    }

    private static string FormatNotional(decimal value) =>
        value >= 1_000_000
            ? $"{value / 1_000_000:G4}M"
            : value >= 1_000
                ? $"{value / 1_000:G4}K"
                : value.ToString("N0", CultureInfo.InvariantCulture);

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        ShouldRestore = true;
        WasDismissed = false;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        ShouldRestore = false;
        WasDismissed = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ShouldRestore = true; WasDismissed = false; Close(); }
        else if (e.Key == Key.Escape) { ShouldRestore = false; WasDismissed = false; Close(); }
    }
}

/// <summary>
/// Pure display model for one leg row — no WPF types.
/// Colour is resolved in XAML via DataTrigger on <see cref="IsBuy"/>.
/// </summary>
internal sealed record LegSummaryRow(
    string LegLabel,
    string Direction,
    bool IsBuy,
    string Pair,
    string StrikeExpiry,
    string Notional);
