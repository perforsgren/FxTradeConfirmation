namespace FxTradeConfirmation.Models;

public static class MicBrokerMapping
{
    private static readonly Dictionary<string, string> MicToBroker = new()
    {
        ["BGCO"] = "BGC",
        ["BTFE"] = "BLOOMBERG",
        ["FXOP"] = "ICAP",
        ["GFSM"] = "FENICS",
        ["GFSO"] = "GFI",
        ["TEFD"] = "TULLETT",
        ["XOFF"] = ""
    };

    private static readonly Dictionary<string, string> BrokerToMic;

    static MicBrokerMapping()
    {
        BrokerToMic = new Dictionary<string, string>();
        foreach (var kvp in MicToBroker)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
                BrokerToMic[kvp.Value] = kvp.Key;
        }
    }

    public static string GetBrokerFromMic(string mic)
        => MicToBroker.TryGetValue(mic, out var broker) ? broker : string.Empty;

    public static string GetMicFromBroker(string broker)
        => BrokerToMic.TryGetValue(broker, out var mic) ? mic : "XOFF";
}
