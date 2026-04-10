using System.Diagnostics;
using System.Globalization;
using FxTradeConfirmation.Models;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Services.Ingest;
using FxTradeHub.Services.Parsing;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Wraps the FxTradeHub STP pipeline. Maps <see cref="TradeLeg"/> to
/// <see cref="ExternalTradePayload"/> and submits via <see cref="ExternalTradeIngestService"/>.
/// Each leg produces one OPTION_VANILLA payload. If the leg has a hedge,
/// a second SPOT or FWD payload is submitted for the delta hedge.
/// </summary>
public class TradeIngestService : ITradeIngestService
{
    private const string SourceVenueCode = "FXTRADE_CONFIRM";

    private readonly ExternalTradeIngestService _ingestService;
    private readonly MessageInRepository _messageInRepo;
    private readonly MySqlStpRepository _stpRepo;
    private readonly MessageInService _messageInService;

    public TradeIngestService(string connectionString)
    {
        _messageInRepo = new MessageInRepository(connectionString);
        _stpRepo = new MySqlStpRepository(connectionString);
        _messageInService = new MessageInService(_messageInRepo);

        var parsers = new List<IInboundMessageParser>
        {
            new ExternalTradeApiParser()
        };

        var orchestrator = new MessageInParserOrchestrator(
            _messageInRepo,
            _stpRepo,
            parsers,
            notificationService: null);

        _ingestService = new ExternalTradeIngestService(_messageInService, orchestrator);
    }

    public void Dispose()
    {
        (_ingestService as IDisposable)?.Dispose();
        (_messageInService as IDisposable)?.Dispose();
        (_stpRepo as IDisposable)?.Dispose();
        (_messageInRepo as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<IReadOnlyList<TradeSubmitResult>> SubmitTradeAsync(IReadOnlyList<TradeLeg> legs)
    {
        var results = new List<TradeSubmitResult>();
        var batchId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            int legNumber = i + 1;

            var executionTimeUtc = ParseExecutionTime(leg.ExecutionTime);

            // BuySell in the UI is from the CLIENT's perspective.
            // The STP hub stores from the BANK's perspective — so invert.
            var bankBuySell = leg.BuySell == BuySell.Buy ? "Sell" : "Buy";
            var bankHedgeBuySell = leg.HedgeBuySell == BuySell.Buy ? "Sell" : "Buy";

            // ── 1. Submit the OPTION_VANILLA payload ──
            var optionPayload = new ExternalTradePayload
            {
                ProductType = "OPTION_VANILLA",
                CurrencyPair = leg.CurrencyPair,
                BuySell = bankBuySell,
                Notional = leg.Notional ?? 0m,
                NotionalCurrency = leg.NotionalCurrency,
                TradeDate = DateTime.Today,
                SettlementDate = leg.SettlementDate ?? DateTime.Today.AddDays(2),
                CounterpartyCode = leg.Counterpart,
                TraderId = leg.Trader,
                PortfolioMx3 = leg.PortfolioMX3,
                CallPut = leg.CallPut.ToString(),
                Strike = leg.Strike ?? 0m,
                ExpiryDate = leg.ExpiryDate ?? DateTime.Today,
                Cut = leg.Cut,
                Premium = leg.PremiumAmount.HasValue ? Math.Abs(leg.PremiumAmount.Value) : 0m,
                PremiumCurrency = leg.PremiumCurrency,
                PremiumDate = leg.PremiumDate ?? DateTime.Today.AddDays(2),
                InvId = leg.InvestmentDecisionID,
                ReportingEntityId = leg.ReportingEntity,
                Mic = leg.MIC,
                Tvtic = leg.TVTIC,
                Isin = leg.ISIN,
                BrokerCode = leg.Broker,
                Margin = leg.Margin ?? 0m,
                SourceVenueCode = SourceVenueCode,
                ExternalSourceTradeId = $"FTC-{batchId}-L{legNumber}-OPT",
                ExecutionTimeUtc = executionTimeUtc
            };

            var optResult = await SubmitSingleAsync(optionPayload, $"Leg {legNumber} Option");
            results.Add(optResult);

            // ── 2. Submit hedge payload if applicable ──
            if (leg.Hedge != HedgeType.No && leg.HedgeNotional.HasValue && leg.HedgeRate.HasValue)
            {
                var hedgeProductType = leg.Hedge == HedgeType.Spot ? "SPOT" : "FWD";

                var hedgePayload = new ExternalTradePayload
                {
                    ProductType = hedgeProductType,
                    HedgeType = hedgeProductType,
                    CurrencyPair = leg.CurrencyPair,
                    BuySell = bankHedgeBuySell,
                    Notional = Math.Abs(leg.HedgeNotional.Value),
                    NotionalCurrency = leg.HedgeNotionalCurrency,
                    TradeDate = DateTime.Today,
                    SettlementDate = leg.HedgeSettlementDate ?? DateTime.Today.AddDays(2),
                    CounterpartyCode = leg.Counterpart,
                    TraderId = leg.Trader,
                    PortfolioMx3 = leg.PortfolioMX3,
                    CalypsoBook = leg.BookCalypso,
                    HedgeRate = leg.HedgeRate.Value,
                    InvId = leg.InvestmentDecisionID,
                    ReportingEntityId = leg.ReportingEntity,
                    Mic = leg.MIC,
                    Tvtic = leg.HedgeTVTIC,
                    Isin = leg.HedgeISIN,
                    Uti = leg.HedgeUTI,
                    SourceVenueCode = SourceVenueCode,
                    ExternalSourceTradeId = $"FTC-{batchId}-L{legNumber}-HDG",
                    ExecutionTimeUtc = executionTimeUtc
                };

                var hdgResult = await SubmitSingleAsync(hedgePayload, $"Leg {legNumber} Hedge");
                results.Add(hdgResult);
            }
        }

        return results;
    }

    private async Task<TradeSubmitResult> SubmitSingleAsync(ExternalTradePayload payload, string context)
    {
        try
        {
            var result = await Task.Run(() => _ingestService.SubmitTrade(payload));

            if (result.Success)
            {
                Debug.WriteLine($"[TradeIngest] {context} OK — MessageInId={result.MessageInId}");
                return new TradeSubmitResult(true, result.MessageInId, null);
            }
            else
            {
                Debug.WriteLine($"[TradeIngest] {context} REJECTED — {result.ErrorMessage}");
                return new TradeSubmitResult(false, null, $"{context}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TradeIngest] {context} ERROR — {ex.Message}");
            return new TradeSubmitResult(false, null, $"{context}: {ex.Message}");
        }
    }

    private static DateTime ParseExecutionTime(string executionTime)
    {
        if (DateTime.TryParseExact(executionTime,
                "yyyyMMdd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }
}
