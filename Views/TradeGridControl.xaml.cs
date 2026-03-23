using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.ViewModels;

namespace FxTradeConfirmation.Views;

public partial class TradeGridControl : UserControl
{
    // Row indices — Section headers + Option fields
    private const int RowHeader = 0;
    private const int RowTradeSection = 1;     // "TRADE DETAILS" section header
    private const int RowCounterpart = 2;
    private const int RowCurrencyPair = 3;
    private const int RowBuySell = 4;
    private const int RowOptionSection = 5;    // "OPTION DETAILS" section header
    private const int RowCallPut = 6;
    private const int RowStrike = 7;
    private const int RowExpiry = 8;
    private const int RowSettlement = 9;
    private const int RowCut = 10;
    private const int RowAmountSection = 11;   // "AMOUNTS & PREMIUM" section header
    private const int RowNotional = 12;
    private const int RowPremium = 13;
    private const int RowPremiumAmount = 14;
    private const int RowPremiumDate = 15;
    // Hedge section
    private const int RowHedgeSep = 16;
    private const int RowHedge = 17;
    private const int RowHedgeBuySell = 18;
    private const int RowHedgeNotional = 19;
    private const int RowHedgeNotionalCcy = 20;
    private const int RowHedgeRate = 21;
    private const int RowHedgeSettlement = 22;
    private const int TotalRows = 23;

    // Hedge detail rows (collapse/expand based on hedge type)
    private static readonly int[] HedgeDetailRows =
        [RowHedgeBuySell, RowHedgeNotional, RowHedgeRate, RowHedgeSettlement];

    private const int ColDistInput = 0;
    private const int ColLabel = 1;
    private const int ColDistToggle = 2;

    private MainViewModel? _vm;

    // Track hedge detail elements per leg for collapse/expand
    private readonly List<List<UIElement>> _hedgeDetailElements = [];

    // Track distributor combos that need clearing
    private ComboBox? _distCounterpart;
    private ComboBox? _distCcyPair;
    private ComboBox? _distCut;

