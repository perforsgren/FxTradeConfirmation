using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FxTradeConfirmation.Helpers;
using FxTradeConfirmation.ViewModels;

namespace FxTradeConfirmation.Views;

public partial class PayoffChartControl : UserControl
{
    private MainViewModel? _vm;
    private bool _redrawPending;

    // single allocation instead of one per MakeLabel call
    private static readonly FontFamily _segoeUi = new("Segoe UI");

    // named constants for label offsets
    private const double YLabelOffset = 20;
    private const double XMaxLabelOffset = 40;

    /// <summary>Property names on <see cref="TradeLegViewModel"/> that affect the payoff curve.</summary>
    private static readonly HashSet<string> _relevantProperties =
    [
        nameof(TradeLegViewModel.StrikeText),
        nameof(TradeLegViewModel.BuySell),
        nameof(TradeLegViewModel.CallPut),
        nameof(TradeLegViewModel.NotionalText),
        nameof(TradeLegViewModel.PremiumText),
        nameof(TradeLegViewModel.PremiumAmountText),
        nameof(TradeLegViewModel.CurrencyPair),
    ];

    public PayoffChartControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleRedraw(); };
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unwire();
        _vm = null;
    }

    // --- Wiring ---

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unwire();

        _vm = e.NewValue as MainViewModel;

        if (_vm is not null)
        {
            _vm.Legs.CollectionChanged += OnLegsCollectionChanged;
            foreach (var leg in _vm.Legs)
                leg.PropertyChanged += OnLegPropertyChanged;
        }

        ScheduleRedraw();
    }

    private void Unwire()
    {
        if (_vm is null) return;

        _vm.Legs.CollectionChanged -= OnLegsCollectionChanged;
        foreach (var leg in _vm.Legs)
            leg.PropertyChanged -= OnLegPropertyChanged;
    }

    private void OnLegsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TradeLegViewModel leg in e.OldItems)
                leg.PropertyChanged -= OnLegPropertyChanged;

        if (e.NewItems is not null)
            foreach (TradeLegViewModel leg in e.NewItems)
                leg.PropertyChanged += OnLegPropertyChanged;

        ScheduleRedraw();
    }

    private void OnLegPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_relevantProperties.Contains(e.PropertyName ?? ""))
            ScheduleRedraw();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    /// <summary>
    /// Defers Redraw to the next layout pass so that ActualWidth/ActualHeight
    /// are up to date. Coalesces multiple rapid property changes into one draw.
    /// </summary>
    private void ScheduleRedraw()
    {
        if (_redrawPending) return;
        _redrawPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _redrawPending = false;
            Redraw();
        });
    }

    // --- Drawing ---

    private void Redraw()
    {
        ChartCanvas.Children.Clear();

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;

        if (_vm is null || _vm.Legs.Count == 0)
        {
            DrawEmptyMessage(w, h);
            return;
        }

        var range = PayoffCalculator.GetSpotRange(_vm.Legs);
        if (range is null)
        {
            DrawEmptyMessage(w, h);
            return;
        }

        var (minSpot, maxSpot) = range.Value;
        var data = PayoffCalculator.Calculate(_vm.Legs, minSpot, maxSpot);
        if (data.Count == 0)
        {
            DrawEmptyMessage(w, h);
            return;
        }

        if (w < 10 || h < 10) return;

        // Adaptive spot precision: mirror the strike decimals already used in the grid.
        // StrikeDecimals is 3 for JPY pairs, 5 for all others — subtract 1 for spot
        // labels so USDJPY shows 2 dp and EURUSD/EURSEK show 4 dp.
        int spotDecimals = Math.Max(1, _vm.Legs[0].StrikeDecimals - 1);

        // Margins for axis labels
        const double marginLeft = 60;
        const double marginRight = 16;
        const double marginTop = 12;
        const double marginBottom = 28;

        double plotW = w - marginLeft - marginRight;
        double plotH = h - marginTop - marginBottom;
        if (plotW < 10 || plotH < 10) return;

        // PnL range (auto-scale Y)
        double minPnl = data.Min(d => d.PnL);
        double maxPnl = data.Max(d => d.PnL);

        // Ensure the zero line is always visible
        if (minPnl > 0) minPnl = 0;
        if (maxPnl < 0) maxPnl = 0;

        // Add some vertical padding
        double pnlSpan = maxPnl - minPnl;
        if (pnlSpan < 1e-9) pnlSpan = 1;
        minPnl -= pnlSpan * 0.08;
        maxPnl += pnlSpan * 0.08;

        double ToX(double spot) => marginLeft + (spot - minSpot) / (maxSpot - minSpot) * plotW;
        double ToY(double pnl) => marginTop + (maxPnl - pnl) / (maxPnl - minPnl) * plotH;

        // --- Zero line ---
        var zeroY = ToY(0);
        var zeroLine = new Line
        {
            X1 = marginLeft,
            Y1 = zeroY,
            X2 = marginLeft + plotW,
            Y2 = zeroY,
            Stroke = FindBrush("BorderDefaultBrush"),
            StrokeThickness = 1,
            SnapsToDevicePixels = true
        };
        ChartCanvas.Children.Add(zeroLine);

        // --- "0" label on Y axis ---
        var zeroLabel = MakeLabel("0", FindBrush("TextMutedBrush"), 10);
        Canvas.SetLeft(zeroLabel, marginLeft - YLabelOffset);
        Canvas.SetTop(zeroLabel, zeroY - 8);
        ChartCanvas.Children.Add(zeroLabel);

        // --- PnL curve via StreamGeometry ---
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool first = true;
            foreach (var (spot, pnl) in data)
            {
                var pt = new Point(ToX(spot), ToY(pnl));
                if (first)
                {
                    ctx.BeginFigure(pt, false, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(pt, true, false);
                }
            }
        }
        geometry.Freeze();

        var curvePath = new Path
        {
            Data = geometry,
            Stroke = FindBrush("AccentBlueBrush"),
            StrokeThickness = 2,
            SnapsToDevicePixels = true
        };
        ChartCanvas.Children.Add(curvePath);

        // --- Profit/Loss shading ---
        DrawShadedArea(data, ToX, ToY, isProfit: true);
        DrawShadedArea(data, ToX, ToY, isProfit: false);

        // --- Break-even markers (where PnL crosses zero) ---
        for (int i = 1; i < data.Count; i++)
        {
            var (s0, p0) = data[i - 1];
            var (s1, p1) = data[i];

            if ((p0 < 0 && p1 > 0) || (p0 > 0 && p1 < 0))
            {
                // Linear interpolation to find the zero crossing
                double t = p0 / (p0 - p1);
                double bSpot = s0 + t * (s1 - s0);
                double bx = ToX(bSpot);

                var dashLine = new Line
                {
                    X1 = bx,
                    Y1 = marginTop,
                    X2 = bx,
                    Y2 = marginTop + plotH,
                    Stroke = FindBrush("TextMutedBrush"),
                    StrokeThickness = 1,
                    StrokeDashArray = [4, 3],
                    SnapsToDevicePixels = true
                };
                ChartCanvas.Children.Add(dashLine);

                // Break-even spot label
                var beLabel = MakeLabel(FormatSpot(bSpot, spotDecimals), FindBrush("TextSecondaryBrush"), 9);
                Canvas.SetLeft(beLabel, bx - 20);
                Canvas.SetTop(beLabel, marginTop + plotH + 2);
                ChartCanvas.Children.Add(beLabel);
            }
        }

        // --- X-axis labels (min / max spot) ---
        var minLabel = MakeLabel(FormatSpot(minSpot, spotDecimals), FindBrush("TextMutedBrush"), 9);
        Canvas.SetLeft(minLabel, marginLeft);
        Canvas.SetTop(minLabel, marginTop + plotH + 2);
        ChartCanvas.Children.Add(minLabel);

        var maxLabel = MakeLabel(FormatSpot(maxSpot, spotDecimals), FindBrush("TextMutedBrush"), 9);
        Canvas.SetLeft(maxLabel, marginLeft + plotW - XMaxLabelOffset);
        Canvas.SetTop(maxLabel, marginTop + plotH + 2);
        ChartCanvas.Children.Add(maxLabel);

        // --- Y-axis labels (min / max PnL) ---
        var topPnlLabel = MakeLabel(FormatPnl(maxPnl), FindBrush("TextMutedBrush"), 9);
        Canvas.SetLeft(topPnlLabel, 2);
        Canvas.SetTop(topPnlLabel, marginTop);
        ChartCanvas.Children.Add(topPnlLabel);

        var bottomPnlLabel = MakeLabel(FormatPnl(minPnl), FindBrush("TextMutedBrush"), 9);
        Canvas.SetLeft(bottomPnlLabel, 2);
        Canvas.SetTop(bottomPnlLabel, marginTop + plotH - 14);
        ChartCanvas.Children.Add(bottomPnlLabel);

        // --- "P&L" axis title ---
        var axisTitle = MakeLabel("P&L", FindBrush("TextDimmedBrush"), 9, isBold: true);
        Canvas.SetLeft(axisTitle, 2);
        Canvas.SetTop(axisTitle, marginTop + plotH / 2 - 7);
        ChartCanvas.Children.Add(axisTitle);

        // --- "Spot" axis title ---
        var spotTitle = MakeLabel("Spot", FindBrush("TextDimmedBrush"), 9, isBold: true);
        Canvas.SetLeft(spotTitle, marginLeft + plotW / 2 - 12);
        Canvas.SetTop(spotTitle, marginTop + plotH + 12);
        ChartCanvas.Children.Add(spotTitle);
    }

    /// <summary>
    /// Draws a semi-transparent shaded area between the PnL curve and the zero line
    /// for either the profit region (above zero) or the loss region (below zero).
    /// </summary>
    private void DrawShadedArea(
        IReadOnlyList<(double Spot, double PnL)> data,
        Func<double, double> toX, Func<double, double> toY,
        bool isProfit)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool inRegion = false;

            for (int i = 0; i < data.Count; i++)
            {
                var (spot, pnl) = data[i];
                bool match = isProfit ? pnl > 0 : pnl < 0;

                if (match && !inRegion)
                {
                    double startX = toX(spot);

                    if (i > 0)
                    {
                        var (s0, p0) = data[i - 1];
                        if ((isProfit && p0 < 0) || (!isProfit && p0 > 0))
                        {
                            double t = p0 / (p0 - pnl);
                            startX = toX(s0 + t * (spot - s0));
                        }
                    }

                    ctx.BeginFigure(new Point(startX, toY(0)), true, true);
                    ctx.LineTo(new Point(toX(spot), toY(pnl)), true, false);
                    inRegion = true;
                }
                else if (match && inRegion)
                {
                    ctx.LineTo(new Point(toX(spot), toY(pnl)), true, false);
                }
                else if (!match && inRegion)
                {
                    var (s0, p0) = data[i - 1];
                    double t = p0 / (p0 - pnl);
                    double endX = toX(s0 + t * (spot - s0));
                    ctx.LineTo(new Point(endX, toY(0)), true, false);
                    inRegion = false;
                }
            }

            if (inRegion)
            {
                ctx.LineTo(new Point(toX(data[^1].Spot), toY(0)), true, false);
            }
        }
        geo.Freeze();

        // derive fill color from theme brushes, so theme changes propagate automatically
        var sourceBrush = (SolidColorBrush)(isProfit
            ? FindBrush("PositiveGreenBrush")
            : FindBrush("NegativeRedBrush"));

        var color = Color.FromArgb(30, sourceBrush.Color.R, sourceBrush.Color.G, sourceBrush.Color.B);

        var fill = new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(color),
            SnapsToDevicePixels = true
        };
        ChartCanvas.Children.Add(fill);
    }

    // --- Helpers ---

    /// <summary>
    /// Draws a centered placeholder message on the canvas when no strikes are available.
    /// </summary>
    private void DrawEmptyMessage(double w, double h)
    {
        if (w < 10 || h < 10) return;

        var tb = MakeLabel("Enter strikes to see payoff diagram", FindBrush("TextMutedBrush"), 12);
        // Measure to center it
        tb.Measure(new Size(w, h));
        Canvas.SetLeft(tb, (w - tb.DesiredSize.Width) / 2);
        Canvas.SetTop(tb, (h - tb.DesiredSize.Height) / 2);
        ChartCanvas.Children.Add(tb);
    }

    private Brush FindBrush(string key) =>
        (Brush)(FindResource(key) ?? Brushes.Gray);

    private static TextBlock MakeLabel(string text, Brush foreground, double fontSize, bool isBold = false)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            FontFamily = _segoeUi,
            FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
        };
    }

    private static string FormatPnl(double value)
    {
        double abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000 => $"{value / 1_000_000:F1}M",
            >= 1_000 => $"{value / 1_000:F0}K",
            _ => value.ToString("F0", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Formats a spot value using pair-adaptive decimal precision.
    /// <paramref name="decimals"/> is derived from <see cref="TradeLegViewModel.StrikeDecimals"/> − 1,
    /// giving 2 dp for JPY pairs and 4 dp for all others.
    /// </summary>
    private static string FormatSpot(double value, int decimals) =>
        value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
}
