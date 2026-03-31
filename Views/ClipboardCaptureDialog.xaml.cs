using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace FxTradeConfirmation.Views;

public partial class ClipboardCaptureDialog : Window
{
    public ClipboardCaptureAction Result { get; private set; } = ClipboardCaptureAction.Reject;

    public IReadOnlyList<LegRow> ParsedLegs { get; private set; } = [];

    public string CurrentOvml { get; private set; } = string.Empty;

    private readonly IReadOnlyList<OvmlLeg> _originalLegs;
    private readonly bool _mixedExpiry;

    // Header popup (single-expiry mode)
    private Popup?       _headerPopup;
    private WpfCalendar? _headerCalendar;

    // Offset from owner's top-left corner
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
        CurrentOvml   = ovml;

        // ── Parse status badge ────────────────────────────────────────────
        if (legs.Count > 0)
        {
            var method = parsedByAi ? "AI" : "Regex";
            ParseStatusLabel.Text             = $"✓  Parsed via {method}  —  {legs.Count} leg(s)";
            ParseStatusBorder.Background      = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
            ParseStatusBorder.BorderBrush     = (Brush)Application.Current.Resources["PositiveGreenBrush"];
            ParseStatusBorder.BorderThickness = new Thickness(1);
            ParseStatusLabel.Foreground       = (Brush)Application.Current.Resources["PositiveGreenBrush"];
        }
        else
        {
            ParseStatusLabel.Text             = "⚠  Could not parse — no legs extracted";
            ParseStatusBorder.Background      = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
            ParseStatusBorder.BorderBrush     = (Brush)Application.Current.Resources["NegativeRedBrush"];
            ParseStatusBorder.BorderThickness = new Thickness(1);
            ParseStatusLabel.Foreground       = (Brush)Application.Current.Resources["NegativeRedBrush"];
            PopulateButton.IsEnabled  = false;
            BloombergButton.IsEnabled = false;
            BothButton.IsEnabled      = false;
        }

        var distinctExpiries = legs
            .Select(l => l.Expiry)
            .Where(exp => !string.IsNullOrWhiteSpace(exp))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _mixedExpiry = distinctExpiries.Count > 1;

        var first = legs.Count > 0 ? legs[0] : null;
        PairLabel.Text   = first?.Pair ?? "—";
        SpotLabel.Text   = first?.Spot is { Length: > 0 } sp ? sp.Replace(',', '.') : "—";
        ExpiryLabel.Text = _mixedExpiry ? "MIXED" : ResolveExpiryDisplay(legs);

        ParsedLegs = legs.Select((l, i) => new LegRow(i + 1, l)).ToList();
        LegsList.ItemsSource = ParsedLegs;

        LegExpiryList.Visibility  = _mixedExpiry ? Visibility.Visible : Visibility.Collapsed;
        LegExpiryList.ItemsSource = _mixedExpiry ? ParsedLegs : null;

        OvmlLabel.Text = string.IsNullOrEmpty(ovml) ? "(no OVML generated)" : ovml;

        if (!_mixedExpiry)
            BuildHeaderPicker();

