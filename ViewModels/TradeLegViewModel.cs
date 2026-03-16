using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxTradeConfirmation.Helpers;
using FxTradeConfirmation.Models;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows.Controls;

namespace FxTradeConfirmation.ViewModels;

public partial class TradeLegViewModel : ObservableObject
{
    private readonly MainViewModel _parent;

    public TradeLegViewModel(MainViewModel parent, int legNumber)
    {
        _parent = parent;
        LegNumber = legNumber;
    }

    [ObservableProperty] private int _legNumber;

    // Option fields
    [ObservableProperty] private string _counterpart = string.Empty;
    [ObservableProperty] private string _currencyPair = string.Empty;
    [ObservableProperty] private BuySell _buySell = BuySell.Buy;
    [ObservableProperty] private CallPut _callPut = CallPut.Call;
    [ObservableProperty] private string _strikeText = string.Empty;
    [ObservableProperty] private DateTime? _expiryDate;
    [ObservableProperty] private DateTime? _settlementDate;
    [ObservableProperty] private string _cut = "NYC";
    [ObservableProperty] private string _notionalText = string.Empty;
    [ObservableProperty] private string _notionalCurrency = string.Empty;
    [ObservableProperty] private string _premiumText = string.Empty;
    [ObservableProperty] private string _premiumAmountText = string.Empty;
    [ObservableProperty] private string _premiumCurrency = string.Empty;
    [ObservableProperty] private DateTime? _premiumDate;
    [ObservableProperty] private PremiumStyle _premiumStyle = PremiumStyle.Pips;
    [ObservableProperty] private PremiumDateType _premiumDateType = PremiumDateType.Spot;
    [ObservableProperty] private string _portfolioMX3 = string.Empty;
    [ObservableProperty] private string _trader = "P901PEF";
    [ObservableProperty] private string _executionTime = string.Empty;
    [ObservableProperty] private string _mic = "XOFF";
    [ObservableProperty] private string _tvtic = string.Empty;
    [ObservableProperty] private string _isin = string.Empty;
    [ObservableProperty] private string _sales = string.Empty;
    [ObservableProperty] private string _investmentDecisionID = string.Empty;
    [ObservableProperty] private string _broker = string.Empty;
    [ObservableProperty] private string _marginText = string.Empty;
    [ObservableProperty] private string _reportingEntity = string.Empty;

    // Hedge fields
    [ObservableProperty] private HedgeType _hedge = HedgeType.No;
    [ObservableProperty] private BuySell _hedgeBuySell = BuySell.Buy;
    [ObservableProperty] private string _hedgeNotionalText = string.Empty;
    [ObservableProperty] private string _hedgeNotionalCurrency = string.Empty;
    [ObservableProperty] private string _hedgeRateText = string.Empty;
    [ObservableProperty] private DateTime? _hedgeSettlementDate;
    [ObservableProperty] private string _hedgeTVTIC = string.Empty;
    [ObservableProperty] private string _hedgeUTI = string.Empty;
    [ObservableProperty] private string _hedgeISIN = string.Empty;
    [ObservableProperty] private string _bookCalypso = string.Empty;

    // Solving state
    [ObservableProperty] private bool _isSolvingTarget;
    [ObservableProperty] private bool _isPremiumLocked;

    // Validation
    [ObservableProperty] private bool _hasValidationError;

    public bool HasHedge => Hedge != HedgeType.No;

    public string BaseCurrency => CurrencyPair.Length >= 3 ? CurrencyPair[..3] : "";
    public string QuoteCurrency => CurrencyPair.Length >= 6 ? CurrencyPair[3..6] : "";