    public TradeGridControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Select-all text when any TextBox in the grid receives focus via mouse click
        EventManager.RegisterClassHandler(typeof(TextBox), GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnTextBoxGotKeyboardFocus));
        EventManager.RegisterClassHandler(typeof(TextBox), PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnTextBoxPreviewMouseLeftButtonDown));
    }

    /// <summary>
    /// When a TextBox gains keyboard focus, select all text for faster user input.
    /// </summary>
    private static void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox tb && !tb.IsReadOnly)
            tb.SelectAll();
    }

    /// <summary>
    /// When clicking a TextBox that isn't already focused, give it focus (which triggers select-all)
    /// instead of placing the caret at the click position.
    /// </summary>
    private static void OnTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var tb = FindParent<TextBox>(source);
        if (tb == null || tb.IsReadOnly || tb.IsKeyboardFocusWithin) return;

        tb.Focus();
        e.Handled = true;
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

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Legs.CollectionChanged -= OnLegsChanged;
            oldVm.DistributorClearRequested -= OnDistributorClearRequested;
            oldVm.SolvingDialogRequested -= OnSolvingDialogRequested;
            foreach (var leg in oldVm.Legs)
                leg.PropertyChanged -= OnLegPropertyChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            _vm = newVm;
            _vm.Legs.CollectionChanged += OnLegsChanged;
            _vm.DistributorClearRequested += OnDistributorClearRequested;
            _vm.SolvingDialogRequested += OnSolvingDialogRequested;
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

    /// <summary>
    /// When solve mode starts, immediately show the SolvingDialog.
    /// </summary>
    /// <summary>
    /// When solve mode starts, immediately show the SolvingDialog.
    /// </summary>
    private void OnSolvingDialogRequested(bool isByAmount, string unitDisplay)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var ownerWindow = Window.GetWindow(this);
            var dialog = new SolvingDialog(isByAmount, unitDisplay) { Owner = ownerWindow };

            // Wire up the solve callback so validation errors stay in the dialog
            dialog.SolveCallback = (target, payReceive) => _vm?.SolvePremium(target, payReceive);

            if (dialog.ShowDialog() == true)
            {
                // Solve already applied inside the callback — nothing more to do.
            }
            else
            {
                _vm?.CancelSolvingCommand.Execute(null);
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
            UpdateHedgeVisibility();
    }

    private static int LegValCol(int legIndex) => 3 + legIndex * 2;
    private static int LegTogCol(int legIndex) => 4 + legIndex * 2;

    // ================================================================
    //  Comma → Dot real-time replacement for numeric TextBoxes
    // ================================================================

    /// <summary>
    /// Intercepts comma keypress and replaces it with a dot in the TextBox.
    /// Attach via PreviewTextInput on any numeric input field.
    /// </summary>
    private static void ReplaceCommaWithDot(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ",")
        {
            e.Handled = true;
            if (sender is TextBox tb)
            {
                int selStart = tb.SelectionStart;
                int selLen = tb.SelectionLength;
                string before = tb.Text[..selStart];
                string after = tb.Text[(selStart + selLen)..];
                tb.Text = before + "." + after;
                tb.CaretIndex = selStart + 1;
            }
        }
    }

    /// <summary>
    /// Attaches comma→dot, LostFocus and Enter handlers to a numeric TextBox.
    /// </summary>
    private static void AttachNumericHandlers(TextBox tb, Action<string> applyAction)
    {
        tb.PreviewTextInput += ReplaceCommaWithDot;
        tb.LostFocus += (s, _) =>
        {
            if (s is TextBox t) applyAction(t.Text);
        };
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
    //  GRID REBUILD
    // ================================================================
    private void RebuildGrid()
    {
        if (_vm == null) return;

        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        _hedgeDetailElements.Clear();

        int legCount = _vm.Legs.Count;

        for (int i = 0; i < TotalRows; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        for (int i = 0; i < legCount; i++)
        {
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        int totalCols = RootGrid.ColumnDefinitions.Count;

        // Header row background
        var headerBg = new Border { Background = FindBrush("BgHeaderRowBrush") };
        Grid.SetRow(headerBg, RowHeader);
        Grid.SetColumn(headerBg, 0);
        Grid.SetColumnSpan(headerBg, totalCols);
        RootGrid.Children.Add(headerBg);

        // Label column background
        var labelColBg = new Border { Background = FindBrush("BgLabelColumnBrush") };
        Grid.SetRow(labelColBg, 0);
        Grid.SetColumn(labelColBg, ColLabel);
        Grid.SetColumnSpan(labelColBg, 2);
        Grid.SetRowSpan(labelColBg, TotalRows);
        RootGrid.Children.Add(labelColBg);

        // Label column right border
        var labelColBorder = new Border
        {
            BorderBrush = FindBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(labelColBorder, 0);
        Grid.SetColumn(labelColBorder, ColLabel);
        Grid.SetColumnSpan(labelColBorder, 2);
        Grid.SetRowSpan(labelColBorder, TotalRows);
        RootGrid.Children.Add(labelColBorder);

        // Distributor header
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
        Grid.SetRow(distHeader, RowHeader);
        Grid.SetColumn(distHeader, ColDistInput);
        RootGrid.Children.Add(distHeader);

        // Leg headers
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
            Grid.SetColumnSpan(headerPanel, 2);
            RootGrid.Children.Add(headerPanel);
        }

        AddSectionHeaderRow(RowTradeSection, "📋  TRADE DETAILS", totalCols);
        AddSectionHeaderRow(RowOptionSection, "📊  OPTION DETAILS", totalCols);
        AddSectionHeaderRow(RowAmountSection, "💰  AMOUNTS & PREMIUM", totalCols);

        BuildOptionRows(legCount, totalCols);
        BuildHedgeRows(legCount, totalCols);
        UpdateHedgeVisibility();
    }

    // ================================================================
    //  SECTION HEADER ROW
    // ================================================================
    private void AddSectionHeaderRow(int row, string text, int totalCols)
    {
        // Background
        var bg = new Border
        {
            Background = FindBrush("BgSectionHeaderBrush")
        };
        Grid.SetRow(bg, row);
        Grid.SetColumn(bg, 0);
        Grid.SetColumnSpan(bg, totalCols);
        RootGrid.Children.Add(bg);

        // Top accent line
        var line = new Border
        {
            BorderBrush = FindBrush("AccentBlueBrush"),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Opacity = 0.5
        };
        Grid.SetRow(line, row);
        Grid.SetColumn(line, 0);
        Grid.SetColumnSpan(line, totalCols);
        RootGrid.Children.Add(line);

        // Label text
        var label = new TextBlock
        {
            Text = text,
            Style = FindStyle<TextBlock>("SectionHeader"),
            Margin = new Thickness(12, 6, 0, 6)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, ColLabel);
        Grid.SetColumnSpan(label, 2);
        RootGrid.Children.Add(label);
    }

    // ================================================================
    //  OPTION ROWS
    // ================================================================
    private void BuildOptionRows(int legCount, int totalCols)
    {
        // Trade Details section
        AddRowLabel(RowCounterpart, "Counterpart", totalCols);
        AddRowLabel(RowCurrencyPair, "Currency Pair", totalCols);
        AddRowLabel(RowBuySell, "Client Buy/Sell", totalCols);

        // Option Details section
        AddRowLabel(RowCallPut, "Call/Put", totalCols);
        AddRowLabel(RowStrike, "Strike", totalCols);
        AddRowLabel(RowExpiry, "Expiry Date", totalCols);
        AddRowLabel(RowSettlement, "Settlement Date", totalCols);
        AddRowLabel(RowCut, "Cut", totalCols);

        // Amounts & Premium section
        AddRowLabel(RowNotional, "Notional", totalCols);
        AddRowLabel(RowPremium, "Premium", totalCols);
        AddRowLabel(RowPremiumAmount, "Premium Amount", totalCols);
        AddRowLabel(RowPremiumDate, "Premium Date", totalCols, badgeText: "Spot");

        // Distributor inputs (col 0) — selection clears after distributing
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
            {
                _vm?.SetAllStrikeCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        distStrike.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _vm?.SetAllStrikeCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        AddCell(RowStrike, ColDistInput, distStrike);

        var distExpiry = CreateDistTextBox();
        distExpiry.ToolTip = "Enter tenor (on, 1d, 1w, 1m, 1y) or date (dd/MM, dd-MMM). Press Enter.";
        distExpiry.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            {
                _vm?.SetAllExpiryCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        distExpiry.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _vm?.SetAllExpiryCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
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
        distNotional.ToolTip = "Enter notional (e.g. 5m, 100k, 2b). Press Enter to apply to all legs.";
        distNotional.PreviewTextInput += ReplaceCommaWithDot;
        distNotional.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            {
                _vm?.SetAllNotionalCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        distNotional.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _vm?.SetAllNotionalCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        AddCell(RowNotional, ColDistInput, distNotional);

        // --- Premium summary in distributor column (read-only totals) ---
        var brushConverter = new BrushKeyConverter(this);

        var totalPremiumStyleTb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            IsReadOnly = true,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 1, 2, 1)
        };
        totalPremiumStyleTb.SetBinding(TextBox.TextProperty,
            new Binding(nameof(MainViewModel.TotalPremiumStyleDisplay)) { Source = _vm });
        totalPremiumStyleTb.SetBinding(TextBox.ForegroundProperty,
            new Binding(nameof(MainViewModel.TotalPremiumBrushKey))
            {
                Source = _vm,
                Converter = brushConverter
            });
        AddCell(RowPremium, ColDistInput, totalPremiumStyleTb);

        var totalPremiumAmountTb = new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            IsReadOnly = true,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 1, 2, 1)
        };
        totalPremiumAmountTb.SetBinding(TextBox.TextProperty,
            new Binding(nameof(MainViewModel.TotalPremiumAmountDisplay)) { Source = _vm });
        totalPremiumAmountTb.SetBinding(TextBox.ForegroundProperty,
            new Binding(nameof(MainViewModel.TotalPremiumBrushKey))
            {
                Source = _vm,
                Converter = brushConverter
            });
        AddCell(RowPremiumAmount, ColDistInput, totalPremiumAmountTb);

        // Leg columns
        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm!.Legs[i];
            int vc = LegValCol(i);
            bool isReadOnlyLeg = !leg.IsFirstLeg; // Legs 2+ are read-only for Counterpart/CurrencyPair

            // Counterpart: Leg 1 = editable combo, Legs 2+ = read-only text
            if (isReadOnlyLeg)
            {
                AddCell(RowCounterpart, vc, CreateLegTextBox(leg, nameof(leg.Counterpart), isReadOnly: true));
                AddCell(RowCurrencyPair, vc, CreateLegTextBox(leg, nameof(leg.CurrencyPair), isReadOnly: true));
            }
            else
            {
                AddCell(RowCounterpart, vc, CreateLegComboBox(leg, nameof(leg.Counterpart), "ReferenceData.Counterparts"));
                AddCell(RowCurrencyPair, vc, CreateLegComboBox(leg, nameof(leg.CurrencyPair), "ReferenceData.CurrencyPairs"));
            }

            // Buy/Sell segmented toggle
            AddCell(RowBuySell, vc, CreateBuySellToggle(leg));

            // Call/Put segmented toggle
            AddCell(RowCallPut, vc, CreateCallPutToggle(leg));

            // Strike — validated/formatted on LostFocus/Enter via ApplyStrikeInput
            var strikeTb = CreateLegTextBox(leg, nameof(leg.StrikeText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            strikeTb.ToolTip = "Numeric only. Comma → dot. Formatted to 5 decimals (3 for JPY pairs).";
            AttachNumericHandlers(strikeTb, leg.ApplyStrikeInput);
            AddCell(RowStrike, vc, strikeTb);

            // Expiry Date — user types tenor/date into ExpiryText, resolved on LostFocus
            var expiryTb = CreateLegTextBox(leg, nameof(leg.ExpiryText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            expiryTb.ToolTip = "Enter tenor (on, o/n, 1d, 1w, 1m, 1y) or date (dd/MM, dd-MMM, dd/MM/yyyy)";
            expiryTb.LostFocus += (s, _) =>
            {
                if (s is TextBox tb)
                    leg.ApplyExpiryInput(tb.Text);
            };
            expiryTb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && s is TextBox tb)
                {
                    leg.ApplyExpiryInput(tb.Text);
                    Keyboard.ClearFocus();
                }
            };
            AddCell(RowExpiry, vc, expiryTb);

            // Settlement Date — read-only, auto-calculated
            AddCell(RowSettlement, vc, CreateLegTextBox(leg, nameof(leg.SettlementDate),
                stringFormat: "yyyy-MM-dd", isReadOnly: true));

            AddCell(RowCut, vc, CreateLegComboBox(leg, nameof(leg.Cut), "ReferenceData.Cuts"));

            // Notional — inline currency suffix toggle, validated/formatted on LostFocus/Enter
            var notionalTb = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.NotionalText),
                leg, nameof(leg.NotionalCurrency), () => leg.ToggleNotionalCurrencyCommand.Execute(null));
            notionalTb.ToolTip = "Enter notional (e.g. 5m, 100k, 2b, 1y or plain number). Comma → dot.";
            AttachNumericHandlers(notionalTb, leg.ApplyNotionalInput);
            AddCell(RowNotional, vc, notionalTb);

            // Premium — inline style toggle suffix, disabled until notional+strike present
            // Bind IsReadOnly to IsPremiumReadOnly for solve mode
            var premiumTb = CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.PremiumText),
                leg, nameof(leg.PremiumStyleDisplay), () => leg.TogglePremiumStyleCommand.Execute(null));
            premiumTb.SetBinding(TextBox.IsEnabledProperty,
                new Binding(nameof(leg.PremiumInputEnabled)) { Source = leg });
            premiumTb.SetBinding(TextBox.IsReadOnlyProperty,
                new Binding(nameof(leg.IsPremiumReadOnly)) { Source = leg });
            AttachNumericHandlers(premiumTb, leg.ApplyPremiumInput);
            AddCell(RowPremium, vc, premiumTb);

            // Premium Amount — inline currency suffix toggle, disabled until notional+strike present
            // Bind IsReadOnly to IsPremiumLocked OR IsPremiumAmountReadOnly
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

            // Premium Date — inline type badge suffix
            AddCell(RowPremiumDate, vc, CreateLegTextBoxWithInlineSuffix(leg, nameof(leg.PremiumDate),
                leg, nameof(leg.PremiumDateType), () => leg.TogglePremiumDateTypeCommand.Execute(null),
                stringFormat: "yyyy-MM-dd", isReadOnly: true));
        }
    }

    // ================================================================
    //  HEDGE ROWS
    // ================================================================
    private void BuildHedgeRows(int legCount, int totalCols)
    {
        // Hedge separator — full section header
        var sepBg = new Border
        {
            Background = FindBrush("BgSectionHeaderBrush")
        };
        Grid.SetRow(sepBg, RowHedgeSep);
        Grid.SetColumn(sepBg, 0);
        Grid.SetColumnSpan(sepBg, totalCols);
        RootGrid.Children.Add(sepBg);

        var sepLine = new Border
        {
            BorderBrush = FindBrush("AccentBlueBrush"),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Opacity = 0.7
        };
        Grid.SetRow(sepLine, RowHedgeSep);
        Grid.SetColumn(sepLine, 0);
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

        AddDistToggle(RowHedgeBuySell, ColDistToggle, DistToggle_HedgeBuySell, "Flippa Hedge Buy/Sell på alla legs");

        // Distributor for hedge notional — clears after input
        var distHedgeNotional = CreateDistTextBox();
        distHedgeNotional.ToolTip = "Enter hedge notional (e.g. 5m, 100k, 2b). Press Enter to apply to all legs.";
        distHedgeNotional.PreviewTextInput += ReplaceCommaWithDot;
        distHedgeNotional.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            {
                _vm?.SetAllHedgeNotionalCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        distHedgeNotional.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _vm?.SetAllHedgeNotionalCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        AddCell(RowHedgeNotional, ColDistInput, distHedgeNotional);

        // Distributor for hedge rate — same behaviour as Strike distributor:
        // validate input, format, populate all legs, then clear.
        var distHedgeRate = CreateDistTextBox();
        distHedgeRate.ToolTip = "Enter hedge rate. Comma → dot. Press Enter to apply to all legs.";
        distHedgeRate.PreviewTextInput += ReplaceCommaWithDot;
        distHedgeRate.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox tb)
            {
                _vm?.SetAllHedgeRateCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        distHedgeRate.LostFocus += (s, _) =>
        {
            if (s is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _vm?.SetAllHedgeRateCommand.Execute(tb.Text);
                tb.Text = string.Empty;
            }
        };
        AddCell(RowHedgeRate, ColDistInput, distHedgeRate);

        for (int i = 0; i < legCount; i++)
        {
            var leg = _vm!.Legs[i];
            int vc = LegValCol(i);

            var legHedgeElements = new List<UIElement>();

            // Hedge type combo — uses enum values directly so binding survives grid rebuilds
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

            // Hedge Buy/Sell — segmented toggle (same style as option)
            var hBuySellToggle = CreateHedgeBuySellToggle(leg);
            AddCell(RowHedgeBuySell, vc, hBuySellToggle);
            legHedgeElements.Add(hBuySellToggle);

            // Hedge Notional — with inline currency suffix toggle, validated/formatted on LostFocus/Enter
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

            // Hedge Rate — same behaviour as Strike: comma→dot, revert on invalid, formatted decimals
            var hRate = CreateLegTextBox(leg, nameof(leg.HedgeRateText),
                trigger: UpdateSourceTrigger.PropertyChanged);
            hRate.ToolTip = "Numeric only. Comma → dot. Same formatting as Strike.";
            AttachNumericHandlers(hRate, leg.ApplyHedgeRateInput);
            AddCell(RowHedgeRate, vc, hRate);
            legHedgeElements.Add(hRate);

            // Hedge Settlement Date — read-only, auto-calculated
            var hSettlement = CreateLegTextBox(leg, nameof(leg.HedgeSettlementDate),
                stringFormat: "yyyy-MM-dd", isReadOnly: true);
            AddCell(RowHedgeSettlement, vc, hSettlement);
            legHedgeElements.Add(hSettlement);

            _hedgeDetailElements.Add(legHedgeElements);
        }
    }

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

        // Show/hide hedge detail row backgrounds and labels
        foreach (var child in RootGrid.Children.OfType<UIElement>())
        {
            int row = Grid.GetRow(child);
            if (HedgeDetailRows.Contains(row))
            {
                int col = Grid.GetColumn(child);
                if (col <= ColDistToggle)
                {
                    child.Visibility = anyHasHedge ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // Per-leg hedge detail elements
        for (int i = 0; i < _vm.Legs.Count && i < _hedgeDetailElements.Count; i++)
        {
            var vis = _vm.Legs[i].HasHedge ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in _hedgeDetailElements[i])
                el.Visibility = vis;
        }

        // Distributor elements in hedge detail rows
        foreach (var child in RootGrid.Children.OfType<UIElement>())
        {
            int row = Grid.GetRow(child);
            int col = Grid.GetColumn(child);
            if (HedgeDetailRows.Contains(row) && col == ColDistInput)
                child.Visibility = anyHasHedge ? Visibility.Visible : Visibility.Collapsed;
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
        {
            leg.HedgeNotionalCurrency = leg.HedgeNotionalCurrency == leg.BaseCurrency
                ? leg.QuoteCurrency : leg.BaseCurrency;
        }
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
        // Label with optional badge
        if (badgeText != null)
        {
            var labelPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 3, 4, 3)
            };

            var label = new TextBlock
            {
                Text = text,
                Foreground = FindBrush("TextSecondaryBrush"),
                FontFamily = FindFont("MainFont"),
                FontWeight = FontWeights.Medium,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            labelPanel.Children.Add(label);

            var badge = new Border
            {
                Style = FindStyle<Border>("PremiumBadge"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeLabel = new TextBlock
            {
                Text = badgeText,
                FontSize = 9,
                Foreground = FindBrush("TextDimmedBrush"),
                FontFamily = FindFont("MonoFont")
            };
            badge.Child = badgeLabel;
            labelPanel.Children.Add(badge);

            AddCell(row, ColLabel, labelPanel);
        }
        else
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = FindBrush("TextSecondaryBrush"),
                FontFamily = FindFont("MainFont"),
                FontWeight = FontWeights.Medium,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 3, 4, 3)
            };
            AddCell(row, ColLabel, label);
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
    //  FACTORY: Distributor column controls (col 0)
    // ================================================================
    private TextBox CreateDistTextBox()
    {
        return new TextBox
        {
            Style = FindStyle<TextBox>("TradingTextBox"),
            Margin = new Thickness(2, 1, 2, 1),
            ToolTip = "Press Enter to apply to all legs"
        };
    }

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
        if (stringFormat != null)
            binding.StringFormat = stringFormat;

        tb.SetBinding(TextBox.TextProperty, binding);
        return tb;
    }

    /// <summary>
    /// Creates a TextBox with an inline suffix toggle button rendered *inside* the textbox border.
    /// The suffix button shows bound text (e.g. currency code) and triggers an action on click.
    /// </summary>
    private TextBox CreateLegTextBoxWithInlineSuffix(TradeLegViewModel leg, string textPath,
        TradeLegViewModel suffixSource, string suffixPath, Action onToggle,
        string? stringFormat = null, bool isReadOnly = false,
        UpdateSourceTrigger trigger = UpdateSourceTrigger.PropertyChanged)
    {
        // Create the suffix button that will live inside the textbox border via Tag
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
        if (stringFormat != null)
            binding.StringFormat = stringFormat;

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
        label.SetBinding(TextBlock.TextProperty,
            new Binding(path) { Source = leg });
        return label;
    }

    /// <summary>
    /// Creates a currency label with an inline ↻ toggle button on the right, all within a single cell.
    /// </summary>
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
        label.SetBinding(TextBlock.TextProperty,
            new Binding(path) { Source = leg });
        panel.Children.Add(label);

        return panel;
    }

    private Button CreateIconButton(string content, string tooltip)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Style = FindStyle<Button>("IconButton"),
            Margin = new Thickness(3, 0, 0, 0)
        };
        return btn;
    }

    // ================================================================
    //  RESOURCE HELPERS
    // ================================================================
    private SolidColorBrush FindBrush(string key)
        => (SolidColorBrush)(FindResource(key) ?? Brushes.Gray);

    private FontFamily FindFont(string key)
        => (FontFamily)(FindResource(key) ?? new FontFamily("Segoe UI"));

    private Style? FindStyle(string key)
        => FindResource(key) as Style;

    private Style? FindStyle<T>(string key)
        => FindResource(key) as Style;

    // ================================================================
    //  CONVERTERS
    // ================================================================

    /// <summary>
    /// Converts a brush resource key string (e.g. "NegativeRedBrush") to the actual
    /// <see cref="SolidColorBrush"/> by looking it up in the owning element's resource tree.
    /// </summary>
    private sealed class BrushKeyConverter(FrameworkElement owner) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key)
            {
                var resource = owner.TryFindResource(key);
                if (resource is SolidColorBrush brush)
                    return brush;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// MultiBinding converter: returns true if ANY of the bound bool values is true.
    /// Used to combine IsPremiumLocked and IsPremiumAmountReadOnly into a single IsReadOnly.
    /// </summary>
    private sealed class OrBoolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var v in values)
            {
                if (v is true) return true;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}