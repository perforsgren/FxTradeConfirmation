using Bloomberglp.Blpapi;
using System.Globalization;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Fetches FX spot mid prices from a local Bloomberg terminal via BLPAPI.
/// Returns null if Bloomberg is unavailable or the request times out.
/// </summary>
public static class BloombergFx
{
    private const string Tag = nameof(BloombergFx);

    public static Task<decimal?> GetFxSpotMidAsync(
        string pair,
        int timeoutMs = 3000,
        CancellationToken ct = default)
    {
        // Offload the entire blocking BLPAPI session to a dedicated thread so
        // we never block a thread-pool thread.  Once BLPAPI exposes a true
        // awaitable NextEventAsync we can remove the Task.Run wrapper.
        return Task.Run(() => GetFxSpotMidCore(pair, timeoutMs, ct), ct);
    }

    /// <summary>Synchronous core — runs on its own thread via <see cref="GetFxSpotMidAsync"/>.</summary>
    private static decimal? GetFxSpotMidCore(string pair, int timeoutMs, CancellationToken ct)
    {
        var ticker = pair.ToUpperInvariant() + " Curncy";

        FileLogger.Instance?.Info(Tag, $"Connecting to localhost:8194 for {ticker} (timeout={timeoutMs} ms)…");

        var opts = new SessionOptions
        {
            ServerHost = "localhost",
            ServerPort = 8194,
            ConnectTimeout = 5000
        };

        // using disposes the session deterministically on scope exit:
        // Dispose() stops the session and releases the TCP socket + internal
        // BLPAPI buffers immediately, without waiting for GC finalization.
        using var session = new Session(opts);

        if (!session.Start())
        {
            FileLogger.Instance?.Warn(Tag, $"session.Start() failed — Bloomberg not reachable for {ticker}.");
            return null;
        }

        FileLogger.Instance?.Info(Tag, $"Session started — opening //blp/refdata for {ticker}.");
        ct.ThrowIfCancellationRequested();

        if (!session.OpenService("//blp/refdata"))
        {
            FileLogger.Instance?.Warn(Tag, $"OpenService(//blp/refdata) failed for {ticker}.");
            return null;
        }

        FileLogger.Instance?.Info(Tag, $"Service open — sending ReferenceDataRequest for {ticker}.");

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
            ct.ThrowIfCancellationRequested();

            var ev = session.NextEvent(50); // short slice so CT is checked often

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
                    var bid = TryGetDecimal(fieldData, "BID");
                    var ask = TryGetDecimal(fieldData, "ASK");

                    FileLogger.Instance?.Info(Tag,
                        $"Raw fields for {ticker} — BID={bid?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}  ASK={ask?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}  MID={mid?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");

                    if (mid.HasValue)
                        return mid;

                    if (bid.HasValue && ask.HasValue)
                    {
                        var computed = (bid.Value + ask.Value) / 2m;
                        FileLogger.Instance?.Info(Tag,
                            $"MID missing for {ticker} — computed mid=(BID+ASK)/2={computed.ToString(CultureInfo.InvariantCulture)}.");
                        return computed;
                    }

                    var fallback = bid ?? ask;
                    if (fallback is null)
                        FileLogger.Instance?.Warn(Tag, $"Neither BID nor ASK available for {ticker} — returning null.");
                    else
                        FileLogger.Instance?.Info(Tag,
                            $"Only one side available for {ticker} — returning {(bid.HasValue ? "BID" : "ASK")}={fallback.Value.ToString(CultureInfo.InvariantCulture)}.");

                    return fallback;
                }

                if (ev.Type == Event.EventType.RESPONSE)
                {
                    FileLogger.Instance?.Warn(Tag, $"RESPONSE exhausted without usable price data for {ticker}.");
                    return null;
                }
            }
        }

        FileLogger.Instance?.Warn(Tag, $"Timed out after {timeoutMs} ms waiting for price response for {ticker}.");
        return null;
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
