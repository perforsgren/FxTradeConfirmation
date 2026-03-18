using System.Data;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

public interface IDatabaseService
{
    Task<bool> TestConnectionAsync();
    Task<ReferenceData> LoadReferenceDataAsync();
    Task<string> GetPortfolioForCurrencyPairAsync(string currencyPair);
    Task<string> GetSalesNameAsync(string username);
    Task<string> GetReportingEntityAsync(string salesName);
    Task<string> GetInvestmentDecisionIdAsync(string username);
    //Task SaveTradeAsync(IReadOnlyList<TradeLeg> legs);
    Task<DataTable> LoadHolidaysAsync();
}
