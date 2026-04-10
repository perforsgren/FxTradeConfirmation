using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxTradeConfirmation.Helpers;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.Services;
using System.Globalization;

namespace FxTradeConfirmation.ViewModels;

public partial class TradeLegViewModel : ObservableObject
{
    private readonly MainViewModel _parent;
    private const string Tag = nameof(TradeLegViewModel);

    /// <summary>Expected format for ExecutionTime: yyyyMMdd HH:mm:ss.fff (UTC).</summary>
    public const string ExecutionTimeFormat = "yyyyMMdd HH:mm:ss.fff";

    public TradeLegViewModel(MainViewModel parent, int legNumber)
    {
        _parent = parent;
        LegNumber = legNumber;
        var now = DateTime.UtcNow.ToString(ExecutionTimeFormat);
        _executionTime = now;
        _lastValidExecutionTime = now;
    }

    [ObservableProperty] private int _legNumber;

    // Option fields
    [ObservableProperty] private string _counterpart = string.Empty;
    [ObservableProperty] private string _currencyPair = "EURSEK";
    private string _lastValidCurrencyPair = "EURSEK";
    [ObservableProperty] private BuySell _buySell = BuySell.Buy;
    [ObservableProperty] private CallPut _callPut = CallPut.Call;
    [ObservableProperty] private string _strikeText = string.Empty;
    [ObservableProperty] private DateTime? _expiryDate;
    [ObservableProperty] private DateTime? _settlementDate;
    [ObservableProperty] private string _cut = "NYC";
    [ObservableProperty] private string _notionalText = string.Empty;
    [ObservableProperty] private string _notionalCurrency = "EUR";
    [ObservableProperty] private string _premiumText = string.Empty;
    [ObservableProperty] private string _premiumAmountText = string.Empty;
    [ObservableProperty] private string _premiumCurrency = "SEK";
    [ObservableProperty] private DateTime? _premiumDate;
    [ObservableProperty] private PremiumStyle _premiumStyle = PremiumStyle.PipsQuote;
    [ObservableProperty] private PremiumDateType _premiumDateType = PremiumDateType.Spot;
    [ObservableProperty] private string _portfolioMX3 = string.Empty;
    [ObservableProperty] private string _trader = Environment.UserName.ToUpper();
    [ObservableProperty] private string _executionTime;
    [ObservableProperty] private string _mic = "XOFF";
    [ObservableProperty] private string _tvtic = string.Empty;
    [ObservableProperty] private string _isin = string.Empty;
    [ObservableProperty] private string _sales = string.Empty;
    [ObservableProperty] private string _investmentDecisionID = Environment.UserName.ToUpper();
    [ObservableProperty] private string _broker = string.Empty;
    [ObservableProperty] private string _marginText = string.Empty;
    [ObservableProperty] private string _reportingEntity = string.Empty;

    // Hedge fields
    [ObservableProperty] private HedgeType _hedge = HedgeType.No;
    [ObservableProperty] private BuySell _hedgeBuySell = BuySell.Buy;
    [ObservableProperty] private string _hedgeNotionalText = string.Empty;
    [ObservableProperty] private string _hedgeNotionalCurrency = "EUR";
    [ObservableProperty] private string _hedgeRateText = string.Empty;
    [ObservableProperty] private DateTime? _hedgeSettlementDate;
    [ObservableProperty] private string _hedgeTVTIC = string.Empty;
    [ObservableProperty] private string _hedgeUTI = string.Empty;
    [ObservableProperty] private string _hedgeISIN = string.Empty;
    [ObservableProperty] private string _bookCalypso = string.Empty;

    // Solving state
    [ObservableProperty] private bool _isSolvingTarget;
    [ObservableProperty] private bool _isPremiumLocked;

    /// <summary>True when this leg is being solved for Premium (the Premium cell is readonly).</summary>
    [ObservableProperty] private bool _isPremiumReadOnly;

    /// <summary>True when this leg is being solved for Premium Amount (the Premium Amount cell is readonly).</summary>
    [ObservableProperty] private bool _isPremiumAmountReadOnly;

    // Validation
    [ObservableProperty] private bool _hasValidationError;

    /// <summary>True when the resolved expiry date falls on a Saturday or Sunday.</summary>
    [ObservableProperty] private bool _expiryIsWeekend;

    /// <summary>True when the resolved expiry date falls on a local market holiday (not a weekend).</summary>
    [ObservableProperty] private bool _expiryIsHoliday;

    /// <summary>Tooltip text describing the holiday(s) when <see cref="ExpiryIsHoliday"/> is true.</summary>
    [ObservableProperty] private string _expiryWarningTooltip = string.Empty;

    /// <summary>
    /// The raw expiry input text shown in the TextBox. User types tenor or date here.
    /// On LostFocus, ApplyExpiryInput() is called which parses it and sets ExpiryDate/SettlementDate.
    /// If invalid, the last valid expiry date is restored.
    /// </summary>
    [ObservableProperty] private string _expiryText = string.Empty;

    private DateTime? _cachedSpotDate;

    /// <summary>Last valid parsed strike. Used to revert StrikeText on invalid input.</summary>
    private decimal? _lastValidStrike;

    /// <summary>Last valid parsed hedge rate. Used to revert HedgeRateText on invalid input.</summary>
    private decimal? _lastValidHedgeRate;

    /// <summary>
    /// Spot reference captured from the most recent parse (e.g. SP10.30).
    /// Cleared when the user changes currency pair or triggers Clear All.
    /// </summary>
    private string _parsedSpotRef = string.Empty;

    /// <summary>Last valid parsed option notional. Used to revert NotionalText on invalid input.</summary>
    private decimal? _lastValidNotional;

    /// <summary>Last valid parsed hedge notional. Used to revert HedgeNotionalText on invalid input.</summary>
    private decimal? _lastValidHedgeNotional;

