using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

public interface IEmailService
{
    void SendTradeConfirmation(IReadOnlyList<TradeLeg> legs, ReferenceData referenceData);
}
