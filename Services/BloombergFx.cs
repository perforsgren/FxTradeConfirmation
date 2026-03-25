using Bloomberglp.Blpapi;
using System.Globalization;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Fetches FX spot mid prices from a local Bloomberg terminal via BLPAPI.
/// Returns null if Bloomberg is unavailable or the request times out.
/// </summary>
public static class BloombergFx
{
    public static decimal? GetFxSpotMid(string pair, int timeoutMs = 3000)
    {
        var ticker = pair.ToUpperInvariant() + " Curncy";

        var opts = new SessionOptions { ServerHost = "localhost", ServerPort = 8194 };
        using var session = new Session(opts);

        if (!session.Start()) return null;
        if (!session.OpenService("//blp/refdata")) return null;

        var svc = session.GetService("//blp/refdata");
        var req = svc.CreateRequest("ReferenceDataRequest");
        req.Append("securities", ticker);
        req.Append("fields", "BID");
        req.Append("fields", "ASK");
        req.Append("fields", "MID");

        session.SendRequest(req, new CorrelationID(1));

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var ev = session.NextEvent(500);

            if (ev.Type is Event.EventType.PARTIAL_RESPONSE or Event.EventType.RESPONSE)
            {
                foreach (var msg in ev)
                {
                    if (!msg.MessageType.Equals(Name.GetName("ReferenceDataResponse"))) continue;
                    if (!msg.HasElement("securityData")) continue;

                    var secDataArray = msg.GetElement("securityData");
                    if (secDataArray.NumValues == 0) continue;

                    var fieldData = secDataArray.GetValueAsElement(0).GetElement("fieldData");

                    var mid = TryGetDecimal(fieldData, "MID");
                    if (mid.HasValue) return mid;

                    var bid = TryGetDecimal(fieldData, "BID");
                    var ask = TryGetDecimal(fieldData, "ASK");
                    if (bid.HasValue && ask.HasValue) return (bid.Value + ask.Value) / 2m;

                    return bid ?? ask;
                }

                if (ev.Type == Event.EventType.RESPONSE) return null;
            }
        }

        return null; // timeout
    }

    private static decimal? TryGetDecimal(Element fieldData, string fieldName)
    {
        var name = Name.GetName(fieldName);
        if (!fieldData.HasElement(name)) return null;

        try
        {
            return Convert.ToDecimal(fieldData.GetElementAsFloat64(name), CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}