    /// <summary>Last valid parsed premium. Used to revert PremiumText on invalid input.</summary>
    private decimal? _lastValidPremium;

    /// <summary>Number of decimal places the user entered for premium (only set when > default).</summary>
    private int? _userPremiumDecimals;

    /// <summary>Last valid parsed premium amount. Used to revert PremiumAmountText on invalid input.</summary>
    private decimal? _lastValidPremiumAmount;

    /// <summary>Number of decimal places the user entered for premium amount (only when > 2).</summary>
    private int? _userPremiumAmountDecimals;

    /// <summary>Number of decimal places the user entered for notional (only when result has fraction).</summary>
    private int? _userNotionalDecimals;

    /// <summary>Number of decimal places the user entered for hedge notional (only when result has fraction).</summary>
    private int? _userHedgeNotionalDecimals;

    /// <summary>Last valid parsed margin. Used to revert MarginText on invalid input.</summary>
    private decimal? _lastValidMargin;

    /// <summary>Last valid ExecutionTime string. Used to revert on invalid input.</summary>
    private string _lastValidExecutionTime;

    /// <summary>
    /// Snapshot of PremiumText taken just before "?" triggers solving.
    /// Restored when the user cancels the solve dialog.
    /// </summary>
    private string? _preSolvePremiumText;

    /// <summary>
    /// Snapshot of PremiumAmountText taken just before "?" triggers solving.
    /// Restored when the user cancels the solve dialog.
    /// </summary>
    private string? _preSolvePremiumAmountText;

    /// <summary>Snapshot of _lastValidPremium taken before solving starts.</summary>
    private decimal? _preSolveLastValidPremium;

    /// <summary>Snapshot of _userPremiumDecimals taken before solving starts.</summary>
    private int? _preSolveUserPremiumDecimals;

    /// <summary>Snapshot of _lastValidPremiumAmount taken before solving starts.</summary>
    private decimal? _preSolveLastValidPremiumAmount;

    /// <summary>Snapshot of _userPremiumAmountDecimals taken before solving starts.</summary>
    private int? _preSolveUserPremiumAmountDecimals;

    public bool HasHedge => Hedge != HedgeType.No;

    /// <summary>True if this is the first leg — only Leg 1 can edit Counterpart/CurrencyPair.</summary>
    public bool IsFirstLeg => LegNumber == 1;

    public string BaseCurrency => CurrencyPair.Length >= 3 ? CurrencyPair[..3] : "";
    public string QuoteCurrency => CurrencyPair.Length >= 6 ? CurrencyPair[3..6] : "";

    /// <summary>JPY pairs use 3 decimal places for strike; all others use 5.</summary>
    public int StrikeDecimals => IsJpyPair ? 3 : 5;

    /// <summary>
    /// Minimum decimal places for strike and hedge rate: JPY pairs use 2, all others use 4.
    /// If the user enters more decimals, those are preserved.
    /// </summary>
    public int StrikeMinDecimals => IsJpyPair ? 2 : 4;

    private bool IsJpyPair =>
        CurrencyPair.Length >= 6 &&
        (CurrencyPair[..3] == "JPY" || CurrencyPair[3..6] == "JPY");

    public bool PremiumInputEnabled => Notional.HasValue && Strike.HasValue;

    private bool IsPremiumPct => PremiumStyle is PremiumStyle.PctBase or PremiumStyle.PctQuote;

    /// <summary>Default minimum decimal places for premium: pips = 1, % = 3.</summary>
    private int PremiumDefaultDecimals => IsPremiumPct ? 3 : 1;

    /// <summary>Exposed for MainViewModel to use when formatting a solved premium value.</summary>
    public int PremiumDefaultDecimalsPublic => PremiumDefaultDecimals;

    public string PremiumStyleDisplay => PremiumStyle switch
    {
        PremiumStyle.PctBase => $"%{BaseCurrency}",
        PremiumStyle.PipsQuote => $"{QuoteCurrency} pips",
        PremiumStyle.PctQuote => $"%{QuoteCurrency}",
        _ => ""
    };

