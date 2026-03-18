using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxTradeConfirmation.Helpers;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;

namespace FxTradeConfirmation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public IDatabaseService DatabaseService { get; }
    private readonly IEmailService _emailService;

    public MainViewModel(IDatabaseService databaseService, IEmailService emailService)
    {
        DatabaseService = databaseService;
        _emailService = emailService;
        ReferenceData = new ReferenceData();

        // Start with one leg
        AddLegInternal();

        _ = InitializeAsync();
    }

    // --- State ---
    [ObservableProperty] private ReferenceData _referenceData;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _showAdminRows;
    [ObservableProperty] private bool _isSolvingMode;
    [ObservableProperty] private decimal _totalPremium;
    [ObservableProperty] private string _totalPremiumDisplay = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Holiday calendar data loaded from the AHS database.</summary>
    public DataTable Holidays { get; private set; } = new();

    public ObservableCollection<TradeLegViewModel> Legs { get; } = [];

    public bool HasMultipleLegs => Legs.Count > 1;
    public bool HasAnyHedge => Legs.Any(l => l.HasHedge);

    // Solving state
    private TradeLegViewModel? _solvingLeg;
    private bool _solvingByAmount;

    /// <summary>
    /// Raised when a distributor combo should clear its selection after distributing.
    /// The string parameter is the field name (e.g. "Counterpart", "CurrencyPair", "Cut").
    /// </summary>
    public event Action<string>? DistributorClearRequested;

    // --- Initialization ---

    private async Task InitializeAsync()
    {
        var user = Environment.UserName.ToUpperInvariant();
        IsAdmin = user is "P901PEF" or "P901MGU";

        await RefreshConnectionAsync();
        if (IsConnected)
        {
            var data = await DatabaseService.LoadReferenceDataAsync();

            // Must assign on the UI thread so WPF bindings update correctly
            Application.Current.Dispatcher.Invoke(() => ReferenceData = data);

            await SetupUserDefaults();
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
        var username = Environment.UserName;
        var sales = await DatabaseService.GetSalesNameAsync(username);
        var reporting = await DatabaseService.GetReportingEntityAsync(sales);
        var invDecId = await DatabaseService.GetInvestmentDecisionIdAsync(username);

        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var leg in Legs)
            {
                leg.Sales = sales;
                leg.ReportingEntity = reporting;
                leg.InvestmentDecisionID = invDecId;
            }
        });
    }

    // --- Commands ---

    [RelayCommand]
    private void AddLeg()
    {
        AddLegInternal();
        OnPropertyChanged(nameof(HasMultipleLegs));
    }

    [RelayCommand]
    private void DeleteLeg(TradeLegViewModel? leg)
    {
        if (leg == null || Legs.Count <= 1) return;
        Legs.Remove(leg);
        RenumberLegs();
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
        OnPropertyChanged(nameof(HasMultipleLegs));
    }

    [RelayCommand]
    private void ClearAll()
    {
        Legs.Clear();
        AddLegInternal();
        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(HasAnyHedge));
        TotalPremium = 0;
        TotalPremiumDisplay = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!ValidateAllLegs())
        {
            StatusMessage = "Please fill in all required fields";
            return;
        }

        try
        {
            foreach (var leg in Legs)
                leg.ExecutionTime = DateTime.UtcNow.ToString("yyyyMMdd HH:mm:ss:fff");

            var models = Legs.Select(l => l.ToModel()).ToList();
            await DatabaseService.SaveTradeAsync(models);
            StatusMessage = "Trade saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
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
        if (IsAdmin)
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
                    leg.CurrencyPair = value;
                    break;
            }
        }
    }

    // --- Solving ---

    public void StartSolving(TradeLegViewModel solvingLeg, bool isByAmount)
    {
        if (Legs.Count < 2) return;

        _solvingLeg = solvingLeg;
        _solvingByAmount = isByAmount;
        IsSolvingMode = true;

        foreach (var leg in Legs)
        {
            if (leg == solvingLeg)
            {
                leg.IsSolvingTarget = true;
                leg.IsPremiumLocked = false;
            }
            else
            {
                leg.IsSolvingTarget = false;
                leg.IsPremiumLocked = true;
            }
        }
    }

    public void SolvePremium(decimal targetTotal, string payReceive)
    {
        if (_solvingLeg == null) return;

        decimal signedTarget = payReceive switch
        {
            "Pay" => -Math.Abs(targetTotal),
            "Receive" => Math.Abs(targetTotal),
            "ZeroCost" => 0m,
            _ => targetTotal
        };

        var sumOthers = Legs.Where(l => l != _solvingLeg)
                            .Sum(l => l.PremiumAmount ?? 0m);

        var solvedAmount = signedTarget - sumOthers;

        if (_solvingByAmount)
        {
            _solvingLeg.PremiumAmountText = solvedAmount.ToString("N2",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            _solvingLeg.PremiumAmountText = solvedAmount.ToString("N2",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        CancelSolving();
    }

    [RelayCommand]
    private void CancelSolving()
    {
        IsSolvingMode = false;
        _solvingLeg = null;
        foreach (var leg in Legs)
        {
            leg.IsSolvingTarget = false;
            leg.IsPremiumLocked = false;
        }
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
        }

        Legs.Add(leg);
    }

    private void RenumberLegs()
    {
        for (int i = 0; i < Legs.Count; i++)
            Legs[i].LegNumber = i + 1;
    }

    public void UpdateTotalPremium()
    {
        TotalPremium = Legs.Sum(l => l.PremiumAmount ?? 0m);
        TotalPremiumDisplay = TotalPremium switch
        {
            > 0 => $"Client receives {TotalPremium:N2}",
            < 0 => $"Client pays {Math.Abs(TotalPremium):N2}",
            _ => "Zero cost"
        };
        OnPropertyChanged(nameof(HasMultipleLegs));
    }

    public void NotifyLegChanged()
    {
        OnPropertyChanged(nameof(HasAnyHedge));
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
                      && leg.Notional.HasValue;
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