    // Parsed values
    public decimal? Strike => decimal.TryParse(StrikeText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? Notional => NotionalParser.Parse(NotionalText);
    public decimal? Premium => decimal.TryParse(PremiumText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? PremiumAmount => decimal.TryParse(PremiumAmountText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? HedgeNotional => NotionalParser.Parse(HedgeNotionalText);
    public decimal? HedgeRate => decimal.TryParse(HedgeRateText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? Margin => NotionalParser.Parse(MarginText);

    // --- Reactions to property changes ---

    partial void OnCurrencyPairChanged(string value)
    {
        if (value.Length >= 6)
        {
            NotionalCurrency = BaseCurrency;
            PremiumCurrency = QuoteCurrency;
            HedgeNotionalCurrency = BaseCurrency;
            OnPropertyChanged(nameof(BaseCurrency));
            OnPropertyChanged(nameof(QuoteCurrency));

            // Auto-set portfolio from DB
            _ = LoadPortfolioAsync(value);
        }
        _parent.NotifyLegChanged();
    }

    partial void OnBuySellChanged(BuySell value)
    {
        UpdateHedgeDirection();
        RecalculatePremiumSign();
        _parent.NotifyLegChanged();
    }

    partial void OnCallPutChanged(CallPut value)
    {
        UpdateHedgeDirection();
        _parent.NotifyLegChanged();
    }

    partial void OnPremiumTextChanged(string value)
    {
        if (value == "?")
        {
            _parent.StartSolving(this, isByAmount: false);
            return;
        }
        if (!IsPremiumLocked && Premium.HasValue && Notional.HasValue)
        {
            var amount = PremiumCalculator.CalculateAmount(Premium, Notional, PremiumStyle);
            if (amount.HasValue)
            {
                var signed = PremiumCalculator.ApplySign(amount.Value, BuySell);
                SetProperty(ref _premiumAmountText, signed.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), nameof(PremiumAmountText));
            }
        }
        _parent.UpdateTotalPremium();
    }

    partial void OnPremiumAmountTextChanged(string value)
    {
        if (value == "?")
        {
            _parent.StartSolving(this, isByAmount: true);
            return;
        }
        if (!IsPremiumLocked && PremiumAmount.HasValue && Notional.HasValue)
        {
            var prem = PremiumCalculator.CalculatePremium(PremiumAmount, Notional, PremiumStyle);
            if (prem.HasValue)
            {
                SetProperty(ref _premiumText, prem.Value.ToString("N4", System.Globalization.CultureInfo.InvariantCulture), nameof(PremiumText));
            }
        }
        _parent.UpdateTotalPremium();
    }

    partial void OnHedgeChanged(HedgeType value)
    {
        OnPropertyChanged(nameof(HasHedge));
        if (value == HedgeType.No)
        {
            HedgeSettlementDate = null;
        }
        _parent.NotifyLegChanged();
    }

    partial void OnMicChanged(string value)
    {
        var broker = MicBrokerMapping.GetBrokerFromMic(value);
        if (!string.IsNullOrEmpty(broker))
            Broker = broker;
    }

    partial void OnBrokerChanged(string value)
    {
        var mic = MicBrokerMapping.GetMicFromBroker(value);
        Mic = mic;
    }

    // --- Methods ---

    public void ToggleBuySell() => BuySell = BuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
    public void ToggleCallPut() => CallPut = CallPut == CallPut.Call ? CallPut.Put : CallPut.Call;

    [RelayCommand]
    public void ToggleNotionalCurrency()
    {
        NotionalCurrency = NotionalCurrency == BaseCurrency ? QuoteCurrency : BaseCurrency;
    }

    [RelayCommand]
    public void TogglePremiumCurrency()
    {
        if (PremiumCurrency == QuoteCurrency)
        {
            PremiumCurrency = BaseCurrency;
            PremiumStyle = PremiumStyle.Percent;
        }
        else
        {
            PremiumCurrency = QuoteCurrency;
            PremiumStyle = PremiumStyle.Pips;
        }
    }

    [RelayCommand]
    public void TogglePremiumDateType()
    {
        PremiumDateType = PremiumDateType == PremiumDateType.Spot
            ? PremiumDateType.Forward
            : PremiumDateType.Spot;
        // Recalculate premium date based on type
    }

    private void UpdateHedgeDirection()
    {
        HedgeBuySell = PremiumCalculator.GetHedgeDirection(BuySell, CallPut);
    }

    private void RecalculatePremiumSign()
    {
        if (PremiumAmount.HasValue)
        {
            var abs = Math.Abs(PremiumAmount.Value);
            var signed = PremiumCalculator.ApplySign(abs, BuySell);
            PremiumAmountText = signed.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private async Task LoadPortfolioAsync(string currencyPair)
    {
        var portfolio = await _parent.DatabaseService.GetPortfolioForCurrencyPairAsync(currencyPair);
        if (!string.IsNullOrEmpty(portfolio))
            PortfolioMX3 = portfolio;
    }

    public TradeLeg ToModel()
    {
        return new TradeLeg
        {
            Counterpart = Counterpart,
            CurrencyPair = CurrencyPair,
            BuySell = BuySell,
            CallPut = CallPut,
            Strike = Strike,
            ExpiryDate = ExpiryDate,
            SettlementDate = SettlementDate,
            Cut = Cut,
            Notional = Notional,
            NotionalCurrency = NotionalCurrency,
            Premium = Premium,
            PremiumAmount = PremiumAmount,
            PremiumCurrency = PremiumCurrency,
            PremiumDate = PremiumDate,
            PremiumStyle = PremiumStyle,
            PremiumDateType = PremiumDateType,
            PortfolioMX3 = PortfolioMX3,
            Trader = Trader,
            ExecutionTime = DateTime.UtcNow.ToString("yyyyMMdd HH:mm:ss:fff"),
            MIC = Mic,
            TVTIC = Tvtic,
            ISIN = Isin,
            Sales = Sales,
            InvestmentDecisionID = InvestmentDecisionID,
            Broker = Broker,
            Margin = Margin,
            ReportingEntity = ReportingEntity,
            Hedge = Hedge,
            HedgeBuySell = HedgeBuySell,
            HedgeNotional = HedgeNotional,
            HedgeNotionalCurrency = HedgeNotionalCurrency,
            HedgeRate = HedgeRate,
            HedgeSettlementDate = HedgeSettlementDate,
            HedgeTVTIC = HedgeTVTIC,
            HedgeUTI = HedgeUTI,
            HedgeISIN = HedgeISIN,
            BookCalypso = BookCalypso
        };
    }

    public void CopyFrom(TradeLegViewModel source)
    {
        Counterpart = source.Counterpart;
        CurrencyPair = source.CurrencyPair;
        BuySell = source.BuySell;
        CallPut = source.CallPut;
        StrikeText = source.StrikeText;
        ExpiryDate = source.ExpiryDate;
        SettlementDate = source.SettlementDate;
        Cut = source.Cut;
        NotionalText = source.NotionalText;
        NotionalCurrency = source.NotionalCurrency;
        PremiumCurrency = source.PremiumCurrency;
        PremiumStyle = source.PremiumStyle;
        PremiumDateType = source.PremiumDateType;
        PremiumDate = source.PremiumDate;
        PortfolioMX3 = source.PortfolioMX3;
        Trader = source.Trader;
        Mic = source.Mic;
        Broker = source.Broker;
        Hedge = source.Hedge;
        // Premium NOT copied (per legacy behavior)
    }
}