    // Parsed values
    public decimal? Strike => decimal.TryParse(StrikeText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? Notional => NotionalParser.Parse(NotionalText);
    public decimal? Premium => decimal.TryParse(PremiumText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? PremiumAmount
    {
        get
        {
            if (!decimal.TryParse(PremiumAmountText, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return null;
            // Always return correctly signed value regardless of what the user typed.
            return PremiumCalculator.ApplySign(Math.Abs(v), BuySell);
        }
    }
    public decimal? HedgeNotional => NotionalParser.Parse(HedgeNotionalText);
    public decimal? HedgeRate => decimal.TryParse(HedgeRateText, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public decimal? Margin => MarginParser.Parse(MarginText);

    // --- Reactions to property changes ---

    partial void OnCounterpartChanged(string value)
    {
        if (IsFirstLeg)
            _parent.PropagateFromLeg1(nameof(Counterpart), value);
        _parent.UpdateSaveValidation();
    }

    partial void OnCurrencyPairChanged(string value)
    {
        // ── Normalise: strip slash, uppercase ────────────────────────────────
        var normalized = value.Replace("/", string.Empty).ToUpperInvariant();

        if (normalized != value)
        {
            if (normalized.Length == 6)
            {
                var knownPairs = _parent.ReferenceData.CurrencyPairs;
                if (knownPairs.Count == 0 ||
                    _parent.ReferenceData.CurrencyPairSet.Contains(normalized))
                {
                    CurrencyPair = normalized;
                    return;
                }
            }
            CurrencyPair = _lastValidCurrencyPair;
            return;
        }

        // ── Reject unknown pairs (when list is populated) ────────────────────
        if (normalized.Length == 6)
        {
            var knownPairs = _parent.ReferenceData.CurrencyPairs;
            if (knownPairs.Count > 0 &&
                !_parent.ReferenceData.CurrencyPairSet.Contains(normalized))
            {
                _parent.NotifyInvalidCurrencyPair(normalized);
                CurrencyPair = _lastValidCurrencyPair;
                return;
            }
        }

        // ── Value is valid — commit ──────────────────────────────────────────
        _lastValidCurrencyPair = normalized;
        _parsedSpotRef = string.Empty;  // clear spot ref when currency pair changes

        // ── Normal change handling ────────────────────────────────────────────
        if (value.Length >= 6)
        {
            NotionalCurrency = BaseCurrency;
            HedgeNotionalCurrency = BaseCurrency;
            OnPropertyChanged(nameof(BaseCurrency));
            OnPropertyChanged(nameof(QuoteCurrency));
            OnPropertyChanged(nameof(StrikeDecimals));
            OnPropertyChanged(nameof(StrikeMinDecimals));

            UpdatePremiumCurrencyFromStyle();
            OnPropertyChanged(nameof(PremiumStyleDisplay));

            _ = LoadPortfolioAsync(value);

            if (_lastValidStrike.HasValue)
                StrikeText = FormatStrike(_lastValidStrike.Value);

            if (_lastValidHedgeRate.HasValue)
                HedgeRateText = FormatStrike(_lastValidHedgeRate.Value);

            if (IsFirstLeg)
            {
                var newTime = DateTime.UtcNow.ToString(ExecutionTimeFormat);
                _lastValidExecutionTime = newTime;
                ExecutionTime = newTime;
            }
        }

        if (IsFirstLeg)
            _parent.PropagateFromLeg1(nameof(CurrencyPair), value);

        if (ExpiryDate.HasValue && !string.IsNullOrWhiteSpace(ExpiryText))
            ApplyExpiryInput(ExpiryText);

        _parent.NotifyLegChanged();
    }

    partial void OnLegNumberChanged(int value)
    {
        OnPropertyChanged(nameof(IsFirstLeg));
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

    partial void OnStrikeTextChanged(string value)
    {
        OnPropertyChanged(nameof(Strike));
        OnPropertyChanged(nameof(PremiumInputEnabled));
        RecalculatePremiumFromPremiumText();
        _parent.UpdateSaveValidation();
    }

    partial void OnExpiryDateChanged(DateTime? value)
    {
        ValidateExpiryDate();
        _parent.UpdateSaveValidation();
    }

    partial void OnNotionalTextChanged(string value)
    {
        OnPropertyChanged(nameof(Notional));
        OnPropertyChanged(nameof(PremiumInputEnabled));
        RecalculatePremiumFromPremiumText();
        _parent.UpdateTotalPremium();
    }

    partial void OnNotionalCurrencyChanged(string value)
    {
        _parent.UpdateTotalPremium();
    }

    partial void OnPremiumStyleChanged(PremiumStyle value)
    {
        UpdatePremiumCurrencyFromStyle();
        OnPropertyChanged(nameof(PremiumStyleDisplay));
        _userPremiumDecimals = null;
        RecalculatePremiumFromPremiumText();
        _parent.UpdateTotalPremium();
    }

    partial void OnPremiumCurrencyChanged(string value)
    {
        _parent.UpdateTotalPremium();
    }

    partial void OnPremiumTextChanged(string? oldValue, string newValue)
    {
        if (newValue == "?")
        {
            _preSolvePremiumText = oldValue ?? string.Empty;
            _parent.StartSolving(this, isByAmount: false);
            return;
        }
        RecalculatePremiumAmountFromPremiumText();
        _parent.UpdateTotalPremium();
    }

    partial void OnPremiumAmountTextChanged(string? oldValue, string newValue)
    {
        if (newValue == "?")
        {
            _preSolvePremiumAmountText = oldValue ?? string.Empty;
            _parent.StartSolving(this, isByAmount: true);
            return;
        }
        RecalculatePremiumTextFromAmount();
        _parent.UpdateTotalPremium();
    }

    partial void OnHedgeChanged(HedgeType value)
    {
        OnPropertyChanged(nameof(HasHedge));
        if (value == HedgeType.No)
        {
            HedgeSettlementDate = null;
        }
        else
        {
            // If the user manually selects Spot or Forward and a parsed spot ref
            // exists, pre-populate Hedge Rate with it (only when the field is empty).
            if ((value == HedgeType.Spot || value == HedgeType.Forward)
                && !string.IsNullOrEmpty(_parsedSpotRef)
                && string.IsNullOrWhiteSpace(HedgeRateText))
            {
                ApplyHedgeRateInput(_parsedSpotRef);
            }

            RecalculateHedgeSettlementDate();
        }
        _parent.NotifyLegChanged();
    }

    partial void OnHedgeNotionalTextChanged(string value)
    {
        OnPropertyChanged(nameof(HedgeNotional));
        _parent.UpdateSaveValidation();
    }

    partial void OnHedgeRateTextChanged(string value)
    {
        OnPropertyChanged(nameof(HedgeRate));
        _parent.UpdateSaveValidation();
    }

    partial void OnHedgeSettlementDateChanged(DateTime? value)
    {
        _parent.UpdateSaveValidation();
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

    partial void OnSalesChanged(string value)
    {
        if (_isSyncingUserProfile) return;
        _isSyncingUserProfile = true;
        try
        {
            var refData = _parent.ReferenceData;
            if (!string.IsNullOrEmpty(value) && refData.FullNameToUserId.TryGetValue(value, out var userId))
            {
                InvestmentDecisionID = userId;

                if (refData.UserIdToReportingEntity.TryGetValue(userId, out var reporting))
                    ReportingEntity = reporting;
            }
        }
        finally
        {
            _isSyncingUserProfile = false;
        }
    }

    partial void OnInvestmentDecisionIDChanged(string value)
    {
        if (_isSyncingUserProfile) return;
        _isSyncingUserProfile = true;
        try
        {
            var refData = _parent.ReferenceData;
            if (!string.IsNullOrEmpty(value) && refData.UserIdToFullName.TryGetValue(value, out var fullName))
            {
                Sales = fullName;

                if (refData.UserIdToReportingEntity.TryGetValue(value, out var reporting))
                    ReportingEntity = reporting;
            }
        }
        finally
        {
            _isSyncingUserProfile = false;
        }
    }

    // --- Premium Calculation Helpers ---

    private bool _isRecalculating;

    /// <summary>Guard to prevent recursive Sales ↔ InvestmentDecisionID updates.</summary>
    private bool _isSyncingUserProfile;

    private void RecalculatePremiumAmountFromPremiumText()
    {
        if (_isRecalculating || IsPremiumLocked) return;
        if (!Premium.HasValue || !Notional.HasValue) return;

        var amount = PremiumCalculator.CalculateAmount(Premium, Notional, PremiumStyle, Strike, CurrencyPair);
        if (amount.HasValue)
        {
            _isRecalculating = true;
            try
            {
                var signed = PremiumCalculator.ApplySign(amount.Value, BuySell);
                _lastValidPremiumAmount = signed;
                int decimals = _userPremiumAmountDecimals ?? 2;
                SetProperty(ref _premiumAmountText, FormatPremiumAmount(signed, decimals), nameof(PremiumAmountText));
            }
            finally
            {
                _isRecalculating = false;
            }
        }
    }

    private void RecalculatePremiumTextFromAmount()
    {
        if (_isRecalculating || IsPremiumLocked) return;
        if (!PremiumAmount.HasValue || !Notional.HasValue) return;

        // Use absolute amount so Premium is always a positive price, never a signed cash flow.
        var absAmount = Math.Abs(PremiumAmount.Value);
        var prem = PremiumCalculator.CalculatePremium(absAmount, Notional, PremiumStyle, Strike, CurrencyPair);
        if (prem.HasValue)
        {
            _isRecalculating = true;
            try
            {
                _lastValidPremium = prem.Value;
                int decimals = _userPremiumDecimals ?? PremiumDefaultDecimals;
                SetProperty(ref _premiumText, FormatPremium(prem.Value, decimals), nameof(PremiumText));
            }
            finally
            {
                _isRecalculating = false;
            }
        }
    }

    private void RecalculatePremiumFromPremiumText()
    {
        if (_isRecalculating) return;
        if (Premium.HasValue && Notional.HasValue)
            RecalculatePremiumAmountFromPremiumText();
    }

    private void UpdatePremiumCurrencyFromStyle()
    {
        PremiumCurrency = PremiumStyle switch
        {
            PremiumStyle.PctBase => BaseCurrency,
            PremiumStyle.PipsQuote => QuoteCurrency,
            PremiumStyle.PctQuote => QuoteCurrency,
            _ => QuoteCurrency
        };
    }

    // --- Strike Input Handling ---

    public void ApplyStrikeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidStrike = null;
            StrikeText = string.Empty;
            return;
        }

        var normalized = input.Replace(',', '.');

        if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            _lastValidStrike = value;
            StrikeText = FormatStrike(value);
        }
        else
        {
            StrikeText = _lastValidStrike.HasValue ? FormatStrike(_lastValidStrike.Value) : string.Empty;
        }
    }

    // --- Hedge Rate Input Handling ---

    public void ApplyHedgeRateInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidHedgeRate = null;
            HedgeRateText = string.Empty;
            return;
        }

        var normalized = input.Replace(',', '.');

        if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            _lastValidHedgeRate = value;
            HedgeRateText = FormatStrike(value);
        }
        else
        {
            HedgeRateText = _lastValidHedgeRate.HasValue ? FormatStrike(_lastValidHedgeRate.Value) : string.Empty;
        }
    }

    private string FormatStrike(decimal value)
    {
        var raw = value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);
        int dotIndex = raw.IndexOf('.');
        int actualDecimals = dotIndex >= 0 ? raw.Length - dotIndex - 1 : 0;

        int decimals = Math.Max(actualDecimals, StrikeMinDecimals);
        return value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }

    // --- Notional Input Handling ---

    public void ApplyNotionalInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidNotional = null;
            _userNotionalDecimals = null;
            NotionalText = string.Empty;
            return;
        }

        var parsed = NotionalParser.Parse(input);
        if (parsed.HasValue)
        {
            _lastValidNotional = parsed.Value;
            _userNotionalDecimals = NotionalParser.CountInputDecimals(input);
            NotionalText = NotionalParser.Format(parsed.Value, _userNotionalDecimals);
        }
        else
        {
            NotionalText = _lastValidNotional.HasValue
                ? NotionalParser.Format(_lastValidNotional.Value, _userNotionalDecimals)
                : string.Empty;
        }
    }

    public void ApplyHedgeNotionalInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidHedgeNotional = null;
            _userHedgeNotionalDecimals = null;
            HedgeNotionalText = string.Empty;
            return;
        }

        var parsed = NotionalParser.Parse(input);
        if (parsed.HasValue)
        {
            _lastValidHedgeNotional = parsed.Value;
            _userHedgeNotionalDecimals = NotionalParser.CountInputDecimals(input);
            HedgeNotionalText = NotionalParser.Format(parsed.Value, _userHedgeNotionalDecimals);
        }
        else
        {
            HedgeNotionalText = _lastValidHedgeNotional.HasValue
                ? NotionalParser.Format(_lastValidHedgeNotional.Value, _userHedgeNotionalDecimals)
                : string.Empty;
        }
    }

    // --- Margin Input Handling ---

    public void ApplyMarginInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidMargin = null;
            MarginText = string.Empty;
            return;
        }

        var parsed = MarginParser.Parse(input);
        if (parsed.HasValue)
        {
            _lastValidMargin = parsed.Value;
            MarginText = MarginParser.Format(parsed.Value);
        }
        else
        {
            MarginText = _lastValidMargin.HasValue
                ? MarginParser.Format(_lastValidMargin.Value)
                : string.Empty;
        }
    }

    // --- Execution Time Input Handling ---

    /// <summary>
    /// Validates and applies user-edited ExecutionTime text.
    /// Expected format: yyyyMMdd HH:mm:ss.fff (UTC).
    /// Invalid input reverts to the last valid value.
    /// </summary>
    public void ApplyExecutionTimeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            var now = DateTime.UtcNow.ToString(ExecutionTimeFormat);
            _lastValidExecutionTime = now;
            ExecutionTime = now;
            return;
        }

        if (DateTime.TryParseExact(input.Trim(),
                ExecutionTimeFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
        {
            _lastValidExecutionTime = input.Trim();
            ExecutionTime = input.Trim();
        }
        else
        {
            // Invalid format — revert TextBox to last valid value
            ExecutionTime = _lastValidExecutionTime;
        }
    }

    // --- Premium Input Handling ---

    public void ApplyPremiumInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidPremium = null;
            _userPremiumDecimals = null;
            PremiumText = string.Empty;
            _lastValidPremiumAmount = null;
            _userPremiumAmountDecimals = null;
            PremiumAmountText = string.Empty;
            return;
        }

        var normalized = input.Replace(',', '.');

        if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            // Premium is always a positive price — ignore any sign the user typed.
            value = Math.Abs(value);
            _lastValidPremium = value;

            int dotIndex = normalized.IndexOf('.');
            int userDecimals = dotIndex >= 0 ? normalized.Length - dotIndex - 1 : 0;
            _userPremiumDecimals = userDecimals > PremiumDefaultDecimals ? userDecimals : null;

            int decimals = Math.Max(userDecimals, PremiumDefaultDecimals);
            PremiumText = FormatPremium(value, decimals);
        }
        else
        {
            if (_lastValidPremium.HasValue)
                PremiumText = FormatPremium(_lastValidPremium.Value, _userPremiumDecimals ?? PremiumDefaultDecimals);
            else
            {
                // Invalid input and no prior valid premium — clear both fields so they stay in sync.
                PremiumText = string.Empty;
                _lastValidPremiumAmount = null;
                _userPremiumAmountDecimals = null;
                PremiumAmountText = string.Empty;
            }
        }
    }

    public void ApplyPremiumAmountInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _lastValidPremiumAmount = null;
            _userPremiumAmountDecimals = null;
            PremiumAmountText = string.Empty;
            _lastValidPremium = null;
            _userPremiumDecimals = null;
            PremiumText = string.Empty;
            return;
        }

        var parsed = NotionalParser.Parse(input);
        if (parsed.HasValue)
        {
            // Normalize sign according to Buy/Sell — user must not be able to force wrong direction.
            var signed = PremiumCalculator.ApplySign(Math.Abs(parsed.Value), BuySell);
            _lastValidPremiumAmount = signed;

            var expandedDecimals = NotionalParser.CountInputDecimals(input);
            int userDecimals = expandedDecimals ?? 0;
            _userPremiumAmountDecimals = userDecimals > 2 ? userDecimals : null;

            int decimals = Math.Max(userDecimals, 2);
            PremiumAmountText = FormatPremiumAmount(signed, decimals);
        }
        else
        {
            if (_lastValidPremiumAmount.HasValue)
                PremiumAmountText = FormatPremiumAmount(_lastValidPremiumAmount.Value, _userPremiumAmountDecimals ?? 2);
            else
            {
                // Invalid input and no prior valid premium amount — clear both fields so they stay in sync.
                PremiumAmountText = string.Empty;
                _lastValidPremium = null;
                _userPremiumDecimals = null;
                PremiumText = string.Empty;
            }
        }
    }

    private static string FormatPremium(decimal value, int decimals)
        => value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatPremiumAmount(decimal value, int decimals)
        => value.ToString($"N{decimals}", System.Globalization.CultureInfo.InvariantCulture);

    // --- Expiry Date Input Handling ---

    /// <summary>
    /// Raised when a parsed expiry date lands on a weekend.
    /// The subscriber (view) should show a roll dialog and call
    /// <see cref="ApplyExpiryDateDirect"/> with the chosen date,
    /// or do nothing to keep the weekend date as-is.
    /// Parameter: the leg itself so a single handler can serve all legs.
    /// </summary>
    public event Action<TradeLegViewModel>? WeekendExpiryDetected;

    public void ApplyExpiryInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        var convention = ExpiryDateParser.Parse(input, CurrencyPair, _parent.Holidays);

        if (convention != null)
        {
            ExpiryDate = convention.ExpiryDate;
            SettlementDate = convention.DeliveryDate;
            ExpiryText = convention.ExpiryDate.ToString("yyyy-MM-dd");
            _cachedSpotDate = convention.SpotDate;

            PremiumDate = PremiumDateType == PremiumDateType.Spot
                ? convention.SpotDate
                : convention.DeliveryDate;

            RecalculateHedgeSettlementDate();

            // ValidateExpiryDate is called by OnExpiryDateChanged, but only
            // when the property value actually changes. Force it here so that
            // re-entering the same date (e.g. after a currency pair change) still
            // refreshes the weekend/holiday flags.
            ValidateExpiryDate();

            if (ExpiryIsWeekend)
                WeekendExpiryDetected?.Invoke(this);
        }
        else
        {
            ExpiryText = ExpiryDate.HasValue
                ? ExpiryDate.Value.ToString("yyyy-MM-dd")
                : string.Empty;
        }
    }

    /// <summary>
    /// Applies a pre-resolved expiry date directly, bypassing the convention
    /// engine. Use this when rolling a weekend date so that the rolled date is
    /// not re-processed through <see cref="ExpiryDateParser"/> (which would
    /// treat it as a delivery horizon and shift it further).
    /// Settlement date is recomputed correctly for the new expiry.
    /// </summary>
    public void ApplyExpiryDateDirect(DateTime expiryDate)
    {
        ExpiryDate = expiryDate;
        ExpiryText = expiryDate.ToString("yyyy-MM-dd");

        // Recompute delivery/settlement for the rolled expiry.
        if (CurrencyPair.Length >= 6)
        {
            try
            {
                var dc = new DateConvention(CurrencyPair, _parent.Holidays);
                var delivery = dc.getForwardDate(expiryDate, dc.TAdd);
                SettlementDate = delivery;

                PremiumDate = PremiumDateType == PremiumDateType.Spot
                    ? (_cachedSpotDate ?? dc.getForwardDate(DateTime.Today, dc.TAdd))
                    : delivery;
            }
            catch (Exception ex)
            {
                // Date convention failed — settlement date is left unchanged.
                FileLogger.Instance?.Warn(Tag,
                    $"DateConvention failed for {CurrencyPair} / {expiryDate:yyyy-MM-dd}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        RecalculateHedgeSettlementDate();
        ValidateExpiryDate();
    }

    /// <summary>
    /// Evaluates the current <see cref="ExpiryDate"/> against weekend and
    /// local-market holiday calendars and sets the <see cref="ExpiryIsWeekend"/>,
    /// <see cref="ExpiryIsHoliday"/> and <see cref="ExpiryWarningTooltip"/>
    /// flags accordingly. Called every time the expiry date changes so that
    /// visual warnings are cleared when the user enters a valid business day.
    /// </summary>
    public void ValidateExpiryDate()
    {
        if (!ExpiryDate.HasValue)
        {
            ExpiryIsWeekend = false;
            ExpiryIsHoliday = false;
            ExpiryWarningTooltip = string.Empty;
            return;
        }

        var date = ExpiryDate.Value;

        ExpiryIsWeekend = Helpers.Calendar.IsWeekend(date);

        if (!ExpiryIsWeekend && CurrencyPair.Length >= 6 && _parent.Holidays.Rows.Count > 0)
        {
            var markets = DateConvention.ctryNames(CurrencyPair)
                                        .Where(m => !string.IsNullOrEmpty(m));

            var (isHoliday, description) = Helpers.Calendar.IsMarketHoliday(
                date, _parent.Holidays, markets);

            ExpiryIsHoliday = isHoliday;
            ExpiryWarningTooltip = isHoliday
                ? $"Local holiday: {description}"
                : string.Empty;
        }
        else
        {
            ExpiryIsHoliday = false;
            ExpiryWarningTooltip = ExpiryIsWeekend
                ? $"{date.ToString("dddd", CultureInfo.InvariantCulture)} — weekend"
                : string.Empty;
        }
    }

    private void RecalculateHedgeSettlementDate()
    {
        if (Hedge == HedgeType.No)
        {
            HedgeSettlementDate = null;
            return;
        }

        if (Hedge == HedgeType.Forward)
        {
            HedgeSettlementDate = SettlementDate;
            return;
        }

        if (Hedge == HedgeType.Spot && CurrencyPair.Length >= 6 && _parent.Holidays.Rows.Count > 0)
        {
            try
            {
                var dc = new DateConvention(CurrencyPair, _parent.Holidays);
                HedgeSettlementDate = dc.GetConvention("1D").SpotDate;
            }
            catch (Exception ex)
            {
                HedgeSettlementDate = null;
                _parent.StatusMessage = $"⚠ Could not calculate hedge settlement date: {ex.Message}";
            }
        }
    }

    // --- Solving helpers ---

    public void ClearSolvingField(bool isByAmount)
    {
        _isRecalculating = true;
        if (isByAmount)
        {
            IsPremiumAmountReadOnly = true;
            IsPremiumReadOnly = false;
            _lastValidPremiumAmount = null;
            _userPremiumAmountDecimals = null;
            SetProperty(ref _premiumAmountText, string.Empty, nameof(PremiumAmountText));
        }
        else
        {
            IsPremiumReadOnly = true;
            IsPremiumAmountReadOnly = true;
            _lastValidPremium = null;
            _userPremiumDecimals = null;
            _lastValidPremiumAmount = null;
            _userPremiumAmountDecimals = null;
            SetProperty(ref _premiumText, string.Empty, nameof(PremiumText));
            SetProperty(ref _premiumAmountText, string.Empty, nameof(PremiumAmountText));
        }
        _isRecalculating = false;
    }

    /// <summary>
    /// Captures the current premium tracking state before solving begins so that
    /// <see cref="RestorePreSolveValues"/> can fully recover precision metadata on cancel.
    /// </summary>
    public void SavePreSolveValues()
    {
        _preSolvePremiumText = _premiumText;
        _preSolvePremiumAmountText = _premiumAmountText;
        _preSolveLastValidPremium = _lastValidPremium;
        _preSolveUserPremiumDecimals = _userPremiumDecimals;
        _preSolveLastValidPremiumAmount = _lastValidPremiumAmount;
        _preSolveUserPremiumAmountDecimals = _userPremiumAmountDecimals;
    }

    public void RestorePreSolveValues()
    {
        _lastValidPremium = _preSolveLastValidPremium;
        _userPremiumDecimals = _preSolveUserPremiumDecimals;
        _lastValidPremiumAmount = _preSolveLastValidPremiumAmount;
        _userPremiumAmountDecimals = _preSolveUserPremiumAmountDecimals;

        if (_preSolvePremiumText != null)
        {
            ApplyPremiumInput(_preSolvePremiumText);
            _preSolvePremiumText = null;
        }

        if (_preSolvePremiumAmountText != null)
        {
            ApplyPremiumAmountInput(_preSolvePremiumAmountText);
            _preSolvePremiumAmountText = null;
        }
    }

    public void ClearSolvingFlags()
    {
        IsSolvingTarget = false;
        IsPremiumLocked = false;
        IsPremiumReadOnly = false;
        IsPremiumAmountReadOnly = false;
    }

    // --- Methods ---

    public void ToggleBuySell() => BuySell = BuySell == BuySell.Buy ? BuySell.Sell : BuySell.Buy;
    public void ToggleCallPut() => CallPut = CallPut == CallPut.Call ? CallPut.Put : CallPut.Call;

    [RelayCommand]
    public void ToggleNotionalCurrency()
    {
        var newCurrency = NotionalCurrency == BaseCurrency ? QuoteCurrency : BaseCurrency;
        _parent.SetAllNotionalCurrency(newCurrency);
    }

    [RelayCommand]
    public void TogglePremiumStyle()
    {
        var nextStyle = PremiumStyle switch
        {
            PremiumStyle.PctBase => PremiumStyle.PipsQuote,
            PremiumStyle.PipsQuote => PremiumStyle.PctQuote,
            PremiumStyle.PctQuote => PremiumStyle.PctBase,
            _ => PremiumStyle.PctBase
        };

        _parent.SetAllPremiumStyle(nextStyle);
    }

    [RelayCommand]
    public void TogglePremiumCurrency()
    {
        if (PremiumCurrency == BaseCurrency)
            _parent.SetAllPremiumStyle(PremiumStyle.PipsQuote);
        else
            _parent.SetAllPremiumStyle(PremiumStyle.PctBase);
    }

    [RelayCommand]
    public void TogglePremiumDateType()
    {
        PremiumDateType = PremiumDateType == PremiumDateType.Spot
            ? PremiumDateType.Forward
            : PremiumDateType.Spot;

        if (PremiumDateType == PremiumDateType.Spot)
        {
            if (_cachedSpotDate.HasValue)
            {
                PremiumDate = _cachedSpotDate;
            }
            else if (CurrencyPair.Length >= 6)
            {
                try
                {
                    var dc = new DateConvention(CurrencyPair, _parent.Holidays);
                    var tAdd = CurrencyPair.Replace("/", "") is "USDCAD" or "USDTRY" or "USDPHP"
                        or "USDRUB" or "USDKZT" or "USDPKR" ? 1 : 2;
                    PremiumDate = dc.getForwardDate(DateTime.Today, tAdd);
                }
                catch (Exception ex)
                {
                    _parent.StatusMessage = $"⚠ Could not calculate spot premium date: {ex.Message}";
                }
            }
        }
        else
        {
            PremiumDate = SettlementDate;
        }
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
            _lastValidPremiumAmount = signed;
            int decimals = _userPremiumAmountDecimals ?? 2;
            PremiumAmountText = FormatPremiumAmount(signed, decimals);
        }
    }

    private async Task LoadPortfolioAsync(string currencyPair)
    {
        // ── Fast path: use the already-loaded in-memory lookup ───────────
        // CurrencyToPortfolio is populated from the same DB table as the live
        // query below; using it avoids a DB round-trip and any timing races.
        var refData = _parent.ReferenceData;
        if (refData.CurrencyToPortfolio.TryGetValue(currencyPair, out var cached) &&
            !string.IsNullOrEmpty(cached))
        {
            PortfolioMX3 = cached;
            return;
        }

        // ── Slow path: live DB query (e.g. pair added after startup) ─────
        var portfolio = await _parent.DatabaseService.GetPortfolioForCurrencyPairAsync(currencyPair);
        PortfolioMX3 = !string.IsNullOrEmpty(portfolio) ? portfolio : "MAJORS";
    }

    public async Task LoadPortfolioForCurrentPairAsync()
    {
        if (CurrencyPair.Length >= 6)
            await LoadPortfolioAsync(CurrencyPair);
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
            ExecutionTime = ExecutionTime,
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
        _lastValidStrike = source._lastValidStrike;
        ExpiryText = source.ExpiryText;
        ExpiryDate = source.ExpiryDate;
        SettlementDate = source.SettlementDate;
        Cut = source.Cut;
        NotionalText = source.NotionalText;
        _lastValidNotional = source._lastValidNotional;
        _userNotionalDecimals = source._userNotionalDecimals;
        NotionalCurrency = source.NotionalCurrency;
        PremiumCurrency = source.PremiumCurrency;
        PremiumStyle = source.PremiumStyle;
        PremiumDateType = source.PremiumDateType;
        PremiumDate = source.PremiumDate;
        PortfolioMX3 = source.PortfolioMX3;
        Trader = source.Trader;
        Sales = source.Sales;
        ReportingEntity = source.ReportingEntity;
        InvestmentDecisionID = source.InvestmentDecisionID;
        ExecutionTime = source.ExecutionTime;
        _lastValidExecutionTime = source._lastValidExecutionTime;
        Mic = source.Mic;
        Broker = source.Broker;
        BookCalypso = source.BookCalypso;
        _cachedSpotDate = source._cachedSpotDate;
    }

    public void LoadFromModel(TradeLeg model)
    {
        Counterpart = model.Counterpart;
        CurrencyPair = model.CurrencyPair;
        BuySell = model.BuySell;
        CallPut = model.CallPut;

        if (model.Strike.HasValue)
            ApplyStrikeInput(model.Strike.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));

        if (model.ExpiryDate.HasValue)
        {
            ExpiryDate = model.ExpiryDate;
            ExpiryText = model.ExpiryDate.Value.ToString("yyyy-MM-dd");
        }

        SettlementDate = model.SettlementDate;
        Cut = model.Cut;

        if (model.Notional.HasValue)
            ApplyNotionalInput(model.Notional.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));

        NotionalCurrency = model.NotionalCurrency;
        PremiumCurrency = model.PremiumCurrency;

        // Set PremiumDateType and PremiumDate before applying premium values,
        // but set PremiumStyle AFTER Premium so that OnPremiumStyleChanged fires
        // with Premium already populated — avoids a no-op recalculation mid-load.
        PremiumDateType = model.PremiumDateType;
        PremiumDate = model.PremiumDate;

        if (model.Premium.HasValue)
            ApplyPremiumInput(model.Premium.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));

        if (model.PremiumAmount.HasValue)
            ApplyPremiumAmountInput(model.PremiumAmount.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

        // Apply PremiumStyle last so OnPremiumStyleChanged recalculates with
        // PremiumText already set — correct ordering, not just a no-op guard.
        PremiumStyle = model.PremiumStyle;

        PortfolioMX3 = model.PortfolioMX3;
        Trader = model.Trader;
        ExecutionTime = model.ExecutionTime;
        Mic = model.MIC;
        Tvtic = model.TVTIC;
        Isin = model.ISIN;
        Sales = model.Sales;
        InvestmentDecisionID = model.InvestmentDecisionID;
        Broker = model.Broker;
        ReportingEntity = model.ReportingEntity;

        if (model.Margin.HasValue)
            MarginText = model.Margin.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

        // Hedge fields
        Hedge = model.Hedge;
        HedgeBuySell = model.HedgeBuySell;

        if (model.HedgeNotional.HasValue)
            ApplyHedgeNotionalInput(model.HedgeNotional.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));

        HedgeNotionalCurrency = model.HedgeNotionalCurrency;

        if (model.HedgeRate.HasValue)
            ApplyHedgeRateInput(model.HedgeRate.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));

        HedgeSettlementDate = model.HedgeSettlementDate;
        HedgeTVTIC = model.HedgeTVTIC;
        HedgeUTI = model.HedgeUTI;
        HedgeISIN = model.HedgeISIN;
        BookCalypso = model.BookCalypso;
    }

    /// <summary>
    /// Populates this leg from a parsed <see cref="OvmlLeg"/>.
    /// Only fields that the parser can reliably produce are set;
    /// admin fields (Trader, Sales, etc.) are left at their defaults.
    /// </summary>
    public void ApplyFromOvmlLeg(OvmlLeg leg)
    {
        // Currency pair
        if (!string.IsNullOrWhiteSpace(leg.Pair) &&
            !leg.Pair.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            var newPair = leg.Pair.ToUpperInvariant();
            CurrencyPair = newPair;

            // OnCurrencyPairChanged is skipped by CommunityToolkit when the value
            // equals the current default ("EURSEK"). Force the portfolio lookup so
            // PortfolioMX3 is always populated after a parse, regardless of whether
            // the pair actually changed.
            _ = LoadPortfolioAsync(newPair);
        }

        // Buy / Sell — default to Buy if the parser returned null or empty
        BuySell = leg.BuySell?.StartsWith("S", StringComparison.OrdinalIgnoreCase) == true
            ? BuySell.Sell
            : BuySell.Buy;

        // Call / Put — default to Call if the parser returned null or empty
        CallPut = leg.PutCall?.StartsWith("P", StringComparison.OrdinalIgnoreCase) == true
            ? CallPut.Put
            : CallPut.Call;

        // Strike — "ATM" or numeric
        if (!string.IsNullOrWhiteSpace(leg.Strike))
            ApplyStrikeInput(leg.Strike.Equals("ATM", StringComparison.OrdinalIgnoreCase)
                ? string.Empty   // leave blank — no numeric strike for ATM
                : leg.Strike);

        // Notional — stored as absolute (e.g. 25_000_000); NotionalParser.Format handles display
        if (leg.Notional > 0)
        {
            var notionalDecimal = (decimal)leg.Notional;
            _lastValidNotional = notionalDecimal;
            _userNotionalDecimals = null;
            NotionalText = NotionalParser.Format(notionalDecimal, null);
        }

        // Expiry — try to parse as date first, then fall back to tenor
        if (!string.IsNullOrWhiteSpace(leg.Expiry))
            ApplyExpiryInput(leg.Expiry);

        // Spot reference — save for later use when the user manually sets Hedge Type
        // to Spot or Forward. Only stored on the first leg (the parser sets Spot on
        // all legs but the value is trade-level, not per-leg).
        if (IsFirstLeg && !string.IsNullOrWhiteSpace(leg.Spot))
        {
            _parsedSpotRef = leg.Spot;
        }
    }

    /// <summary>
    /// Converts the current UI state of this leg back to an <see cref="OvmlLeg"/>
    /// suitable for OVML generation via <see cref="OvmlBuilderAP3.RebuildOvml"/>.
    /// </summary>
    public OvmlLeg ToOvmlLeg()
    {
        var pair = CurrencyPair.Replace("/", string.Empty).ToUpperInvariant();
        var buySell = BuySell == BuySell.Sell ? "Sell" : "Buy";
        var putCall = CallPut == CallPut.Put ? "Put" : "Call";

        // Strike: use the raw text so delta strikes ("25D") are preserved
        var strike = StrikeText;

        // Notional: convert from decimal to the long (absolute) format expected by OvmlLeg
        var notional = Notional.HasValue ? (long)Math.Abs(Notional.Value) : 0L;

        // Expiry: format as yyyy-MM-dd (the format RebuildOvml normalises to MM/dd/yy)
        var expiry = ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

        // Spot: not stored per-leg in the UI — pass empty; RebuildOvml will omit SP if blank
        var spot = string.Empty;

        return new OvmlLeg(pair, buySell, putCall, strike, notional, expiry, spot);
    }

    /// <summary>
    /// Returns the sign prefix ("-" for Buy, "" for Sell) to pre-fill
    /// the Premium Amount TextBox when it receives focus.
    /// </summary>
    public string PremiumAmountSignPrefix => BuySell == BuySell.Buy ? "-" : "";
}
