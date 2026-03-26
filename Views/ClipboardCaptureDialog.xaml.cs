using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;

namespace FxTradeConfirmation.Views;

public partial class ClipboardCaptureDialog : Window
{
    public ClipboardCaptureAction Result { get; private set; } = ClipboardCaptureAction.Reject;

    // Kept so MainWindow can read back the (possibly user-toggled) legs
    public IReadOnlyList<LegRow> ParsedLegs { get; private set; } = [];

    /// <summary>
    /// The current OVML string, rebuilt whenever the user toggles Buy/Sell or Call/Put.
    /// MainWindow reads this for Bloomberg paste instead of the original static string.
    /// </summary>
    public string CurrentOvml { get; private set; } = string.Empty;

    // Original OvmlLegs — needed to rebuild OVML with toggled values
    private readonly IReadOnlyList<OvmlLeg> _originalLegs;

    public ClipboardCaptureDialog(
        ClipboardChangedEventArgs e,
        string ovml,
        IReadOnlyList<OvmlLeg> legs,
        bool parsedByAi)
    {
        InitializeComponent();

        _originalLegs = legs;
        CurrentOvml = ovml;

        // ── Parse status badge ─────────────────────────────────────────────
        if (legs.Count > 0)
        {
            var method = parsedByAi ? "AI" : "Regex";
            ParseStatusLabel.Text = $"✓  Parsed via {method}  —  {legs.Count} leg(s)";
            ParseStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
            ParseStatusBorder.BorderBrush = (Brush)Application.Current.Resources["PositiveGreenBrush"];
            ParseStatusBorder.BorderThickness = new Thickness(1);
            ParseStatusLabel.Foreground = (Brush)Application.Current.Resources["PositiveGreenBrush"];
        }
        else
        {
            ParseStatusLabel.Text = "⚠  Could not parse — no legs extracted";
            ParseStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
            ParseStatusBorder.BorderBrush = (Brush)Application.Current.Resources["NegativeRedBrush"];
            ParseStatusBorder.BorderThickness = new Thickness(1);
            ParseStatusLabel.Foreground = (Brush)Application.Current.Resources["NegativeRedBrush"];
            PopulateButton.IsEnabled = false;
            BloombergButton.IsEnabled = false;
            BothButton.IsEnabled = false;
        }

        // ── Summary ────────────────────────────────────────────────────────
        var first = legs.Count > 0 ? legs[0] : null;
        PairLabel.Text = first?.Pair ?? "—";
        ExpiryLabel.Text = first?.Expiry is { Length: > 0 } exp ? FormatExpiry(exp) : "—";
        SpotLabel.Text = first?.Spot is { Length: > 0 } sp ? sp : "—";

        // ── Legs ───────────────────────────────────────────────────────────
        ParsedLegs = legs.Select((l, i) => new LegRow(i + 1, l)).ToList();
        LegsList.ItemsSource = ParsedLegs;

        // ── OVML ───────────────────────────────────────────────────────────
        OvmlLabel.Text = string.IsNullOrEmpty(ovml) ? "(no OVML generated)" : ovml;
    }

    // ── Toggle handlers ───────────────────────────────────────────────────

