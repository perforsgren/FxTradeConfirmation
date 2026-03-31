using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using FxTradeConfirmation.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FxTradeConfirmation.Views;

public partial class TradeGridControl : UserControl
{
    private const int RowHeader = 0;
    private const int RowTradeSection = 1;
    private const int RowCounterpart = 2;
    private const int RowCurrencyPair = 3;
    private const int RowBuySell = 4;
    private const int RowOptionSection = 5;
    private const int RowCallPut = 6;
    private const int RowStrike = 7;
    private const int RowExpiry = 8;
    private const int RowSettlement = 9;
    private const int RowCut = 10;
    private const int RowAmountSection = 11;
    private const int RowNotional = 12;
    private const int RowPremium = 13;
    private const int RowPremiumAmount = 14;
    private const int RowPremiumDate = 15;
    private const int RowAdminSection = 16;
    private const int RowPortfolio = 17;
    private const int RowTrader = 18;
    private const int RowExecutionTime = 19;
    private const int RowMic = 20;
    private const int RowTvtic = 21;
    private const int RowIsin = 22;
    private const int RowSales = 23;
    private const int RowInvDecId = 24;
    private const int RowBroker = 25;
    private const int RowMargin = 26;
    private const int RowReportingEntity = 27;
    private const int RowHedgeSep = 28;
    private const int RowHedge = 29;
    private const int RowHedgeBuySell = 30;
    private const int RowHedgeNotional = 31;
    private const int RowHedgeNotionalCcy = 32;
    private const int RowHedgeRate = 33;
    private const int RowHedgeSettlement = 34;
    private const int RowBookCalypso = 35;
    private const int RowHedgeTvtic = 36;
    private const int RowHedgeUti = 37;
    private const int RowHedgeIsin = 38;
    private const int TotalRows = 39;

    private static readonly int[] HedgeDetailRows =
        [RowHedgeBuySell, RowHedgeNotional, RowHedgeRate, RowHedgeSettlement];

    private static readonly int[] AdminRows =
        [RowAdminSection, RowPortfolio, RowTrader, RowExecutionTime, RowMic, RowTvtic,
         RowIsin, RowSales, RowInvDecId, RowBroker, RowMargin, RowReportingEntity];

    private static readonly int[] HedgeAdminRows =
        [RowHedgeTvtic, RowHedgeUti, RowHedgeIsin, RowBookCalypso];

    private const int ColDistInput = 0;
    private const int ColLabel = 1;
    private const int ColDistToggle = 2;

    private MainViewModel? _vm;

    private readonly List<List<UIElement>> _hedgeDetailElements = [];
    private readonly List<List<UIElement>> _adminElements = [];
    private readonly List<List<UIElement>> _hedgeAdminElements = [];

    private ComboBox? _distCounterpart;
    private ComboBox? _distCcyPair;
    private ComboBox? _distCut;

    private Border? _solvingDimOverlay;
    private Border? _solvingTargetHighlight;
    private Border? _solvingTotalHighlight;
    private Storyboard? _solvingPulseStoryboard;

    private TextBox? _totalPremiumStyleTb;
    private TextBox? _totalPremiumAmountTb;

    private readonly List<(TextBox premiumTb, TextBox premiumAmountTb)> _legPremiumControls = [];

    // ================================================================
    //  TAB NAVIGATION
    // ================================================================

    /// <summary>
    /// Flat ordered list of tabbable controls in row-first (horizontal) order.
    /// Each entry carries an optional runtime condition — when the condition
    /// returns false the control is skipped even if it is visible and enabled.
    /// </summary>
    private readonly List<(UIElement control, Func<bool>? condition)> _tabOrder = [];

    /// <summary>
    /// Maps each distributor-column input to the Leg 1 control on the same row.
    /// When Tab is pressed from a dist control, focus jumps directly to that target
    /// instead of following the normal _tabOrder sequence.
    /// </summary>
    private readonly Dictionary<UIElement, UIElement> _distTabMap = [];

    private void RegisterTabbable(UIElement element)
        => _tabOrder.Add((element, null));

    private void RegisterTabbableIf(UIElement element, Func<bool> condition)
        => _tabOrder.Add((element, condition));

