using Bloomberglp.Blpapi;
using System.Diagnostics;
using System.Globalization;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Fetches FX spot mid prices from a local Bloomberg terminal via BLPAPI.
/// Returns null if Bloomberg is unavailable or the request times out.
/// </summary>
public static class BloombergFx
{
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

        Debug.WriteLine($"[BloombergFx] Connecting to localhost:8194 for {ticker} (timeout={timeoutMs} ms)…");

        var opts = new SessionOptions
        {
            ServerHost = "localhost",
            ServerPort = 8194,
            ConnectTimeout = 5000
        };

        var session = new Session(opts);
        try
        {
            if (!session.Start())
            {
                Debug.WriteLine("[BloombergFx] session.Start() failed — Bloomberg not reachable.");
                return null;
            }

            Debug.WriteLine("[BloombergFx] Session started OK.");
            ct.ThrowIfCancellationRequested();

            if (!session.OpenService("//blp/refdata"))
            {
                Debug.WriteLine("[BloombergFx] OpenService(//blp/refdata) failed.");
                return null;
            }

            Debug.WriteLine("[BloombergFx] Service //blp/refdata open. Sending ReferenceDataRequest…");

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
                    Debug.WriteLine($"[BloombergFx] Received event: {ev.Type}");

                    foreach (var msg in ev)
                    {
                        Debug.WriteLine($"[BloombergFx] Message type: {msg.MessageType}");

                        if (!msg.MessageType.Equals(Name.GetName("ReferenceDataResponse"))) continue;
                        if (!msg.HasElement("securityData")) continue;

                        var secDataArray = msg.GetElement("securityData");
                        if (secDataArray.NumValues == 0) continue;

                        var fieldData = secDataArray.GetValueAsElement(0).GetElement("fieldData");

                        var mid = TryGetDecimal(fieldData, "MID");
                        var bid = TryGetDecimal(fieldData, "BID");
                        var ask = TryGetDecimal(fieldData, "ASK");

                        Debug.WriteLine($"[BloombergFx] Raw fields — BID={bid?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}  ASK={ask?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}  MID={mid?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");

                        if (mid.HasValue)
                        {
                            Debug.WriteLine($"[BloombergFx] Returning MID={mid.Value.ToString(CultureInfo.InvariantCulture)} for {ticker}");
                            return mid;
                        }

                        if (bid.HasValue && ask.HasValue)
                        {
                            var computed = (bid.Value + ask.Value) / 2m;
                            Debug.WriteLine($"[BloombergFx] MID missing — computed mid=(BID+ASK)/2={computed.ToString(CultureInfo.InvariantCulture)} for {ticker}");
                            return computed;
                        }

                        var fallback = bid ?? ask;
                        Debug.WriteLine($"[BloombergFx] Only one side available — returning {(bid.HasValue ? "BID" : "ASK")}={fallback?.ToString(CultureInfo.InvariantCulture) ?? "null"} for {ticker}");
                        return fallback;
                    }

                    if (ev.Type == Event.EventType.RESPONSE)
                    {
                        Debug.WriteLine($"[BloombergFx] RESPONSE event exhausted without usable data for {ticker}.");
                        return null;
                    }
                }
            }

            Debug.WriteLine($"[BloombergFx] Timed out after {timeoutMs} ms waiting for response for {ticker}.");
            return null;
        }
        finally
        {
            // Session has no IDisposable — Stop() is the only cleanup needed.
            // Calling it explicitly before the scope exits gives BLPAPI time to
            // close the TCP socket gracefully, preventing SocketException noise
            // in the debugger.
            try { session.Stop(); } catch { }
            Debug.WriteLine("[BloombergFx] Session stopped.");
        }
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