    private void ToggleBuySell_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is LegRow row)
        {
            row.ToggleBuySell();
            RebuildOvmlFromCurrentLegs();
        }
    }

    private void ToggleCallPut_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is LegRow row)
        {
            row.ToggleCallPut();
            RebuildOvmlFromCurrentLegs();
        }
    }

    /// <summary>
    /// Rebuilds the OVML string from the current (possibly toggled) leg rows
    /// and updates both the displayed label and <see cref="CurrentOvml"/>.
    /// </summary>
    private void RebuildOvmlFromCurrentLegs()
    {
        var updatedLegs = ParsedLegs
            .Select((row, i) => row.ToOvmlLeg(_originalLegs[i]))
            .ToList();

        CurrentOvml = OvmlBuilderAP3.RebuildOvml(updatedLegs);
        OvmlLabel.Text = string.IsNullOrEmpty(CurrentOvml) ? "(no OVML generated)" : CurrentOvml;
    }

    // ── OVML context menu ─────────────────────────────────────────────────

    private void OvmlCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OvmlLabel.Text))
            Clipboard.SetText(OvmlLabel.Text);
    }

    // ── Action buttons ────────────────────────────────────────────────────

    private void Populate_Click(object sender, RoutedEventArgs e)
    {
        Result = ClipboardCaptureAction.PopulateUi;
        Close();
    }

    private void Bloomberg_Click(object sender, RoutedEventArgs e)
    {
        Result = ClipboardCaptureAction.OpenInBloomberg;
        Close();
    }

    private void Both_Click(object sender, RoutedEventArgs e)
    {
        Result = ClipboardCaptureAction.Both;
        Close();
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        Result = ClipboardCaptureAction.Reject;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = ClipboardCaptureAction.Reject; Close(); }
        else if (e.Key == Key.Enter) { Result = ClipboardCaptureAction.PopulateUi; Close(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatExpiry(string raw)
    {
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        if (DateTime.TryParseExact(raw, "MM/dd/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        return raw.ToUpperInvariant();
    }

    // ── LegRow — mutable, notifies UI on toggle ───────────────────────────

    public sealed class LegRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string LegNumber { get; }
        public string Strike { get; }
        public string NotionalDisplay { get; }

        private readonly bool _putCallUnknown;

        private string _buySell;
        private string _putCall;

        public string BuySell
        {
            get => _buySell;
            private set { _buySell = value; Notify(nameof(BuySell)); Notify(nameof(BuySellBrush)); }
        }

        public string PutCall
        {
            get => _putCall;
            private set { _putCall = value; Notify(nameof(PutCall)); Notify(nameof(CallPutBrush)); }
        }

        public Brush BuySellBrush => BuySell == "SELL"
            ? (Brush)(Application.Current.Resources["NegativeRedDarkBrush"] ?? Brushes.DarkRed)
            : (Brush)(Application.Current.Resources["PositiveGreenDarkBrush"] ?? Brushes.DarkGreen);

        public Brush CallPutBrush => PutCall == "PUT"
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x1D, 0x5A))
            : PutCall == "—"
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x44, 0x44, 0x44))
                : new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x3A, 0x6E));

        public LegRow(int number, OvmlLeg leg)
        {
            LegNumber = $"#{number}";

            _buySell = leg.BuySell.StartsWith("S", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";

            _putCallUnknown = string.IsNullOrWhiteSpace(leg.PutCall);
            _putCall = _putCallUnknown ? "—" : leg.PutCall.ToUpperInvariant();

            Strike = string.IsNullOrWhiteSpace(leg.Strike) ? "—" : leg.Strike;

            NotionalDisplay = leg.Notional >= 1_000_000
                ? $"{leg.Notional / 1_000_000:0.##}M"
                : leg.Notional > 0
                    ? leg.Notional.ToString("N0", CultureInfo.InvariantCulture)
                    : "—";
        }

        /// <summary>Cycles BUY → SELL → BUY.</summary>
        public void ToggleBuySell() =>
            BuySell = BuySell == "BUY" ? "SELL" : "BUY";

        /// <summary>Cycles — → CALL → PUT → CALL.</summary>
        public void ToggleCallPut() =>
            PutCall = PutCall switch
            {
                "—"    => "CALL",
                "CALL" => "PUT",
                _      => "CALL"
            };

        /// <summary>Converts back to OvmlLeg with any user-toggled values applied.</summary>
        public OvmlLeg ToOvmlLeg(OvmlLeg original) => original with
        {
            BuySell = BuySell == "SELL" ? "Sell" : "Buy",
            PutCall = PutCall switch
            {
                "CALL" => "Call",
                "PUT"  => "Put",
                _      => original.PutCall
            }
        };

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}