        Loaded += OnLoaded;
    }

    // ── Picker factory ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Calendar whose day/month-button styles, CalendarItem template,
    /// and DayTitle template are loaded from the XAML Border.Resources.
    /// </summary>
    private WpfCalendar CreateCalendar(DateTime? selected)
    {
        var cal = new WpfCalendar
        {
            SelectionMode   = CalendarSelectionMode.SingleDate,
            SelectedDate    = selected,
            DisplayDate     = selected ?? DateTime.Today,
            Language        = System.Windows.Markup.XmlLanguage.GetLanguage("en-US"),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground      = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            Margin          = new Thickness(0),
        };

        // Styles defined in XAML Border.Resources — resolved via the live element tree
        cal.CalendarDayButtonStyle = (Style)ExpiryStack.FindResource("CalDayButton");
        cal.CalendarButtonStyle    = (Style)ExpiryStack.FindResource("CalMonthButton");
        cal.CalendarItemStyle      = (Style)ExpiryStack.FindResource("CalItemHiddenHeader");

        // DayTitle template — weekday header row (M T W T F S S)
        var dayTitleTemplate = (DataTemplate)ExpiryStack.FindResource("CalDayTitleTemplate");
        cal.Resources[CalendarItem.DayTitleTemplateResourceKey] = dayTitleTemplate;

        return cal;
    }

    /// <summary>
    /// Wraps a Calendar in a popup card with an explicit nav row (◀ MMM YYYY ▶)
    /// that is always visible — not dependent on hover state.
    /// </summary>
    private static Popup CreatePopup(FrameworkElement target, WpfCalendar cal)
    {
        // ── Month/year label ──────────────────────────────────────────────
        var monthLabel = new TextBlock
        {
            Text                = cal.DisplayDate.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            Foreground          = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            FontSize            = 12,
            FontWeight          = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        cal.DisplayDateChanged += (_, _) =>
            monthLabel.Text = cal.DisplayDate.ToString("MMM yyyy", CultureInfo.InvariantCulture);

        // ── Nav button style (from XAML resources, removes default chrome) ─
        var navStyle = (Style)target.FindResource("CalNavButton");

        // ── Nav button factory ────────────────────────────────────────────
        Button MakeNavButton(string pathData, string tooltip)
        {
            var icon = new Path
            {
                Data               = Geometry.Parse(pathData),
                Stroke             = (Brush)Application.Current.Resources["TextMutedBrush"],
                StrokeThickness    = 1.8,
                StrokeLineJoin     = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                Width              = 10,
                Height             = 10,
                Stretch            = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };

            return new Button
            {
                Content = icon,
                ToolTip = tooltip,
                Style   = navStyle,
            };
        }

        var prevBtn = MakeNavButton("M9,4 L4,8 L9,12", "Previous month");
        var nextBtn = MakeNavButton("M4,4 L9,8 L4,12", "Next month");

        prevBtn.Click += (_, _) =>
        {
            var d = cal.DisplayDate;
            cal.DisplayDate = d.Month == 1
                ? new DateTime(d.Year - 1, 12, 1)
                : new DateTime(d.Year, d.Month - 1, 1);
        };
        nextBtn.Click += (_, _) =>
        {
            var d = cal.DisplayDate;
            cal.DisplayDate = d.Month == 12
                ? new DateTime(d.Year + 1, 1, 1)
                : new DateTime(d.Year, d.Month + 1, 1);
        };

        // ── Nav row ───────────────────────────────────────────────────────
        var navGrid = new Grid { Margin = new Thickness(4, 4, 4, 2) };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(prevBtn,    0);
        Grid.SetColumn(monthLabel, 1);
        Grid.SetColumn(nextBtn,    2);
        navGrid.Children.Add(prevBtn);
        navGrid.Children.Add(monthLabel);
        navGrid.Children.Add(nextBtn);

        // ── Outer grid ────────────────────────────────────────────────────
        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(navGrid, 0);
        Grid.SetRow(cal,     1);
        outerGrid.Children.Add(navGrid);
        outerGrid.Children.Add(cal);

        // ── Card ──────────────────────────────────────────────────────────
        var card = new System.Windows.Controls.Border
        {
            Background      = (Brush)Application.Current.Resources["BgCardBrush"],
            BorderBrush     = (Brush)Application.Current.Resources["BorderDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(4),
            Effect = new DropShadowEffect
            {
                Color       = Colors.Black,
                BlurRadius  = 12,
                ShadowDepth = 4,
                Opacity     = 0.4,
            },
            Child = outerGrid,
        };

        return new Popup
        {
            Child              = card,
            PlacementTarget    = target,
            Placement          = PlacementMode.Bottom,
            VerticalOffset     = 4,
            StaysOpen          = false,
            AllowsTransparency = true,
            PopupAnimation     = PopupAnimation.Fade,
        };
    }

    // ── Header picker (single-expiry) ─────────────────────────────────────

    private void BuildHeaderPicker()
    {
        _headerCalendar = CreateCalendar(ParsedLegs.Count > 0 ? ParsedLegs[0].ExpiryDate : null);
        _headerCalendar.SelectedDatesChanged += HeaderCalendar_SelectedDatesChanged;

        _headerPopup = CreatePopup(ExpiryLabel, _headerCalendar);

        ExpiryLabel.Cursor  = Cursors.Hand;
        ExpiryLabel.ToolTip = "Click to change expiry date";
        ExpiryLabel.MouseLeftButtonUp += ExpiryLabel_MouseLeftButtonUp;

        var hint = new TextBlock
        {
            Text       = "✎ click to edit",
            FontSize   = 8,
            Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
            Margin     = new Thickness(0, 2, 0, 0),
        };

        ExpiryStack.Children.Add(hint);
        ExpiryStack.Children.Add(_headerPopup);
    }

    private void ExpiryLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_headerPopup is null || _headerCalendar is null) return;

        var current = ParsedLegs.Count > 0 ? ParsedLegs[0].ExpiryDate : null;
        _headerCalendar.SelectedDate = current;
        _headerCalendar.DisplayDate  = current ?? DateTime.Today;
        _headerPopup.IsOpen = true;
        e.Handled = true;
    }

    private void HeaderCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_headerCalendar?.SelectedDate is not { } picked) return;
        _headerPopup!.IsOpen = false;

        foreach (var row in ParsedLegs)
            row.ExpiryDate = picked;

        ExpiryLabel.Text = FormatExpiryDate(picked);
        RebuildOvmlFromCurrentLegs();
    }

    // ── Per-leg pickers (mixed-expiry) ────────────────────────────────────

    private void BuildLegPickers()
    {
        LegExpiryList.UpdateLayout();

        for (int i = 0; i < ParsedLegs.Count; i++)
        {
            var row = ParsedLegs[i];

            var container = (FrameworkElement?)LegExpiryList.ItemContainerGenerator
                .ContainerFromIndex(i);
            if (container is null) continue;

            var label = FindChild<TextBlock>(container, "LegExpiryLabel");
            if (label is null) continue;

            var cal   = CreateCalendar(row.ExpiryDate);
            var popup = CreatePopup(label, cal);

            label.Tag = (row, popup, cal);
            cal.Tag   = (row, popup);

            label.MouseLeftButtonUp  += LegExpiryLabel_MouseLeftButtonUp;
            cal.SelectedDatesChanged += LegCalendar_SelectedDatesChanged;

            ExpiryStack.Children.Add(popup);
        }
    }

    private void LegExpiryLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        if (tb.Tag is not (LegRow row, Popup popup, WpfCalendar cal)) return;

        cal.SelectedDate = row.ExpiryDate;
        cal.DisplayDate  = row.ExpiryDate ?? DateTime.Today;
        popup.IsOpen = true;
        e.Handled = true;
    }

    private void LegCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not WpfCalendar cal) return;
        if (cal.Tag is not (LegRow row, Popup popup)) return;
        if (cal.SelectedDate is not { } picked) return;

        popup.IsOpen   = false;
        row.ExpiryDate = picked;
        RebuildOvmlFromCurrentLegs();
    }

    // ── Visual tree helper ────────────────────────────────────────────────

    private static T? FindChild<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var result = FindChild<T>(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    // ── Owner tracking ────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Owner is not null)
        {
            _offsetLeft = (Owner.Width - ActualWidth) / 2;
            _offsetTop  = (Owner.Height - ActualHeight) / 2;
            SnapToOwner();
            Owner.LocationChanged += OnOwnerLocationChanged;
            Owner.Closed          += OnOwnerClosed;
        }

        if (_mixedExpiry)
            BuildLegPickers();
    }

    private void OnOwnerLocationChanged(object? sender, EventArgs e) => SnapToOwner();
    private void OnOwnerClosed(object? sender, EventArgs e) => Close();

    private void SnapToOwner()
    {
        if (Owner is null) return;
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

    // ── Header drag — intentionally no-op ────────────────────────────────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

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

    private void Populate_Click(object sender, RoutedEventArgs e)  { Result = ClipboardCaptureAction.PopulateUi;      Close(); }
    private void Bloomberg_Click(object sender, RoutedEventArgs e) { Result = ClipboardCaptureAction.OpenInBloomberg; Close(); }
    private void Both_Click(object sender, RoutedEventArgs e)      { Result = ClipboardCaptureAction.Both;            Close(); }
    private void Reject_Click(object sender, RoutedEventArgs e)    { Result = ClipboardCaptureAction.Reject;          Close(); }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_headerPopup?.IsOpen == true) { _headerPopup.IsOpen = false; return; }
            Result = ClipboardCaptureAction.Reject;
            Close();
        }
        else if (e.Key == Key.Enter && PopulateButton.IsEnabled)
        {
            Result = ClipboardCaptureAction.PopulateUi;
            Close();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ResolveExpiryDisplay(IReadOnlyList<OvmlLeg> legs)
    {
        if (legs.Count == 0) return "—";

        var distinct = legs
            .Select(l => l.Expiry)
            .Where(exp => !string.IsNullOrWhiteSpace(exp))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count switch
        {
            0 => "—",
            1 => FormatExpiry(distinct[0]),
            _ => "MIXED"
        };
    }

    private static string FormatExpiry(string raw)
    {
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return FormatExpiryDate(dt);
        if (DateTime.TryParseExact(raw, "MM/dd/yy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return FormatExpiryDate(dt);
        return raw.ToUpperInvariant();
    }

    private static string FormatExpiryDate(DateTime dt)
        => dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

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

        public string LegNumber       { get; }
        public string Strike          { get; }
        public string NotionalDisplay { get; }

        private readonly bool _putCallUnknown;
        private string    _buySell;
        private string    _putCall;
        private DateTime? _expiryDate;

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

        public DateTime? ExpiryDate
        {
            get => _expiryDate;
            set { _expiryDate = value; Notify(nameof(ExpiryDate)); Notify(nameof(ExpiryDisplay)); }
        }

        public string ExpiryDisplay => _expiryDate.HasValue
            ? _expiryDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            : "—";

        public Brush BuySellBrush => BuySell == "SELL"
            ? (Brush)(Application.Current.Resources["NegativeRedDarkBrush"]  ?? Brushes.DarkRed)
            : (Brush)(Application.Current.Resources["PositiveGreenDarkBrush"] ?? Brushes.DarkGreen);

        public Brush CallPutBrush => PutCall == "PUT"
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x1D, 0x5A))
            : PutCall == "—"
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x44, 0x44, 0x44))
                : new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x3A, 0x6E));

        public LegRow(int number, OvmlLeg leg)
        {
            LegNumber = $"#{number}";
            _buySell  = leg.BuySell.StartsWith("S", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
            _putCallUnknown = string.IsNullOrWhiteSpace(leg.PutCall);
            _putCall  = _putCallUnknown ? "—" : leg.PutCall.ToUpperInvariant();
            Strike    = FormatStrike(leg.Strike);
            NotionalDisplay = leg.Notional >= 1_000_000
                ? $"{leg.Notional / 1_000_000:0.##}M"
                : leg.Notional > 0 ? leg.Notional.ToString("N0", CultureInfo.InvariantCulture) : "—";

            if (DateTime.TryParseExact(leg.Expiry, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                _expiryDate = dt;
            else if (DateTime.TryParseExact(leg.Expiry, "MM/dd/yy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                _expiryDate = dt;
        }

        public void ToggleBuySell() => BuySell = BuySell == "BUY" ? "SELL" : "BUY";

        public void ToggleCallPut() =>
            PutCall = PutCall switch { "—" => "CALL", "CALL" => "PUT", _ => "CALL" };

        public OvmlLeg ToOvmlLeg(OvmlLeg original) => original with
        {
            BuySell = BuySell == "SELL" ? "Sell" : "Buy",
            PutCall = PutCall switch { "CALL" => "Call", "PUT" => "Put", _ => original.PutCall },
            Expiry  = _expiryDate.HasValue
                ? _expiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : original.Expiry
        };

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}