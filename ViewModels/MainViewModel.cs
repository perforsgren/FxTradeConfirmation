using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxTradeConfirmation.Helpers;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Windows;

namespace FxTradeConfirmation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public IDatabaseService DatabaseService { get; }
    private readonly IEmailService _emailService;
    private readonly ITradeIngestService? _ingestService;
    private readonly IRecentTradeService? _recentTradeService;

    public MainViewModel(IDatabaseService databaseService, IEmailService emailService, ITradeIngestService? ingestService = null, IRecentTradeService? recentTradeService = null)
    {
        DatabaseService = databaseService;
        _emailService = emailService;
        _ingestService = ingestService;
        _recentTradeService = recentTradeService;
        ReferenceData = new ReferenceData();

        // Start with one leg
        AddLegInternal();

        _ = InitializeAsync();
    }

    // --- State ---
    [ObservableProperty] private ReferenceData _referenceData;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showAdminRows;
    [ObservableProperty] private bool _isSolvingMode;
    [ObservableProperty] private decimal _totalPremium;
    [ObservableProperty] private string _totalPremiumDisplay = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>
    /// Total premium expressed in leg 1's premium style (pips or %).
    /// Shown in the distributor column on the Premium row.
    /// </summary>
    [ObservableProperty] private string _totalPremiumStyleDisplay = string.Empty;

    /// <summary>
    /// Total premium amount (absolute currency amount) formatted for the distributor column.
    /// Shown on the Premium Amount row.
    /// </summary>
    [ObservableProperty] private string _totalPremiumAmountDisplay = string.Empty;

    /// <summary>True when all required fields are filled on every leg.</summary>
    [ObservableProperty] private bool _canSave;

    /// <summary>Tooltip text listing the missing required fields, or empty when all fields are valid.</summary>
    [ObservableProperty] private string _saveValidationTooltip = string.Empty;

    /// <summary>
    /// Brush resource key for total premium color:
    /// negative (pay) → "NegativeRedBrush", positive (receive) → "PositiveGreenBrush", zero → "AccentBlueBrush".
    /// </summary>
    public string TotalPremiumBrushKey => TotalPremium switch
    {
        < 0 => "NegativeRedBrush",
        > 0 => "PositiveGreenBrush",
        _ => "AccentBlueBrush"
    };

    /// <summary>Holiday calendar data loaded from the AHS database.</summary>
    public DataTable Holidays { get; private set; } = new();

    public ObservableCollection<TradeLegViewModel> Legs { get; } = [];

    public bool HasMultipleLegs => Legs.Count > 1;
    public bool HasAnyHedge => Legs.Any(l => l.HasHedge);

    // Solving state
    private TradeLegViewModel? _solvingLeg;
    private bool _solvingByAmount;
    private bool _suppressTotalPremiumUpdate;

    /// <summary>
    /// Raised when a distributor combo should clear its selection after distributing.
    /// The string parameter is the field name (e.g. "Counterpart", "CurrencyPair", "Cut").
    /// </summary>
    public event Action<string>? DistributorClearRequested;

    /// <summary>
    /// Raised when solve mode starts and the view should show the SolvingDialog.
    /// Parameters: isByAmount, unitDisplay (e.g. "SEK pips"), contextLabel (e.g. "Leg 2 – Call – Premium").
    /// </summary>
    public event Action<bool, string, string>? SolvingDialogRequested;

    /// <summary>
    /// Raised after SaveAsync completes so the view can show a results dialog.
    /// Parameters: results list, trade leg models (for labeling), success count, fail count.
    /// </summary>
    public event Action<IReadOnlyList<TradeSubmitResult>, IReadOnlyList<TradeLeg>, int, int>? SaveResultDialogRequested;

    /// <summary>
    /// Raised when "Open Recent" is clicked so the view can show the dialog.
    /// The IRecentTradeService is passed so the dialog can load data.
    /// </summary>
    public event Action<IRecentTradeService>? OpenRecentDialogRequested;

    // --- Initialization ---

    private async Task InitializeAsync()
    {
        await RefreshConnectionAsync();
        if (IsConnected)
        {
            var data = await DatabaseService.LoadReferenceDataAsync();

            // Must assign on the UI thread so WPF bindings update correctly
            Application.Current.Dispatcher.Invoke(() => ReferenceData = data);

            await SetupUserDefaults();

            // Load portfolio for the default currency pair on all legs.
            // This must happen AFTER the DB connection is established,
            // because AddLegInternal runs before InitializeAsync completes.
            foreach (var leg in Legs)
                await leg.LoadPortfolioForCurrentPairAsync();
        }

        // Load holiday calendar (from AHS SQL Server, independent of MySQL connection)
        try
        {
            Holidays = await DatabaseService.LoadHolidaysAsync();
        }
        catch
        {
            // Fallback: empty table — date parsing will still work for direct dates
            Holidays = new DataTable();
            Holidays.Columns.Add("Market", typeof(string));
            Holidays.Columns.Add("HolidayDate", typeof(DateTime));
        }

        StartConnectionMonitor();
    }

    private async Task SetupUserDefaults()
    {
        // Wait for reference data lookup maps to be ready before triggering cross-updates.
        // The InvestmentDecisionID default (Environment.UserName) was set at construction,
        // but the lookup maps weren't loaded yet. Re-trigger now.
        Application.Current.Dispatcher.Invoke(() =>
        {
            var currentUser = Environment.UserName.ToUpperInvariant();

            // Resolve Trader from DB: Environment.UserName → userprofile.Mx3Id
            string? resolvedTrader = null;
            if (ReferenceData.UserIdToMx3Id.TryGetValue(currentUser, out var mx3Id)
                && !string.IsNullOrEmpty(mx3Id))
            {
                resolvedTrader = mx3Id;
            }

            // Resolve Calypso Book from DB: Environment.UserName → stp_calypso_book_user.CalypsoBook
            // Fallback to "FX51" if user not found in the mapping table
            string resolvedCalypsoBook = ReferenceData.TraderIdToCalypsoBook.TryGetValue(currentUser, out var book)
                ? book
                : "FX51";

            foreach (var leg in Legs)
            {
                // Set Trader from DB lookup (falls back to Environment.UserName if not found)
                if (resolvedTrader != null)
                    leg.Trader = resolvedTrader;

                // Set Calypso Book from DB lookup
                leg.BookCalypso = resolvedCalypsoBook;

                // Re-assign to trigger OnInvestmentDecisionIDChanged which sets Sales + ReportingEntity
                var currentId = leg.InvestmentDecisionID;
                leg.InvestmentDecisionID = string.Empty;
                leg.InvestmentDecisionID = currentId;
            }
        });

        await Task.CompletedTask;
    }

    // --- Commands ---

    [RelayCommand]
    private void AddLeg()
    {
        AddLegInternal();
        RenumberLegs();
        OnPropertyChanged(nameof(HasMultipleLegs));
        UpdateSaveValidation();
    }

    [RelayCommand]
    private void DeleteLeg(TradeLegViewModel? leg)
    {
        if (leg == null || Legs.Count <= 1) return;

        // Renumber before removing so CollectionChanged → RebuildGrid sees correct numbers.
        // Skip the leg being deleted when calculating new numbers.
        int newNumber = 1;
        foreach (var l in Legs)
        {
            if (l == leg) continue;
            l.LegNumber = newNumber++;
        }

        Legs.Remove(leg);
        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(HasAnyHedge));
        UpdateTotalPremium();
    }

    [RelayCommand]
    private void CopyLeg(TradeLegViewModel? source)
    {
        if (source == null) return;
        var newLeg = new TradeLegViewModel(this, Legs.Count + 1);
        newLeg.CopyFrom(source);
        Legs.Add(newLeg);
        RenumberLegs();
        OnPropertyChanged(nameof(HasMultipleLegs));
        UpdateSaveValidation();
    }

    [RelayCommand]
    private void ClearAll()
    {
        CancelSolving();
        Legs.Clear();
        AddLegInternal();
        RenumberLegs();
        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(HasAnyHedge));
        TotalPremium = 0;
        TotalPremiumDisplay = string.Empty;
        TotalPremiumStyleDisplay = string.Empty;
        TotalPremiumAmountDisplay = string.Empty;
        OnPropertyChanged(nameof(TotalPremiumBrushKey));
        UpdateSaveValidation();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_ingestService == null)
        {
            StatusMessage = "Save unavailable — STP ingest service not configured.";
            return;
        }

        if (!ValidateAllLegs())
        {
            StatusMessage = "Please fill in all required fields.";
            return;
        }

        StatusMessage = "Saving trades…";

        try
        {
            var models = Legs.Select(l => l.ToModel()).ToList();
            var results = await _ingestService.SubmitTradeAsync(models);

            int successCount = results.Count(r => r.Success);
            int failCount = results.Count(r => !r.Success);

            if (failCount == 0)
            {
                var ids = string.Join(", ", results.Where(r => r.MessageInId.HasValue).Select(r => r.MessageInId));
                StatusMessage = $"✓ Saved {successCount} trade(s) — MessageInIds: {ids}";
            }
            else
            {
                var errors = string.Join(" | ", results.Where(r => !r.Success).Select(r => r.ErrorMessage));
                StatusMessage = $"⚠ {successCount} saved, {failCount} failed — {errors}";
            }

            // Save to recent trades (best-effort, don't block the user)
            if (successCount > 0 && _recentTradeService != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await _recentTradeService.SaveRecentTradeAsync(models); }
                    catch { /* silent — recent trade save is non-critical */ }
                });
            }

            // Show results dialog
            SaveResultDialogRequested?.Invoke(results, models, successCount, failCount);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenRecent()
    {
        if (_recentTradeService == null)
        {
            StatusMessage = "Open Recent unavailable — trade storage not configured.";
            return;
        }

        OpenRecentDialogRequested?.Invoke(_recentTradeService);
    }

    /// <summary>
    /// Loads a saved trade into the current workspace, replacing all legs.
    /// </summary>
    public void LoadTrade(SavedTradeData tradeData)
    {
        CancelSolving();
        Legs.Clear();

        foreach (var model in tradeData.Legs)
        {
            var leg = new TradeLegViewModel(this, Legs.Count + 1);
            leg.LoadFromModel(model);
            Legs.Add(leg);
        }

        RenumberLegs();
        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(HasAnyHedge));
        UpdateTotalPremium();
        UpdateSaveValidation();

        StatusMessage = $"Loaded trade: {tradeData.Legs.Count} leg(s)";
    }

    [RelayCommand]
    private void SendMail()
    {
        if (!ValidateAllLegs())
        {
            StatusMessage = "Please fill in all required fields";
            return;
        }

        try
        {
            var models = Legs.Select(l => l.ToModel()).ToList();
            _emailService.SendTradeConfirmation(models, ReferenceData);
            StatusMessage = "Mail created in Outlook";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mail failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleAdminRows()
    {
        ShowAdminRows = !ShowAdminRows;
    }

    // --- Distributor ---

    [RelayCommand]
    private void SetAllCounterpart(string value)
    {
        foreach (var leg in Legs) leg.Counterpart = value;
        DistributorClearRequested?.Invoke(nameof(TradeLegViewModel.Counterpart));
    }

    [RelayCommand]
    private void SetAllCurrencyPair(string value)
    {
        foreach (var leg in Legs) leg.CurrencyPair = value;
        DistributorClearRequested?.Invoke(nameof(TradeLegViewModel.CurrencyPair));
    }

    [RelayCommand]
    private void SetAllBuySell(BuySell value)
    {
        foreach (var leg in Legs) leg.BuySell = value;
    }

    [RelayCommand]
    private void SetAllCallPut(CallPut value)
    {
        foreach (var leg in Legs) leg.CallPut = value;
    }

    [RelayCommand]
    private void SetAllStrike(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var leg in Legs) leg.ApplyStrikeInput(value);
    }

    [RelayCommand]
    private void SetAllExpiry(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        foreach (var leg in Legs)
            leg.ApplyExpiryInput(value);
    }

    [RelayCommand]
    private void SetAllCut(string value)
    {
        foreach (var leg in Legs) leg.Cut = value;
        DistributorClearRequested?.Invoke(nameof(TradeLegViewModel.Cut));
    }

    [RelayCommand]
    private void SetAllNotional(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var leg in Legs) leg.ApplyNotionalInput(value);
    }

    [RelayCommand]
    private void SetAllHedgeNotional(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var leg in Legs) leg.ApplyHedgeNotionalInput(value);
    }

    [RelayCommand]
    private void SetAllHedgeRate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var leg in Legs) leg.ApplyHedgeRateInput(value);
    }

    /// <summary>
    /// Sets premium style on all legs simultaneously.
    /// Called by any leg's TogglePremiumStyle so all legs stay in sync.
    /// </summary>
    public void SetAllPremiumStyle(PremiumStyle style)
    {
        foreach (var leg in Legs)
            leg.PremiumStyle = style;
    }

    /// <summary>
    /// Sets notional currency on all legs simultaneously.
    /// Called when any leg changes its notional currency so all legs stay in sync.
    /// </summary>
    public void SetAllNotionalCurrency(string currency)
    {
        foreach (var leg in Legs)
            leg.NotionalCurrency = currency;
    }

    // --- Propagation from Leg 1 ---

    /// <summary>
    /// Called by Leg 1 when Counterpart or CurrencyPair changes,
    /// to keep all other legs in sync.
    /// </summary>
    public void PropagateFromLeg1(string propertyName, string value)
    {
        foreach (var leg in Legs)
        {
            if (leg.IsFirstLeg) continue;

            switch (propertyName)
            {
                case nameof(TradeLegViewModel.Counterpart):
                    leg.Counterpart = value;
                    break;
                case nameof(TradeLegViewModel.CurrencyPair):
                    // Propagate Leg 1's ExecutionTime BEFORE setting CurrencyPair,
                    // so OnCurrencyPairChanged on Leg 2+ doesn't generate its own timestamp.
                    var leg1 = Legs[0];
                    leg.ExecutionTime = leg1.ExecutionTime;
                    leg.CurrencyPair = value;
                    break;
            }
        }
    }

    // --- Solving ---

    /// <summary>
    /// Enters solve mode. The solving leg's premium/premiumAmount is cleared and made readonly.
    /// Other legs' premiums are locked. The SolvingDialog is opened immediately.
    /// </summary>
    public void StartSolving(TradeLegViewModel solvingLeg, bool isByAmount)
    {
        if (Legs.Count < 2) return;

        bool othersHavePremium = Legs.Where(l => l != solvingLeg)
                                     .All(l => l.PremiumAmount.HasValue);
        if (!othersHavePremium) return;

        _solvingLeg = solvingLeg;
        _solvingByAmount = isByAmount;
        IsSolvingMode = true;

        // Suppress UpdateTotalPremium while we clear fields so the total doesn't flicker.
        _suppressTotalPremiumUpdate = true;

        foreach (var leg in Legs)
        {
            if (leg == solvingLeg)
            {
                leg.IsSolvingTarget = true;
                leg.IsPremiumLocked = false;
                leg.ClearSolvingField(isByAmount);
            }
            else
            {
                leg.IsSolvingTarget = false;
                leg.IsPremiumLocked = true;
            }
        }

        _suppressTotalPremiumUpdate = false;

        string unitDisplay = isByAmount
            ? $"Premium Amount [{solvingLeg.PremiumCurrency}]"
            : $"Premium [{solvingLeg.PremiumStyleDisplay}]";

        // Build context label: "Leg N – Call/Put – Premium / Premium Amount"
        string fieldName = isByAmount ? "Premium Amount" : "Premium";
        string contextLabel = $"Leg {solvingLeg.LegNumber} – {solvingLeg.CallPut} – {fieldName}";

        SolvingDialogRequested?.Invoke(isByAmount, unitDisplay, contextLabel);
    }

    /// <summary>
    /// Solves the premium for the target leg given a total and pay/receive direction.
    /// Returns null on success, or an error message if the result violates Buy/Sell sign rules.
    /// </summary>
    public string? SolvePremium(decimal targetTotal, string payReceive)
    {
        if (_solvingLeg == null) return "No solving leg set.";

        decimal signedTarget = payReceive switch
        {
            "Pay" => -Math.Abs(targetTotal),
            "Receive" => Math.Abs(targetTotal),
            "ZeroCost" => 0m,
            _ => targetTotal
        };

        decimal solvedAmount;

        if (_solvingByAmount)
        {
            var sumOthers = Legs.Where(l => l != _solvingLeg)
                                .Sum(l => l.PremiumAmount ?? 0m);

            solvedAmount = signedTarget - sumOthers;
        }
        else
        {
            // The target is expressed in leg 1's premium style (pips/pct relative to leg 1's notional).
            // Convert to absolute amount, solve by amount, then convert back to the solving leg's pips/pct.
            var leg1 = Legs[0];

            // Convert target pips/pct → absolute amount using leg 1's notional
            var targetAmount = PremiumCalculator.CalculateAmount(
                signedTarget, leg1.Notional, leg1.PremiumStyle, leg1.Strike);

            var sumOthersAmount = Legs.Where(l => l != _solvingLeg)
                                      .Sum(l => l.PremiumAmount ?? 0m);

            solvedAmount = (targetAmount ?? 0m) - sumOthersAmount;
        }

        // Validate: Buy leg must have negative (pay) premium, Sell leg must have positive (receive) premium.
        // Zero is always allowed.
        if (solvedAmount != 0)
        {
            bool isBuy = _solvingLeg.BuySell == BuySell.Buy;
            if (isBuy && solvedAmount > 0)
                return $"Cannot solve: Leg {_solvingLeg.LegNumber} is a BUY — client cannot receive premium on a bought option.";
            if (!isBuy && solvedAmount < 0)
                return $"Cannot solve: Leg {_solvingLeg.LegNumber} is a SELL — client cannot pay premium on a sold option.";
        }

        // Apply the solved value
        if (_solvingByAmount)
        {
            _solvingLeg.ApplyPremiumAmountInput(
                solvedAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            var solvedPremium = PremiumCalculator.CalculatePremium(
                solvedAmount, _solvingLeg.Notional, _solvingLeg.PremiumStyle, _solvingLeg.Strike);

            _solvingLeg.ApplyPremiumInput(
                (solvedPremium ?? 0m).ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
        }

        // Solve succeeded — exit solve mode WITHOUT restoring old values.
        FinishSolving();
        return null;
    }

    [RelayCommand]
    public void CancelSolving()
    {
        if (!IsSolvingMode) return;

        // Restore the solving leg's premium fields to what they were before "?" was typed.
        _solvingLeg?.RestorePreSolveValues();

        FinishSolving();
    }

    /// <summary>
    /// Exits solve mode: clears all flags and updates totals.
    /// Does NOT restore pre-solve values — that is only done by CancelSolving.
    /// </summary>
    private void FinishSolving()
    {
        IsSolvingMode = false;
        _solvingLeg = null;

        foreach (var leg in Legs)
            leg.ClearSolvingFlags();

        UpdateTotalPremium();
    }

    // --- Internal ---

    private void AddLegInternal()
    {
        var leg = new TradeLegViewModel(this, Legs.Count + 1);

        // New legs inherit Counterpart/CurrencyPair from Leg 1
        if (Legs.Count > 0)
        {
            var leg1 = Legs[0];
            leg.Counterpart = leg1.Counterpart;
            leg.CurrencyPair = leg1.CurrencyPair;

            // Inherit admin defaults directly from Leg 1 so the values are set
            // BEFORE Legs.Add triggers RebuildGrid and the ComboBoxes are created.
            leg.Trader = leg1.Trader;
            leg.Sales = leg1.Sales;
            leg.ReportingEntity = leg1.ReportingEntity;
            leg.InvestmentDecisionID = leg1.InvestmentDecisionID;
            leg.ExecutionTime = leg1.ExecutionTime;
            leg.Mic = leg1.Mic;
            leg.Broker = leg1.Broker;
            leg.BookCalypso = leg1.BookCalypso;
        }
        else if (ReferenceData.UserIdToFullName.Count > 0)
        {
            // First leg created after reference data is loaded (e.g. ClearAll)
            var currentUser = Environment.UserName.ToUpperInvariant();

            if (ReferenceData.UserIdToMx3Id.TryGetValue(currentUser, out var mx3Id)
                && !string.IsNullOrEmpty(mx3Id))
                leg.Trader = mx3Id;

            // Resolve Calypso Book for the current user, fallback to "FX51"
            leg.BookCalypso = ReferenceData.TraderIdToCalypsoBook.TryGetValue(currentUser, out var book)
                ? book
                : "FX51";

            // Force trigger to populate Sales + ReportingEntity
            var currentId = leg.InvestmentDecisionID;
            leg.InvestmentDecisionID = string.Empty;
            leg.InvestmentDecisionID = currentId;
        }

        Legs.Add(leg);

        // Trigger portfolio lookup for the default currency pair
        // (OnCurrencyPairChanged doesn't fire for the initial field value)
        _ = leg.LoadPortfolioForCurrentPairAsync();
    }

    private void RenumberLegs()
    {
        for (int i = 0; i < Legs.Count; i++)
            Legs[i].LegNumber = i + 1;
    }

    public void UpdateTotalPremium()
    {
        if (_suppressTotalPremiumUpdate) return;

        TotalPremium = Legs.Sum(l => l.PremiumAmount ?? 0m);
        TotalPremiumDisplay = TotalPremium switch
        {
            > 0 => $"Client receives {TotalPremium:N2}",
            < 0 => $"Client pays {Math.Abs(TotalPremium):N2}",
            _ => "Zero cost"
        };

        // --- Distributor column summaries ---

        var leg1 = Legs.Count > 0 ? Legs[0] : null;
        bool hasPremiumData = Legs.Any(l => l.PremiumAmount.HasValue);

        if (hasPremiumData && leg1?.Notional is > 0)
        {
            var totalStyleValue = PremiumCalculator.CalculatePremium(
                TotalPremium, leg1.Notional, leg1.PremiumStyle, leg1.Strike);

            if (totalStyleValue.HasValue)
            {
                bool isPct = leg1.PremiumStyle is PremiumStyle.PctBase or PremiumStyle.PctQuote;
                int decimals = isPct ? 3 : 1;
                TotalPremiumStyleDisplay = totalStyleValue.Value.ToString(
                    $"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                TotalPremiumStyleDisplay = string.Empty;
            }
        }
        else
        {
            TotalPremiumStyleDisplay = string.Empty;
        }

        TotalPremiumAmountDisplay = hasPremiumData
            ? TotalPremium.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(TotalPremiumBrushKey));

        UpdateSaveValidation();
    }

    /// <summary>
    /// Re-evaluates whether all required fields are filled and updates
    /// <see cref="CanSave"/> and <see cref="SaveValidationTooltip"/>.
    /// Called whenever a leg property changes.
    /// </summary>
    public void UpdateSaveValidation()
    {
        var missing = new List<string>();
        bool multiLeg = Legs.Count > 1;

        foreach (var leg in Legs)
        {
            string prefix = multiLeg ? $"Leg {leg.LegNumber}: " : "";

            if (string.IsNullOrWhiteSpace(leg.Counterpart))
                missing.Add($"{prefix}Counterpart");
            if (string.IsNullOrWhiteSpace(leg.CurrencyPair))
                missing.Add($"{prefix}Currency Pair");
            if (!leg.Strike.HasValue)
                missing.Add($"{prefix}Strike");
            if (!leg.ExpiryDate.HasValue)
                missing.Add($"{prefix}Expiry");
            if (!leg.Notional.HasValue)
                missing.Add($"{prefix}Notional");
            if (!leg.PremiumAmount.HasValue)
                missing.Add($"{prefix}Premium");
        }

        CanSave = missing.Count == 0;
        SaveValidationTooltip = missing.Count == 0
            ? string.Empty
            : "Missing fields:\n" + string.Join("\n", missing.Select(m => $"  • {m}"));
    }

    public void NotifyLegChanged()
    {
        OnPropertyChanged(nameof(HasAnyHedge));
        UpdateSaveValidation();
    }

    private bool ValidateAllLegs()
    {
        bool allValid = true;
        foreach (var leg in Legs)
        {
            bool valid = !string.IsNullOrWhiteSpace(leg.Counterpart)
                      && !string.IsNullOrWhiteSpace(leg.CurrencyPair)
                      && leg.Strike.HasValue
                      && leg.ExpiryDate.HasValue
                      && leg.Notional.HasValue
                      && leg.PremiumAmount.HasValue;
            leg.HasValidationError = !valid;
            if (!valid) allValid = false;
        }
        return allValid;
    }

    // --- Connection Monitor ---

    private System.Timers.Timer? _connectionTimer;

    private void StartConnectionMonitor()
    {
        _connectionTimer = new System.Timers.Timer(6000);
        _connectionTimer.Elapsed += async (_, _) =>
        {
            var connected = await DatabaseService.TestConnectionAsync();
            IsConnected = connected;
            _connectionTimer!.Interval = connected ? 600_000 : 60_000;
        };
        _connectionTimer.Start();
    }

    private async Task RefreshConnectionAsync()
    {
        IsConnected = await DatabaseService.TestConnectionAsync();
    }
}