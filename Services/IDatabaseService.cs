using System.Data;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

public interface IDatabaseService
{
    Task<bool> TestConnectionAsync();
    Task<ReferenceData> LoadReferenceDataAsync();
    Task<string> GetPortfolioForCurrencyPairAsync(string currencyPair);
    Task<DataTable> LoadHolidaysAsync();
}