    public TradeGridControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        PreviewGotKeyboardFocus += OnPreviewGotKeyboardFocus;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewKeyDown += OnGridPreviewKeyDown;
    }

    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter) return;

        // Enter always moves forward; Tab respects Shift modifier
        bool forward = e.Key == Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var focused = Keyboard.FocusedElement as DependencyObject;

        // Resolve the registered UIElement that currently holds focus
        UIElement? focusedElement = null;
        if (focused != null)
        {
            var current = focused;
            while (current != null && focusedElement == null)
            {
                if (current is UIElement ui &&
                    (_tabOrder.Any(x => x.control == ui) || _distTabMap.ContainsKey(ui)))
                    focusedElement = ui;

                var logicalParent = LogicalTreeHelper.GetParent(current);
                current = logicalParent ?? VisualTreeHelper.GetParent(current);
            }
        }

        // ── Distributor → Leg 1 shortcut ────────────────────────────
        // Tab forward from any dist control jumps directly to the same
        // row's Leg 1 control (or Leg 1 of the next tabbable row on
        // Shift+Tab). Shift+Tab from a dist control follows normal order.
        if (focusedElement != null && forward && _distTabMap.TryGetValue(focusedElement, out var leg1Target))
        {
            if (IsTabbable(leg1Target))
            {
                FocusControl(leg1Target);
                e.Handled = true;
                return;
            }
            // leg1Target is currently not tabbable (e.g. Premium disabled) —
            // fall through to normal order starting from that position
            int fallbackIndex = _tabOrder.FindIndex(x => x.control == leg1Target);
            if (fallbackIndex >= 0)
            {
                if (TryFocusFrom(fallbackIndex, forward))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // ── Normal tab order ─────────────────────────────────────────
        int currentIndex = -1;
        if (focusedElement != null)
            currentIndex = _tabOrder.FindIndex(x => x.control == focusedElement);

        // Focused element is outside our grid — let WPF handle naturally
        if (currentIndex < 0 && focused != null && focusedElement == null)
            return;

        if (TryFocusFrom(currentIndex, forward))
            e.Handled = true;
        else
            e.Handled = true; // all non-tabbable — swallow anyway
    }

    private static void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox tb && !tb.IsReadOnly)
            tb.SelectAll();
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var tb = FindParent<TextBox>(source);
        if (tb == null || tb.IsReadOnly || tb.IsKeyboardFocusWithin) return;
        tb.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// Searches _tabOrder starting after <paramref name="startIndex"/> in the given
    /// direction and focuses the first tabbable candidate. Returns true on success.
    /// </summary>
    private bool TryFocusFrom(int startIndex, bool forward)
    {
        int count = _tabOrder.Count;
        if (count == 0) return false;

        int idx = startIndex < 0 ? -1 : startIndex;
        for (int attempt = 0; attempt < count; attempt++)
        {
            idx = forward ? (idx + 1) % count : (idx - 1 + count) % count;

            var (candidate, condition) = _tabOrder[idx];
            if (condition != null && !condition()) continue;
            if (!IsTabbable(candidate)) continue;

            FocusControl(candidate);
            return true;
        }
        return false;
    }

    private static bool IsTabbable(UIElement element)
    {
        if (element.Visibility != Visibility.Visible) return false;
        if (!element.IsEnabled) return false;
        if (element is TextBox tb && tb.IsReadOnly) return false;

        DependencyObject? parent = VisualTreeHelper.GetParent(element);
        while (parent != null)
        {
            if (parent is UIElement p && p.Visibility != Visibility.Visible)
                return false;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return true;
    }

    private static void FocusControl(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ================================================================
    //  DATA CONTEXT / EVENT WIRING
    // ================================================================

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Legs.CollectionChanged -= OnLegsChanged;
            oldVm.DistributorClearRequested -= OnDistributorClearRequested;
            oldVm.SolvingDialogRequested -= OnSolvingDialogRequested;
            oldVm.SaveResultDialogRequested -= OnSaveResultDialogRequested;
            oldVm.OpenRecentDialogRequested -= OnOpenRecentDialogRequested;
            oldVm.PropertyChanged -= OnVmPropertyChanged;
            foreach (var leg in oldVm.Legs)
                leg.PropertyChanged -= OnLegPropertyChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            _vm = newVm;
            _vm.Legs.CollectionChanged += OnLegsChanged;
            _vm.DistributorClearRequested += OnDistributorClearRequested;
            _vm.SolvingDialogRequested += OnSolvingDialogRequested;
            _vm.SaveResultDialogRequested += OnSaveResultDialogRequested;
            _vm.OpenRecentDialogRequested += OnOpenRecentDialogRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            foreach (var leg in _vm.Legs)
                leg.PropertyChanged += OnLegPropertyChanged;
            RebuildGrid();
        }
    }

    private void OnDistributorClearRequested(string fieldName)
    {
        switch (fieldName)
        {
            case nameof(TradeLegViewModel.Counterpart):
                if (_distCounterpart != null) _distCounterpart.SelectedIndex = -1;
                break;
            case nameof(TradeLegViewModel.CurrencyPair):
                if (_distCcyPair != null) _distCcyPair.SelectedIndex = -1;
                break;
            case nameof(TradeLegViewModel.Cut):
                if (_distCut != null) _distCut.SelectedIndex = -1;
                break;
        }
    }

    private void OnSolvingDialogRequested(bool isByAmount, string unitDisplay, string contextLabel)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplySolvingVisuals(isByAmount);
            var ownerWindow = Window.GetWindow(this);
            var dialog = new SolvingDialog(isByAmount, unitDisplay, contextLabel) { Owner = ownerWindow };
            dialog.SolveCallback = (target, payReceive) => _vm?.SolvePremium(target, payReceive);

            if (dialog.ShowDialog() == true)
                RemoveSolvingVisuals();
            else
            {
                RemoveSolvingVisuals();
                _vm?.CancelSolvingCommand.Execute(null);
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnSaveResultDialogRequested(
    IReadOnlyList<TradeSubmitResult> results,
    IReadOnlyList<TradeLeg> models,
    int successCount,
    int failCount)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var successBrush = (System.Windows.Media.Brush)FindResource("PositiveGreenBrush");
            var failBrush = (System.Windows.Media.Brush)FindResource("NegativeRedBrush");

            // First pass: collect (resultIndex, label) for options and hedges separately.
            // SubmitTradeAsync produces results interleaved: option, [hedge], option, [hedge]…
            var optionItems = new List<SaveResultItem>();
            var hedgeItems = new List<SaveResultItem>();
            int resultIndex = 0;

            for (int i = 0; i < models.Count && resultIndex < results.Count; i++)
            {
                var leg = models[i];
                int legNum = i + 1;

                // BuySell in TradeLeg is the CLIENT's perspective — invert for bank display.
                BuySell bankOptionSide = leg.BuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
                string optionLabel = $"Leg {legNum} Option — {bankOptionSide} {leg.CallPut} " +
                                     $"{leg.CurrencyPair} {FormatRate(leg.Strike, leg.CurrencyPair)} " +
                                     $"{leg.ExpiryDate:yyyy-MM-dd}";
                optionItems.Add(SaveResultItem.FromResult(results[resultIndex], optionLabel, successBrush, failBrush));
                resultIndex++;

                // HedgeBuySell is also client perspective — invert for bank display.
                bool hasHedge = leg.Hedge != Models.HedgeType.No
                    && leg.HedgeNotional.HasValue
                    && leg.HedgeRate.HasValue
                    && resultIndex < results.Count;

                if (hasHedge)
                {
                    BuySell bankHedgeSide = leg.HedgeBuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
                    string hedgeLabel = $"Leg {legNum} Hedge — {leg.Hedge} " +
                                        $"{bankHedgeSide} {leg.CurrencyPair} @ {FormatRate(leg.HedgeRate, leg.CurrencyPair)}";
                    hedgeItems.Add(SaveResultItem.FromResult(results[resultIndex], hedgeLabel, successBrush, failBrush));
                    resultIndex++;
                }
            }

            // Options first, then hedges
            var items = optionItems.Concat(hedgeItems).ToList();

            var ownerWindow = Window.GetWindow(this);
            var dialog = new SaveResultDialog(items, successCount, failCount) { Owner = ownerWindow };
            dialog.ShowDialog();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Formats a rate (strike or hedge rate) with a minimum of 4 decimal places,
    /// or 2 decimal places when the currency pair involves JPY.
    /// </summary>
    private static string FormatRate(decimal? value, string currencyPair)
    {
        if (!value.HasValue) return "—";

        bool isJpy = currencyPair.Contains("JPY", StringComparison.OrdinalIgnoreCase);
        int minDecimals = isJpy ? 2 : 4;

        var raw = value.Value.ToString("G29", CultureInfo.InvariantCulture);
        int dotIndex = raw.IndexOf('.');
        int actualDecimals = dotIndex >= 0 ? raw.Length - dotIndex - 1 : 0;

        int decimals = Math.Max(actualDecimals, minDecimals);
        return value.Value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private void OnOpenRecentDialogRequested(IRecentTradeService recentTradeService)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var ownerWindow = Window.GetWindow(this);
            var dialog = new OpenRecentDialog(recentTradeService) { Owner = ownerWindow };

            if (dialog.ShowDialog() == true && dialog.LoadedTrade != null)
            {
                _vm?.LoadTrade(dialog.LoadedTrade);
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnLegsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (TradeLegViewModel leg in e.OldItems)
                leg.PropertyChanged -= OnLegPropertyChanged;
        if (e.NewItems != null)
            foreach (TradeLegViewModel leg in e.NewItems)
                leg.PropertyChanged += OnLegPropertyChanged;
        RebuildGrid();
    }

    private void OnLegPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TradeLegViewModel.HasHedge))
        {
            UpdateHedgeVisibility();
            UpdateAdminVisibility();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowAdminRows))
            UpdateAdminVisibility();
    }

    private static int LegValCol(int legIndex) => 3 + legIndex;

    // ================================================================
    //  COMMA → DOT
    // ================================================================

    private static void ReplaceCommaWithDot(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ",")
        {
            e.Handled = true;
            if (sender is TextBox tb)
            {
                int selStart = tb.SelectionStart;
                int selLen = tb.SelectionLength;
                tb.Text = tb.Text[..selStart] + "." + tb.Text[(selStart + selLen)..];
                tb.CaretIndex = selStart + 1;
            }
        }
    }

    private static void AttachNumericHandlers(TextBox tb, Action<string> applyAction)
    {
        tb.PreviewTextInput += ReplaceCommaWithDot;
        tb.LostFocus += (s, _) => { if (s is TextBox t) applyAction(t.Text); };
        tb.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox t)
            {
                applyAction(t.Text);
                Keyboard.ClearFocus();
            }
        };
    }

    // ================================================================
    //  SOLVING VISUAL EFFECTS
    // ================================================================

    private void ApplySolvingVisuals(bool isByAmount)
    {
        if (_vm == null) return;

        int solvingLegIndex = -1;
        for (int i = 0; i < _vm.Legs.Count; i++)
        {
            if (_vm.Legs[i].IsSolvingTarget) { solvingLegIndex = i; break; }
        }
        if (solvingLegIndex < 0) return;

        var amberBrush = FindBrush("SolvingAmberBrush");
        var amberDimBrush = FindBrush("SolvingAmberDimBrush");
        int totalCols = RootGrid.ColumnDefinitions.Count;

        _solvingDimOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x0E, 0x1A)),
            IsHitTestVisible = false
        };
        Grid.SetRow(_solvingDimOverlay, 0); Grid.SetColumn(_solvingDimOverlay, 0);
        Grid.SetRowSpan(_solvingDimOverlay, TotalRows); Grid.SetColumnSpan(_solvingDimOverlay, totalCols);
        Panel.SetZIndex(_solvingDimOverlay, 50);
        RootGrid.Children.Add(_solvingDimOverlay);

        int targetRow = isByAmount ? RowPremiumAmount : RowPremium;
        _solvingTargetHighlight = new Border
        {
            BorderBrush = amberBrush,
            BorderThickness = new Thickness(2),
            Background = amberDimBrush,
            IsHitTestVisible = false
        };
        Grid.SetRow(_solvingTargetHighlight, targetRow);
        Grid.SetColumn(_solvingTargetHighlight, LegValCol(solvingLegIndex));
        Panel.SetZIndex(_solvingTargetHighlight, 60);
        RootGrid.Children.Add(_solvingTargetHighlight);

        _solvingTotalHighlight = new Border
        {
            BorderBrush = amberBrush,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            Opacity = 1.0
        };
        Grid.SetRow(_solvingTotalHighlight, RowPremium); Grid.SetColumn(_solvingTotalHighlight, ColDistInput);
        Grid.SetRowSpan(_solvingTotalHighlight, 2);
        Panel.SetZIndex(_solvingTotalHighlight, 60);
        RootGrid.Children.Add(_solvingTotalHighlight);

        var pulseAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.3,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        _solvingPulseStoryboard = new Storyboard();
        _solvingPulseStoryboard.Children.Add(pulseAnimation);
        Storyboard.SetTarget(pulseAnimation, _solvingTotalHighlight);
        Storyboard.SetTargetProperty(pulseAnimation, new PropertyPath(OpacityProperty));
        _solvingPulseStoryboard.Begin();
    }

    private void RemoveSolvingVisuals()
    {
        _solvingPulseStoryboard?.Stop();
        _solvingPulseStoryboard = null;
        if (_solvingDimOverlay != null) { RootGrid.Children.Remove(_solvingDimOverlay); _solvingDimOverlay = null; }
        if (_solvingTargetHighlight != null) { RootGrid.Children.Remove(_solvingTargetHighlight); _solvingTargetHighlight = null; }
        if (_solvingTotalHighlight != null) { RootGrid.Children.Remove(_solvingTotalHighlight); _solvingTotalHighlight = null; }
    }

    // ================================================================
    //  GRID REBUILD
    // ================================================================

    private void RebuildGrid()
    {
        if (_vm == null) return;

        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        _hedgeDetailElements.Clear();
        _adminElements.Clear();
        _hedgeAdminElements.Clear();
        _legPremiumControls.Clear();
        _tabOrder.Clear();
        _distTabMap.Clear();

        _solvingDimOverlay = null;
        _solvingTargetHighlight = null;
        _solvingTotalHighlight = null;
        _solvingPulseStoryboard = null;

        int legCount = _vm.Legs.Count;

        for (int i = 0; i < TotalRows; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        for (int i = 0; i < legCount; i++)
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        KeyboardNavigation.SetTabNavigation(RootGrid, KeyboardNavigationMode.None);

        int totalCols = RootGrid.ColumnDefinitions.Count;

        var headerBg = new Border { Background = FindBrush("BgHeaderRowBrush") };
        Grid.SetRow(headerBg, RowHeader); Grid.SetColumn(headerBg, 0);
        Grid.SetColumnSpan(headerBg, totalCols);
        RootGrid.Children.Add(headerBg);

        var labelColBg = new Border { Background = FindBrush("BgLabelColumnBrush") };
        Grid.SetRow(labelColBg, 0); Grid.SetColumn(labelColBg, ColLabel);
        Grid.SetColumnSpan(labelColBg, 2); Grid.SetRowSpan(labelColBg, TotalRows);
        RootGrid.Children.Add(labelColBg);

        var labelColBorder = new Border
        {
            BorderBrush = FindBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(labelColBorder, 0); Grid.SetColumn(labelColBorder, ColLabel);
        Grid.SetColumnSpan(labelColBorder, 2); Grid.SetRowSpan(labelColBorder, TotalRows);
        RootGrid.Children.Add(labelColBorder);

        var distHeader = new TextBlock
        {
            Text = "⇉",
            ToolTip = "Distribute to all legs",
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = FindBrush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 6, 4, 6)
        };
        Grid.SetRow(distHeader, RowHeader); Grid.SetColumn(distHeader, ColDistInput);
        RootGrid.Children.Add(distHeader);

        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm.Legs[i];
            var headerPanel = new DockPanel { Margin = new Thickness(4, 6, 4, 6) };

            var legLabel = new TextBlock
            {
                Text = $"Leg {leg.LegNumber}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = FindBrush("AccentBlueBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = FindFont("MainFont")
            };
            DockPanel.SetDock(legLabel, Dock.Left);
            headerPanel.Children.Add(legLabel);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(btnPanel, Dock.Right);

            var btnCopy = CreateIconButton("+", "Copy Leg");
            btnCopy.Foreground = FindBrush("PositiveGreenBrush");
            btnCopy.Command = _vm.CopyLegCommand;
            btnCopy.CommandParameter = leg;
            btnPanel.Children.Add(btnCopy);

            var btnDelete = CreateIconButton("✕", "Delete Leg");
            btnDelete.Foreground = FindBrush("NegativeRedBrush");
            btnDelete.Command = _vm.DeleteLegCommand;
            btnDelete.CommandParameter = leg;
            btnPanel.Children.Add(btnDelete);

            headerPanel.Children.Add(btnPanel);
            Grid.SetRow(headerPanel, RowHeader);
            Grid.SetColumn(headerPanel, LegValCol(i));
            RootGrid.Children.Add(headerPanel);

            var validIcon = new TextBlock
            {
                Text = "⚠",
                Foreground = FindBrush("NegativeRedBrush"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Required fields missing"
            };
            validIcon.SetBinding(UIElement.VisibilityProperty,
                new Binding(nameof(leg.HasValidationError))
                {
                    Source = leg,
                    Converter = new BooleanToVisibilityConverter()
                });
            DockPanel.SetDock(validIcon, Dock.Left);
            headerPanel.Children.Insert(1, validIcon);
        }

        AddSectionHeaderRow(RowTradeSection, "📋  TRADE DETAILS", totalCols);
        AddSectionHeaderRow(RowOptionSection, "📊  OPTION DETAILS", totalCols);
        AddSectionHeaderRow(RowAmountSection, "💰  AMOUNTS & PREMIUM", totalCols);

        BuildOptionRows(legCount, totalCols);
        BuildAdminRows(legCount, totalCols);
        BuildHedgeRows(legCount, totalCols);
        UpdateHedgeVisibility();
        UpdateAdminVisibility();
    }

    // ================================================================
    //  SECTION HEADER ROW
    // ================================================================

    private void AddSectionHeaderRow(int row, string text, int totalCols)
    {
        var bg = new Border { Background = FindBrush("BgSectionHeaderBrush") };
        Grid.SetRow(bg, row); Grid.SetColumn(bg, 0); Grid.SetColumnSpan(bg, totalCols);
        RootGrid.Children.Add(bg);

        var line = new Border
        {
            BorderBrush = FindBrush("AccentBlueBrush"),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Opacity = 0.5
        };
        Grid.SetRow(line, row); Grid.SetColumn(line, 0); Grid.SetColumnSpan(line, totalCols);
        RootGrid.Children.Add(line);

        var label = new TextBlock
        {
            Text = text,
            Style = FindStyle<TextBlock>("SectionHeader"),
            Margin = new Thickness(12, 6, 0, 6)
        };
        Grid.SetRow(label, row); Grid.SetColumn(label, ColLabel);
        Grid.SetColumnSpan(label, 2);
        RootGrid.Children.Add(label);
    }

    // ================================================================
    //  OPTION ROWS
    // ================================================================

    private void BuildOptionRows(int legCount, int totalCols)
    {
        AddRowLabel(RowCounterpart, "Counterpart", totalCols);
        AddRowLabel(RowCurrencyPair, "Currency Pair", totalCols);
        AddRowLabel(RowBuySell, "Client Buy/Sell", totalCols);
        AddRowLabel(RowCallPut, "Call/Put", totalCols);
        AddRowLabel(RowStrike, "Strike", totalCols);
        AddRowLabel(RowExpiry, "Expiry Date", totalCols);
        AddRowLabel(RowSettlement, "Settlement Date", totalCols);
        AddRowLabel(RowCut, "Cut", totalCols);
        AddRowLabel(RowNotional, "Notional", totalCols);
        AddRowLabel(RowPremium, "Premium", totalCols);
        AddRowLabel(RowPremiumAmount, "Premium Amount", totalCols);
        AddRowLabel(RowPremiumDate, "Premium Date", totalCols);

        _distCounterpart = CreateDistComboBox("ReferenceData.Counterparts");
        _distCounterpart.SelectionChanged += (s, _) =>
        {
            if (s is ComboBox cb && cb.SelectedItem is string val)
                _vm?.SetAllCounterpartCommand.Execute(val);
        };
        AddCell(RowCounterpart, ColDistInput, _distCounterpart);

        _distCcyPair = CreateDistComboBox("ReferenceData.CurrencyPairs");
        _distCcyPair.SelectionChanged += (s, _) =>
        {
            if (s is ComboBox cb && cb.SelectedItem is string val)
                _vm?.SetAllCurrencyPairCommand.Execute(val);
        };
        AddCell(RowCurrencyPair, ColDistInput, _distCcyPair);

        AddDistToggle(RowBuySell, ColDistToggle, DistToggle_BuySell, "Flippa Buy/Sell på alla legs");
        AddDistToggle(RowCallPut, ColDistToggle, DistToggle_CallPut, "Flippa Call/Put på alla legs");
        AddDistToggle(RowPremium, ColDistToggle, DistToggle_PremiumStyle, "Flippa Premium Style på alla legs");
        AddDistToggle(RowPremiumDate, ColDistToggle, DistToggle_PremiumDate, "Flippa Premium Date Type på alla legs");

        var distStrike = CreateDistTextBox();
        distStrike.ToolTip = "Enter strike value. Comma → dot. Press Enter to apply to all legs.";
        distStrike.PreviewTextInput += ReplaceCommaWithDot;
        distStrike.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            { _vm?.SetAllStrikeCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        distStrike.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            { _vm?.SetAllStrikeCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        AddCell(RowStrike, ColDistInput, distStrike);

        var distExpiry = CreateDistTextBox();
        distExpiry.ToolTip = "Enter tenor (on, 1d, 1w, 1m, 1y) or date (dd/MM, dd-MMM). Press Enter.";
        distExpiry.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            { _vm?.SetAllExpiryCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        distExpiry.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            { _vm?.SetAllExpiryCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        AddCell(RowExpiry, ColDistInput, distExpiry);

        _distCut = CreateDistComboBox("ReferenceData.Cuts");
        _distCut.SelectionChanged += (s, _) =>
        {
            if (s is ComboBox cb && cb.SelectedItem is string val)
                _vm?.SetAllCutCommand.Execute(val);
        };
        AddCell(RowCut, ColDistInput, _distCut);

        var distNotional = CreateDistTextBox();
        distNotional.ToolTip = "Enter notional (e.g. 5m, 100k, 2b, 1y or plain number). Comma → dot.";
        distNotional.PreviewTextInput += ReplaceCommaWithDot;
        distNotional.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            { _vm?.SetAllNotionalCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        distNotional.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            { _vm?.SetAllNotionalCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        AddCell(RowNotional, ColDistInput, distNotional);

        var brushConverter = new BrushKeyConverter(this);

        _totalPremiumStyleTb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            IsReadOnly = true,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 1, 2, 1)
        };
        _totalPremiumStyleTb.SetBinding(TextBox.TextProperty,
            new Binding(nameof(MainViewModel.TotalPremiumStyleDisplay)) { Source = _vm });
        _totalPremiumStyleTb.SetBinding(TextBox.ForegroundProperty,
            new Binding(nameof(MainViewModel.TotalPremiumBrushKey)) { Source = _vm, Converter = brushConverter });
        AddCell(RowPremium, ColDistInput, _totalPremiumStyleTb);

        _totalPremiumAmountTb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            IsReadOnly = true,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 1, 2, 1)
        };
        _totalPremiumAmountTb.SetBinding(TextBox.TextProperty,
            new Binding(nameof(MainViewModel.TotalPremiumAmountDisplay)) { Source = _vm });
        _totalPremiumAmountTb.SetBinding(TextBox.ForegroundProperty,
            new Binding(nameof(MainViewModel.TotalPremiumBrushKey)) { Source = _vm, Converter = brushConverter });
        AddCell(RowPremiumAmount, ColDistInput, _totalPremiumAmountTb);

        // ── Per-leg controls ─────────────────────────────────────────
        var legControls = new Dictionary<int, List<(int row, UIElement control)>>();
        for (int i = 0; i < legCount; i++)
            legControls[i] = [];

        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm!.Legs[i];
            int vc = LegValCol(i);

            if (!leg.IsFirstLeg)
            {
                AddCell(RowCounterpart, vc, CreateLegTextBox(leg, nameof(leg.Counterpart), isReadOnly: true));
                AddCell(RowCurrencyPair, vc, CreateLegTextBox(leg, nameof(leg.CurrencyPair), isReadOnly: true));
            }
            else
            {
                var counterpartCombo = CreateLegComboBox(leg, nameof(leg.Counterpart), "ReferenceData.Counterparts");
                AddCell(RowCounterpart, vc, counterpartCombo);
                legControls[i].Add((RowCounterpart, counterpartCombo));

                var ccyPairCombo = CreateLegComboBox(leg, nameof(leg.CurrencyPair), "ReferenceData.CurrencyPairs");
                AddCell(RowCurrencyPair, vc, ccyPairCombo);
                legControls[i].Add((RowCurrencyPair, ccyPairCombo));
            }

            AddCell(RowBuySell, vc, CreateBuySellToggle(leg));
            AddCell(RowCallPut, vc, CreateCallPutToggle(leg));

            var strikeTb = CreateLegTextBox(leg, nameof(leg.StrikeText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            strikeTb.ToolTip = "Numeric only. Comma → dot.";
            AttachNumericHandlers(strikeTb, leg.ApplyStrikeInput);
            AddCell(RowStrike, vc, strikeTb);
            legControls[i].Add((RowStrike, strikeTb));

            var expiryTb = CreateLegTextBox(leg, nameof(leg.ExpiryText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            expiryTb.ToolTip = "Enter tenor (on, o/n, 1d, 1w, 1m, 1y) or date (dd/MM, dd-MMM, dd/MM/yyyy)";
            expiryTb.LostFocus += (s, _) => { if (s is TextBox tb) leg.ApplyExpiryInput(tb.Text); };
            expiryTb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && s is TextBox tb)
                { leg.ApplyExpiryInput(tb.Text); Keyboard.ClearFocus(); }
            };
            AddCell(RowExpiry, vc, expiryTb);
            legControls[i].Add((RowExpiry, expiryTb));

            // Settlement Date — read-only, skip in tab order
            AddCell(RowSettlement, vc, CreateLegTextBox(leg, nameof(leg.SettlementDate),
                stringFormat: "yyyy-MM-dd", isReadOnly: true));

            // Cut — NOT in tab order (changed via mouse)
            var cutCombo = CreateLegComboBox(leg, nameof(leg.Cut), "ReferenceData.Cuts");
            AddCell(RowCut, vc, cutCombo);

            var notionalTb = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.NotionalText),
                leg, nameof(leg.NotionalCurrency), () => leg.ToggleNotionalCurrencyCommand.Execute(null));
            notionalTb.ToolTip = "Enter notional (e.g. 5m, 100k, 2b, 1y or plain number). Comma → dot.";
            AttachNumericHandlers(notionalTb, leg.ApplyNotionalInput);
            AddCell(RowNotional, vc, notionalTb);
            legControls[i].Add((RowNotional, notionalTb));

            var premiumTb = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.PremiumText),
                leg, nameof(leg.PremiumStyleDisplay), () => leg.TogglePremiumStyleCommand.Execute(null));
            premiumTb.SetBinding(TextBox.IsEnabledProperty,
                new Binding(nameof(leg.PremiumInputEnabled)) { Source = leg });
            premiumTb.SetBinding(TextBox.IsReadOnlyProperty,
                new Binding(nameof(leg.IsPremiumReadOnly)) { Source = leg });
            AttachNumericHandlers(premiumTb, leg.ApplyPremiumInput);
            AddCell(RowPremium, vc, premiumTb);
            legControls[i].Add((RowPremium, premiumTb));

            var premAmtTb = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.PremiumAmountText),
                leg, nameof(leg.PremiumCurrency), () => leg.TogglePremiumCurrencyCommand.Execute(null));
            premAmtTb.SetBinding(TextBox.IsEnabledProperty,
                new Binding(nameof(leg.PremiumInputEnabled)) { Source = leg });
            premAmtTb.SetBinding(TextBox.IsReadOnlyProperty,
                new MultiBinding
                {
                    Converter = new OrBoolConverter(),
                    Bindings =
                    {
                        new Binding(nameof(leg.IsPremiumLocked)) { Source = leg },
                        new Binding(nameof(leg.IsPremiumAmountReadOnly)) { Source = leg }
                    }
                });
            AttachNumericHandlers(premAmtTb, leg.ApplyPremiumAmountInput);
            AddCell(RowPremiumAmount, vc, premAmtTb);
            legControls[i].Add((RowPremiumAmount, premAmtTb));

            // Premium Date — read-only, skip in tab order
            AddCell(RowPremiumDate, vc, CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.PremiumDate),
                leg, nameof(leg.PremiumDateType), () => leg.TogglePremiumDateTypeCommand.Execute(null),
                stringFormat: "yyyy-MM-dd", isReadOnly: true));
        }

        // Register in row-first order
        var optionRows = legControls.Values
            .SelectMany(l => l.Select(x => x.row))
            .Distinct().OrderBy(r => r).ToList();

        foreach (var row in optionRows)
            for (int i = 0; i < legCount; i++)
            {
                var match = legControls[i].FirstOrDefault(x => x.row == row);
                if (match.control != null)
                    RegisterTabbable(match.control);
            }

        // ── Dist → Leg 1 tab map ─────────────────────────────────────
        void MapDist(UIElement distCtrl, int row)
        {
            if (!legControls.TryGetValue(0, out var leg1Controls)) return;
            var leg1Match = leg1Controls.FirstOrDefault(x => x.row == row);
            if (leg1Match.control != null)
                _distTabMap[distCtrl] = leg1Match.control;
        }

        MapDist(distStrike, RowStrike);
        MapDist(distExpiry, RowExpiry);
        MapDist(distNotional, RowNotional);
    }

    // ================================================================
    //  ADMIN ROWS
    // ================================================================

    private void BuildAdminRows(int legCount, int totalCols)
    {
        AddSectionHeaderRow(RowAdminSection, "📋  BOOKING DETAILS", totalCols);
        AddRowLabel(RowPortfolio, "Portfolio MX3", totalCols);
        AddRowLabel(RowTrader, "Trader", totalCols);
        AddRowLabel(RowExecutionTime, "Execution Time", totalCols);
        AddRowLabel(RowMic, "MIC", totalCols);
        AddRowLabel(RowTvtic, "TVTIC", totalCols);
        AddRowLabel(RowIsin, "ISIN", totalCols);
        AddRowLabel(RowSales, "Sales", totalCols);
        AddRowLabel(RowInvDecId, "Investment Decision ID", totalCols);
        AddRowLabel(RowBroker, "Broker", totalCols);
        AddRowLabel(RowMargin, "Margin", totalCols);
        AddRowLabel(RowReportingEntity, "Reporting Entity", totalCols);

        var legAdminControls = new Dictionary<int, List<(int row, UIElement control)>>();
        for (int i = 0; i < legCount; i++)
            legAdminControls[i] = [];

        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm!.Legs[i];
            int vc = LegValCol(i);
            var legAdminElements = new List<UIElement>();

            var portfolioCombo = CreateLegComboBox(leg, nameof(leg.PortfolioMX3), "ReferenceData.Portfolios");
            AddCell(RowPortfolio, vc, portfolioCombo);
            legAdminElements.Add(portfolioCombo);
            legAdminControls[i].Add((RowPortfolio, portfolioCombo));

            var traderCombo = CreateLegComboBox(leg, nameof(leg.Trader), "ReferenceData.Traders");
            AddCell(RowTrader, vc, traderCombo);
            legAdminElements.Add(traderCombo);
            legAdminControls[i].Add((RowTrader, traderCombo));

            var execTimeTb = CreateLegTextBox(leg, nameof(leg.ExecutionTime),
                trigger: UpdateSourceTrigger.LostFocus);
            execTimeTb.ToolTip = $"Format: {TradeLegViewModel.ExecutionTimeFormat} (UTC)";
            execTimeTb.LostFocus += (s, _) => { if (s is TextBox tb) leg.ApplyExecutionTimeInput(tb.Text); };
            execTimeTb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && s is TextBox tb)
                { leg.ApplyExecutionTimeInput(tb.Text); Keyboard.ClearFocus(); }
            };
            AddCell(RowExecutionTime, vc, execTimeTb);
            legAdminElements.Add(execTimeTb);
            legAdminControls[i].Add((RowExecutionTime, execTimeTb));

            var micCombo = CreateLegComboBox(leg, nameof(leg.Mic), "ReferenceData.MICs");
            AddCell(RowMic, vc, micCombo);
            legAdminElements.Add(micCombo);
            legAdminControls[i].Add((RowMic, micCombo));

            var tvticTb = CreateLegTextBox(leg, nameof(leg.Tvtic));
            AddCell(RowTvtic, vc, tvticTb);
            legAdminElements.Add(tvticTb);
            legAdminControls[i].Add((RowTvtic, tvticTb));

            var isinTb = CreateLegTextBox(leg, nameof(leg.Isin));
            AddCell(RowIsin, vc, isinTb);
            legAdminElements.Add(isinTb);
            legAdminControls[i].Add((RowIsin, isinTb));

            var salesCombo = CreateLegComboBox(leg, nameof(leg.Sales), "ReferenceData.SalesNames");
            AddCell(RowSales, vc, salesCombo);
            legAdminElements.Add(salesCombo);
            legAdminControls[i].Add((RowSales, salesCombo));

            var invDecIdCombo = CreateLegComboBox(leg, nameof(leg.InvestmentDecisionID), "ReferenceData.InvestmentDecisionIDs");
            AddCell(RowInvDecId, vc, invDecIdCombo);
            legAdminElements.Add(invDecIdCombo);
            legAdminControls[i].Add((RowInvDecId, invDecIdCombo));

            var brokerCombo = CreateLegComboBox(leg, nameof(leg.Broker), "ReferenceData.Brokers");
            AddCell(RowBroker, vc, brokerCombo);
            legAdminElements.Add(brokerCombo);
            legAdminControls[i].Add((RowBroker, brokerCombo));

            var marginTb = CreateLegTextBox(leg, nameof(leg.MarginText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            marginTb.ToolTip = "Enter margin (e.g. 5k, 100K, 2m). Comma → dot. 2 decimals.";
            AttachNumericHandlers(marginTb, leg.ApplyMarginInput);
            AddCell(RowMargin, vc, marginTb);
            legAdminElements.Add(marginTb);
            legAdminControls[i].Add((RowMargin, marginTb));

            var reportingCombo = CreateLegComboBox(leg, nameof(leg.ReportingEntity), "ReferenceData.ReportingEntities");
            AddCell(RowReportingEntity, vc, reportingCombo);
            legAdminElements.Add(reportingCombo);
            legAdminControls[i].Add((RowReportingEntity, reportingCombo));

            _adminElements.Add(legAdminElements);
        }

        var adminRows = legAdminControls.Values
            .SelectMany(l => l.Select(x => x.row))
            .Distinct().OrderBy(r => r).ToList();

        foreach (var row in adminRows)
            for (int i = 0; i < legCount; i++)
            {
                var match = legAdminControls[i].FirstOrDefault(x => x.row == row);
                if (match.control != null)
                    RegisterTabbable(match.control);
            }
    }

    // ================================================================
    //  HEDGE ROWS
    // ================================================================

    private void BuildHedgeRows(int legCount, int totalCols)
    {
        var sepBg = new Border { Background = FindBrush("BgSectionHeaderBrush") };
        Grid.SetRow(sepBg, RowHedgeSep); Grid.SetColumn(sepBg, 0);
        Grid.SetColumnSpan(sepBg, totalCols);
        RootGrid.Children.Add(sepBg);

        var sepLine = new Border
        {
            BorderBrush = FindBrush("AccentBlueBrush"),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Opacity = 0.7
        };
        Grid.SetRow(sepLine, RowHedgeSep); Grid.SetColumn(sepLine, 0);
        Grid.SetColumnSpan(sepLine, totalCols);
        RootGrid.Children.Add(sepLine);

        var hedgeSectionLabel = new TextBlock
        {
            Text = "⛊  HEDGE",
            Style = FindStyle<TextBlock>("SectionHeader"),
            Margin = new Thickness(12, 6, 0, 6)
        };
        Grid.SetRow(hedgeSectionLabel, RowHedgeSep);
        Grid.SetColumn(hedgeSectionLabel, ColLabel);
        RootGrid.Children.Add(hedgeSectionLabel);

        AddRowLabel(RowHedge, "Hedge Type", totalCols);
        AddRowLabel(RowHedgeBuySell, "Client Buy/Sell", totalCols, isHedgeDetail: true);
        AddRowLabel(RowHedgeNotional, "Notional", totalCols, isHedgeDetail: true);
        AddRowLabel(RowHedgeRate, "Hedge Rate", totalCols, isHedgeDetail: true);
        AddRowLabel(RowHedgeSettlement, "Settlement Date", totalCols, isHedgeDetail: true);
        AddRowLabel(RowBookCalypso, "Calypso Book", totalCols, isHedgeDetail: true);  // ← direkt under Settlement Date
        AddRowLabel(RowHedgeTvtic, "Hedge TVTIC", totalCols);
        AddRowLabel(RowHedgeUti, "Hedge UTI", totalCols);
        AddRowLabel(RowHedgeIsin, "Hedge ISIN", totalCols);

        AddDistToggle(RowHedgeBuySell, ColDistToggle, DistToggle_HedgeBuySell, "Flippa Hedge Buy/Sell på alla legs");

        var distHedgeNotional = CreateDistTextBox();
        distHedgeNotional.ToolTip = "Enter hedge notional. Press Enter to apply to all legs.";
        distHedgeNotional.PreviewTextInput += ReplaceCommaWithDot;
        distHedgeNotional.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            { _vm?.SetAllHedgeNotionalCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        distHedgeNotional.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            { _vm?.SetAllHedgeNotionalCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        AddCell(RowHedgeNotional, ColDistInput, distHedgeNotional);

        var distHedgeRate = CreateDistTextBox();
        distHedgeRate.ToolTip = "Enter hedge rate. Comma → dot. Press Enter to apply to all legs.";
        distHedgeRate.PreviewTextInput += ReplaceCommaWithDot;
        distHedgeRate.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            { _vm?.SetAllHedgeRateCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        distHedgeRate.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            { _vm?.SetAllHedgeRateCommand.Execute(tb.Text); tb.Text = string.Empty; }
        };
        AddCell(RowHedgeRate, ColDistInput, distHedgeRate);

        var legHedgeControls = new Dictionary<int, List<(int row, UIElement control, Func<bool>? condition)>>();
        for (int i = 0; i < legCount; i++)
            legHedgeControls[i] = [];

        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm!.Legs[i];
            int vc = LegValCol(i);
            var legHedgeElements = new List<UIElement>();
            var legHedgeAdminElements = new List<UIElement>();

            // Hedge Type — NOT in tab order
            var hedgeCombo = new ComboBox
            {
                Style = FindStyle("TradingComboBox"),
                IsEditable = false,
                ItemsSource = Enum.GetValues<HedgeType>(),
                Margin = new Thickness(2, 1, 2, 1)
            };
            hedgeCombo.SetBinding(ComboBox.SelectedItemProperty,
                new Binding(nameof(leg.Hedge))
                {
                    Source = leg,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            AddCell(RowHedge, vc, hedgeCombo);

            // Hedge Buy/Sell — read-only toggle, skip in tab order
            var hBuySellToggle = CreateHedgeBuySellToggle(leg);
            AddCell(RowHedgeBuySell, vc, hBuySellToggle);
            legHedgeElements.Add(hBuySellToggle);

            var hNotional = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.HedgeNotionalText),
                leg, nameof(leg.HedgeNotionalCurrency), () =>
                {
                    leg.HedgeNotionalCurrency = leg.HedgeNotionalCurrency == leg.BaseCurrency
                        ? leg.QuoteCurrency : leg.BaseCurrency;
                });
            hNotional.ToolTip = "Enter hedge notional (e.g. 5m, 100k, 2b, 1y or plain number). Comma → dot.";
            AttachNumericHandlers(hNotional, leg.ApplyHedgeNotionalInput);
            AddCell(RowHedgeNotional, vc, hNotional);
            legHedgeElements.Add(hNotional);
            legHedgeControls[i].Add((RowHedgeNotional, hNotional, () => leg.HasHedge));

            var hRate = CreateLegTextBox(leg, nameof(leg.HedgeRateText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            hRate.ToolTip = "Numeric only. Comma → dot. Same formatting as Strike.";
            AttachNumericHandlers(hRate, leg.ApplyHedgeRateInput);
            AddCell(RowHedgeRate, vc, hRate);
            legHedgeElements.Add(hRate);
            legHedgeControls[i].Add((RowHedgeRate, hRate, () => leg.HasHedge));

            // Hedge Settlement Date — read-only, skip in tab order
            var hSettlement = CreateLegTextBox(leg, nameof(leg.HedgeSettlementDate),
                stringFormat: "yyyy-MM-dd", isReadOnly: true);
            AddCell(RowHedgeSettlement, vc, hSettlement);
            legHedgeElements.Add(hSettlement);

            // Calypso Book — visible only when Admin + Hedge active
            var hBook = CreateLegComboBox(leg, nameof(leg.BookCalypso), "ReferenceData.CalypsoBooks");
            AddCell(RowBookCalypso, vc, hBook);
            legHedgeAdminElements.Add(hBook);
            legHedgeControls[i].Add((RowBookCalypso, hBook, () => leg.HasHedge));

            var hTvtic = CreateLegTextBox(leg, nameof(leg.HedgeTVTIC));
            AddCell(RowHedgeTvtic, vc, hTvtic);
            legHedgeAdminElements.Add(hTvtic);
            legHedgeControls[i].Add((RowHedgeTvtic, hTvtic, () => leg.HasHedge));

            var hUti = CreateLegTextBox(leg, nameof(leg.HedgeUTI));
            AddCell(RowHedgeUti, vc, hUti);
            legHedgeAdminElements.Add(hUti);
            legHedgeControls[i].Add((RowHedgeUti, hUti, () => leg.HasHedge));

            var hIsin = CreateLegTextBox(leg, nameof(leg.HedgeISIN));
            AddCell(RowHedgeIsin, vc, hIsin);
            legHedgeAdminElements.Add(hIsin);
            legHedgeControls[i].Add((RowHedgeIsin, hIsin, () => leg.HasHedge));

            _hedgeDetailElements.Add(legHedgeElements);
            _hedgeAdminElements.Add(legHedgeAdminElements);
        }

        var hedgeRows = legHedgeControls.Values
            .SelectMany(l => l.Select(x => x.row))
            .Distinct().OrderBy(r => r).ToList();

        foreach (var row in hedgeRows)
            for (int i = 0; i < legCount; i++)
            {
                var match = legHedgeControls[i].FirstOrDefault(x => x.row == row);
                if (match.control != null)
                {
                    if (match.condition != null)
                        RegisterTabbableIf(match.control, match.condition);
                    else
                        RegisterTabbable(match.control);
                }
            }

        // Dist → Leg 1 map for hedge distributor inputs
        void MapDistHedge(UIElement distCtrl, int row)
        {
            if (!legHedgeControls.TryGetValue(0, out var leg1Controls)) return;
            var leg1Match = leg1Controls.FirstOrDefault(x => x.row == row);
            if (leg1Match.control != null)
                _distTabMap[distCtrl] = leg1Match.control;
        }

        MapDistHedge(distHedgeNotional, RowHedgeNotional);
        MapDistHedge(distHedgeRate, RowHedgeRate);
    }

    // ================================================================
    //  HEDGE BUY/SELL TOGGLE
    // ================================================================

    private Grid CreateHedgeBuySellToggle(TradeLegViewModel leg)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var buyRadio = new RadioButton
        {
            Content = "BUY",
            GroupName = $"hbs_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleBuy")
        };
        buyRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.HedgeBuySell))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Buy"
            });
        Grid.SetColumn(buyRadio, 0);

        var sellRadio = new RadioButton
        {
            Content = "SELL",
            GroupName = $"hbs_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleSell"),
            Margin = new Thickness(-1, 0, 0, 0)
        };
        sellRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.HedgeBuySell))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Sell"
            });
        Grid.SetColumn(sellRadio, 1);

        grid.Children.Add(buyRadio);
        grid.Children.Add(sellRadio);
        return grid;
    }

    // ================================================================
    //  HEDGE VISIBILITY
    // ================================================================

    private void UpdateHedgeVisibility()
    {
        if (_vm == null) return;
        bool anyHasHedge = _vm.Legs.Any(l => l.HasHedge);

        foreach (var child in RootGrid.Children.OfType<UIElement>())
        {
            int row = Grid.GetRow(child);
            if (HedgeDetailRows.Contains(row) && Grid.GetColumn(child) <= ColDistToggle)
                child.Visibility = anyHasHedge ? Visibility.Visible : Visibility.Collapsed;
        }

        for (int i = 0; i < _vm.Legs.Count && i < _hedgeDetailElements.Count; i++)
        {
            var vis = _vm.Legs[i].HasHedge ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in _hedgeDetailElements[i])
                el.Visibility = vis;
        }

        foreach (var child in RootGrid.Children.OfType<UIElement>())
        {
            int row = Grid.GetRow(child);
            if (HedgeDetailRows.Contains(row) && Grid.GetColumn(child) == ColDistInput)
                child.Visibility = anyHasHedge ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ================================================================
    //  ADMIN VISIBILITY
    // ================================================================

    private void UpdateAdminVisibility()
    {
        if (_vm == null) return;
        bool showAdmin = _vm.ShowAdminRows;
        bool anyHasHedge = _vm.Legs.Any(l => l.HasHedge);

        foreach (var child in RootGrid.Children.OfType<UIElement>())
        {
            int row = Grid.GetRow(child);
            int col = Grid.GetColumn(child);

            if (AdminRows.Contains(row) && col <= ColDistToggle)
                child.Visibility = showAdmin ? Visibility.Visible : Visibility.Collapsed;

            if (HedgeAdminRows.Contains(row) && col <= ColDistToggle)
                child.Visibility = showAdmin && anyHasHedge ? Visibility.Visible : Visibility.Collapsed;
        }

        for (int i = 0; i < _vm.Legs.Count && i < _adminElements.Count; i++)
        {
            var vis = showAdmin ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in _adminElements[i])
                el.Visibility = vis;
        }

        for (int i = 0; i < _vm.Legs.Count && i < _hedgeAdminElements.Count; i++)
        {
            var vis = showAdmin && _vm.Legs[i].HasHedge ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in _hedgeAdminElements[i])
                el.Visibility = vis;
        }
    }

    // ================================================================
    //  SEGMENTED TOGGLE FACTORIES
    // ================================================================

    private Grid CreateBuySellToggle(TradeLegViewModel leg)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var buyRadio = new RadioButton
        {
            Content = "BUY",
            GroupName = $"bs_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleBuy")
        };
        buyRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.BuySell))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Buy"
            });
        Grid.SetColumn(buyRadio, 0);

        var sellRadio = new RadioButton
        {
            Content = "SELL",
            GroupName = $"bs_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleSell"),
            Margin = new Thickness(-1, 0, 0, 0)
        };
        sellRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.BuySell))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Sell"
            });
        Grid.SetColumn(sellRadio, 1);

        grid.Children.Add(buyRadio);
        grid.Children.Add(sellRadio);
        return grid;
    }

    private Grid CreateCallPutToggle(TradeLegViewModel leg)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var callRadio = new RadioButton
        {
            Content = "CALL",
            GroupName = $"cp_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleLeft")
        };
        callRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.CallPut))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Call"
            });
        Grid.SetColumn(callRadio, 0);

        var putRadio = new RadioButton
        {
            Content = "PUT",
            GroupName = $"cp_grid_{leg.LegNumber}",
            Style = FindStyle<RadioButton>("SegmentedToggleRight"),
            Margin = new Thickness(-1, 0, 0, 0)
        };
        putRadio.SetBinding(RadioButton.IsCheckedProperty,
            new Binding(nameof(leg.CallPut))
            {
                Source = leg,
                Converter = (IValueConverter)FindResource("EnumToBool"),
                ConverterParameter = "Put"
            });
        Grid.SetColumn(putRadio, 1);

        grid.Children.Add(callRadio);
        grid.Children.Add(putRadio);
        return grid;
    }

    // ================================================================
    //  DISTRIBUTOR TOGGLE HANDLERS
    // ================================================================

    private void DistToggle_BuySell(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        foreach (var leg in _vm.Legs)
            leg.BuySell = leg.BuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
    }

    private void DistToggle_CallPut(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        foreach (var leg in _vm.Legs)
            leg.CallPut = leg.CallPut == CallPut.Call ? CallPut.Put : CallPut.Call;
    }

    private void DistToggle_PremiumStyle(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        foreach (var leg in _vm.Legs)
            leg.TogglePremiumStyleCommand.Execute(null);
    }

    private void DistToggle_PremiumDate(object sender, RoutedEventArgs e)
    {
        foreach (var leg in _vm!.Legs)
            leg.TogglePremiumDateTypeCommand.Execute(null);
    }

    private void DistToggle_HedgeBuySell(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        foreach (var leg in _vm.Legs)
            leg.HedgeBuySell = leg.HedgeBuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
    }

    private void DistToggle_HedgeNotionalCcy(object sender, RoutedEventArgs e)
    {
        foreach (var leg in _vm!.Legs)
            leg.HedgeNotionalCurrency = leg.HedgeNotionalCurrency == leg.BaseCurrency
                ? leg.QuoteCurrency : leg.BaseCurrency;
    }

    // ================================================================
    //  GRID HELPERS
    // ================================================================

    private void AddCell(int row, int col, UIElement element)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        RootGrid.Children.Add(element);
    }

    private void AddRowLabel(int row, string text, int totalCols,
        string? badgeText = null, bool isHedgeDetail = false)
    {
        if (badgeText != null)
        {
            var labelPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 3, 4, 3)
            };
            labelPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = FindBrush("TextSecondaryBrush"),
                FontFamily = FindFont("MainFont"),
                FontWeight = FontWeights.Medium,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            var badge = new Border
            {
                Style = FindStyle<Border>("PremiumBadge"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badgeText,
                    FontSize = 9,
                    Foreground = FindBrush("TextDimmedBrush"),
                    FontFamily = FindFont("MonoFont")
                }
            };
            labelPanel.Children.Add(badge);
            AddCell(row, ColLabel, labelPanel);
        }
        else
        {
            AddCell(row, ColLabel, new TextBlock
            {
                Text = text,
                Foreground = FindBrush("TextSecondaryBrush"),
                FontFamily = FindFont("MainFont"),
                FontWeight = FontWeights.Medium,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 3, 4, 3)
            });
        }
    }

    private void AddDistToggle(int row, int col, RoutedEventHandler handler, string tooltip)
    {
        var btn = new Button
        {
            Content = "⇄",
            Style = FindStyle<Button>("DistFlipButton"),
            ToolTip = tooltip
        };
        btn.Click += handler;
        AddCell(row, col, btn);
    }

    // ================================================================
    //  FACTORY: Distributor column
    // ================================================================

    private TextBox CreateDistTextBox() => new()
    {
        Style = FindStyle<TextBox>("TradingTextBox"),
        Margin = new Thickness(2, 1, 2, 1),
        ToolTip = "Press Enter to apply to all legs"
    };

    private ComboBox CreateDistComboBox(string itemsSourcePath)
    {
        var cb = new ComboBox
        {
            Style = FindStyle<ComboBox>("TradingComboBox"),
            Margin = new Thickness(2, 1, 2, 1)
        };
        cb.SetBinding(ComboBox.ItemsSourceProperty,
            new Binding(itemsSourcePath) { Source = _vm });
        return cb;
    }

    // ================================================================
    //  FACTORY: Leg column controls
    // ================================================================

    private TextBox CreateLegTextBox(TradeLegViewModel leg, string path,
        string? stringFormat = null, bool isReadOnly = false,
        UpdateSourceTrigger trigger = UpdateSourceTrigger.PropertyChanged)
    {
        var tb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            IsReadOnly = isReadOnly,
            Margin = new Thickness(2, 1, 2, 1)
        };
        var binding = new Binding(path)
        {
            Source = leg,
            UpdateSourceTrigger = trigger,
            Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay
        };
        if (stringFormat != null) binding.StringFormat = stringFormat;
        tb.SetBinding(TextBox.TextProperty, binding);
        return tb;
    }

    private TextBox CreateLegTextBoxWithInlineSuffix(TradeLegViewModel leg, string textPath,
        TradeLegViewModel suffixSource, string suffixPath, Action onToggle,
        string? stringFormat = null, bool isReadOnly = false,
        UpdateSourceTrigger trigger = UpdateSourceTrigger.PropertyChanged)
    {
        var suffixBtn = new Button
        {
            Style = FindStyle<Button>("InlineSuffixToggle"),
            ToolTip = "Toggle"
        };
        suffixBtn.SetBinding(Button.ContentProperty,
            new Binding(suffixPath) { Source = suffixSource });
        suffixBtn.Click += (_, _) => onToggle();

        var tb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBoxWithSuffix"),
            IsReadOnly = isReadOnly,
            Margin = new Thickness(2, 1, 2, 1),
            Tag = suffixBtn
        };
        var binding = new Binding(textPath)
        {
            Source = leg,
            UpdateSourceTrigger = trigger,
            Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay
        };
        if (stringFormat != null) binding.StringFormat = stringFormat;
        tb.SetBinding(TextBox.TextProperty, binding);
        return tb;
    }

    private ComboBox CreateLegComboBox(TradeLegViewModel leg, string valuePath, string itemsSourcePath)
    {
        var cb = new ComboBox
        {
            Style = FindStyle<ComboBox>("TradingComboBox"),
            Margin = new Thickness(2, 1, 2, 1)
        };
        cb.SetBinding(ComboBox.TextProperty,
            new Binding(valuePath)
            {
                Source = leg,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        cb.SetBinding(ComboBox.ItemsSourceProperty,
            new Binding(itemsSourcePath) { Source = _vm });
        return cb;
    }

    private TextBlock CreateCurrencyLabel(TradeLegViewModel leg, string path)
    {
        var label = new TextBlock
        {
            FontFamily = FindFont("MonoFont"),
            FontSize = 12,
            Foreground = FindBrush("AccentBlueBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 2, 0, 2)
        };
        label.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = leg });
        return label;
    }

    private DockPanel CreateCurrencyLabelWithToggle(TradeLegViewModel leg, string path, Action onToggle)
    {
        var panel = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
        var btn = new Button
        {
            Content = "↻",
            Style = FindStyle<Button>("CircularToggleButton"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Toggle",
            Margin = new Thickness(2, 0, 0, 0)
        };
        btn.Click += (_, _) => onToggle();
        DockPanel.SetDock(btn, Dock.Right);
        panel.Children.Add(btn);

        var label = new TextBlock
        {
            FontFamily = FindFont("MonoFont"),
            FontSize = 12,
            Foreground = FindBrush("AccentBlueBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        label.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = leg });
        panel.Children.Add(label);
        return panel;
    }

    private Button CreateIconButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Style = FindStyle<Button>("IconButton"),
        Margin = new Thickness(3, 0, 0, 0)
    };

    // ================================================================
    //  RESOURCE HELPERS
    // ================================================================

    private SolidColorBrush FindBrush(string key)
        => (SolidColorBrush)(FindResource(key) ?? Brushes.Gray);

    private FontFamily FindFont(string key)
        => (FontFamily)(FindResource(key) ?? new FontFamily("Segoe UI"));

    private Style? FindStyle(string key) => FindResource(key) as Style;
    private Style? FindStyle<T>(string key) => FindResource(key) as Style;

    // ================================================================
    //  CONVERTERS
    // ================================================================

    private sealed class BrushKeyConverter(FrameworkElement owner) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key && owner.TryFindResource(key) is SolidColorBrush brush)
                return brush;
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private sealed class OrBoolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var v in values)
                if (v is true) return true;
            return false;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}