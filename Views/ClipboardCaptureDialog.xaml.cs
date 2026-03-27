using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FxTradeConfirmation.Views;

public partial class ClipboardCaptureDialog : Window
{
    public ClipboardCaptureAction Result { get; private set; } = ClipboardCaptureAction.Reject;

    public IReadOnlyList<LegRow> ParsedLegs { get; private set; } = [];

    public string CurrentOvml { get; private set; } = string.Empty;

    private readonly IReadOnlyList<OvmlLeg> _originalLegs;

    // Offset from owner's top-left corner, set once when dialog is shown
    private double _offsetLeft;
    private double _offsetTop;

    public ClipboardCaptureDialog(
        ClipboardChangedEventArgs e,
        string ovml,
        IReadOnlyList<OvmlLeg> legs,
        bool parsedByAi)
    {
        InitializeComponent();

        _originalLegs = legs;
        CurrentOvml = ovml;

        // ── Parse status badge ────────────────────────────────────────────
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

        var first = legs.Count > 0 ? legs[0] : null;
        PairLabel.Text = first?.Pair ?? "—";
        ExpiryLabel.Text = ResolveExpiryDisplay(legs);
        SpotLabel.Text = first?.Spot is { Length: > 0 } sp ? sp.Replace(',', '.') : "—";

        ParsedLegs = legs.Select((l, i) => new LegRow(i + 1, l)).ToList();
        LegsList.ItemsSource = ParsedLegs;

        OvmlLabel.Text = string.IsNullOrEmpty(ovml) ? "(no OVML generated)" : ovml;

        Loaded += OnLoaded;
    }

    // ── Owner tracking ────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Owner is null)
            return;

        // Compute offset once so the dialog sits centred over the owner
        _offsetLeft = (Owner.Width - ActualWidth) / 2;
        _offsetTop = (Owner.Height - ActualHeight) / 2;

        SnapToOwner();

        Owner.LocationChanged += OnOwnerLocationChanged;
        Owner.Closed += OnOwnerClosed;
    }

    private void OnOwnerLocationChanged(object? sender, EventArgs e) => SnapToOwner();

    private void OnOwnerClosed(object? sender, EventArgs e) => Close();

    private void SnapToOwner()
    {
        if (Owner is null)
            return;

        Left = Owner.Left + _offsetLeft;
        Top  = Owner.Top  + _offsetTop;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Owner is not null)
        {
            Owner.LocationChanged -= OnOwnerLocationChanged;
            Owner.Closed          -= OnOwnerClosed;
        }
        base.OnClosed(e);
    }

    // ── Header mouse-down: drag disabled, dialog is locked to owner ───────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Intentionally empty — dialog follows the owner window instead of
        // being draggable on its own.
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

    private void RebuildOvmlFromCurrentLegs()
    {
        var updatedLegs = ParsedLegs
            .Select((row, i) => row.ToOvmlLeg(_originalLegs[i]))
            .ToList();

        CurrentOvml = OvmlBuilderAP3.RebuildOvml(updatedLegs);
        OvmlLabel.Text = string.IsNullOrEmpty(CurrentOvml) ? "(no OVML generated)" : CurrentOvml;
    }

    private void OvmlCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OvmlLabel.Text))
            Clipboard.SetText(OvmlLabel.Text);
    }

    private void Populate_Click(object sender, RoutedEventArgs e) { Result = ClipboardCaptureAction.PopulateUi; Close(); }
    private void Bloomberg_Click(object sender, RoutedEventArgs e) { Result = ClipboardCaptureAction.OpenInBloomberg; Close(); }
    private void Both_Click(object sender, RoutedEventArgs e) { Result = ClipboardCaptureAction.Both; Close(); }
    private void Reject_Click(object sender, RoutedEventArgs e) { Result = ClipboardCaptureAction.Reject; Close(); }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = ClipboardCaptureAction.Reject; Close(); }
        else if (e.Key == Key.Enter) { Result = ClipboardCaptureAction.PopulateUi; Close(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a single formatted expiry when all legs share the same date,
    /// otherwise "MIXED" to signal that legs have different expiry dates.
    /// </summary>
    private static string ResolveExpiryDisplay(IReadOnlyList<OvmlLeg> legs)
    {
        if (legs.Count == 0)
            return "—";

        var distinctExpiries = legs
            .Select(l => l.Expiry)
            .Where(exp => !string.IsNullOrWhiteSpace(exp))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinctExpiries.Count switch
        {
            0 => "—",
            1 => FormatExpiry(distinctExpiries[0]),
            _ => "MIXED"
        };
    }

    private static string FormatExpiry(string raw)
    {
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        if (DateTime.TryParseExact(raw, "MM/dd/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        return raw.ToUpperInvariant();
    }

    private static string FormatStrike(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "—") return "—";
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        {
            int existingDecimals = raw.Contains('.') ? raw.Length - raw.IndexOf('.') - 1 : 0;
            return d.ToString($"F{Math.Max(2, existingDecimals)}", CultureInfo.InvariantCulture);
        }
        return raw;
    }

    // ── LegRow ────────────────────────────────────────────────────────────

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
            Strike = FormatStrike(leg.Strike);
            NotionalDisplay = leg.Notional >= 1_000_000
                ? $"{leg.Notional / 1_000_000:0.##}M"
                : leg.Notional > 0 ? leg.Notional.ToString("N0", CultureInfo.InvariantCulture) : "—";
        }

        public void ToggleBuySell() => BuySell = BuySell == "BUY" ? "SELL" : "BUY";

        public void ToggleCallPut() =>
            PutCall = PutCall switch { "—" => "CALL", "CALL" => "PUT", _ => "CALL" };

        public OvmlLeg ToOvmlLeg(OvmlLeg original) => original with
        {
            BuySell = BuySell == "SELL" ? "Sell" : "Buy",
            PutCall = PutCall switch { "CALL" => "Call", "PUT" => "Put", _ => original.PutCall }
        };

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}