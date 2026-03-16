using System.Text;
using FxTradeConfirmation.Models;
using Microsoft.Office.Interop.Outlook;

namespace FxTradeConfirmation.Services;

public class EmailService : IEmailService
{
    public void SendTradeConfirmation(IReadOnlyList<TradeLeg> legs, ReferenceData referenceData)
    {
        var app = new Application();
        MailItem mail = (MailItem)app.CreateItem(OlItemType.olMailItem);

        var ccyPair = legs[0].CurrencyPair;
        var tradeDate = DateTime.Now.ToString("yyyy-MM-dd");

        mail.Subject = $"FX Option Trade Confirmation {tradeDate} - {ccyPair}";

        // BCC all email addresses from DB
        foreach (var addr in referenceData.EmailAddresses)
            mail.BCC += (mail.BCC.Length > 0 ? ";" : "") + addr;

        // CC current user
        mail.CC = GetCurrentUserEmail();

        // Special DRAX handling
        if (legs.Any(l => l.Broker == "DRAX"))
        {
            mail.To = "drax-specific@example.com"; // Replace with actual DRAX addresses
        }

        mail.HTMLBody = BuildHtmlBody(legs);
        mail.Display(); // Show in Outlook for review before sending
    }

    private string BuildHtmlBody(IReadOnlyList<TradeLeg> legs)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<html><body style='font-family: Calibri, Arial, sans-serif;'>");

        // Swedbank header
        sb.AppendLine(@"<div style='background-color: #E26B0A; color: white; padding: 12px 20px; font-size: 18px; font-weight: bold;'>
            Swedbank - Trade Confirmation</div>");

        bool hasHedge = legs.Any(l => l.Hedge != HedgeType.No);
        bool isDrax = legs.Any(l => l.Broker == "DRAX");

        if (isDrax)
        {
            sb.AppendLine(@"<div style='background-color: #FFD700; padding: 8px; text-align: center;
                font-weight: bold; font-size: 14px;'>Manual give up please</div>");
        }

        // Option details per leg
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            if (legs.Count > 1)
                sb.AppendLine($"<h3 style='color: #E26B0A;'>Leg {i + 1}</h3>");

            sb.AppendLine("<table style='border-collapse: collapse; margin: 10px 0;'>");
            AddRow(sb, "Trade Date", DateTime.Now.ToString("yyyy-MM-dd"));
            AddRow(sb, "Contract", leg.CurrencyPair);
            AddRow(sb, "Client Buy/Sell", leg.BuySell.ToString());
            AddRow(sb, "Call/Put", leg.CallPut.ToString());
            AddRow(sb, "Strike", leg.Strike?.ToString("0.0000") ?? Highlight("N/A"));
            AddRow(sb, "Notional Amount", FormatNotional(leg.Notional, leg.NotionalCurrency));
            AddRow(sb, "Expiry Cut", leg.Cut);
            AddRow(sb, "Expiry Date", leg.ExpiryDate?.ToString("yyyy-MM-dd") ?? Highlight("N/A"));
            AddRow(sb, "Delivery Date", leg.SettlementDate?.ToString("yyyy-MM-dd") ?? Highlight("N/A"));
            AddRow(sb, "Option Price", FormatPremium(leg));
            AddRow(sb, "Premium", FormatPremiumAmount(leg));
            AddRow(sb, "Premium Date", leg.PremiumDate?.ToString("yyyy-MM-dd") ?? Highlight("N/A"));
            sb.AppendLine("</table>");
        }

        // Total premium for multi-leg
        if (legs.Count > 1)
        {
            var totalPremium = legs.Sum(l => l.PremiumAmount ?? 0m);
            var ccy = legs[0].PremiumCurrency;
            string description = totalPremium > 0 ? $"Client receives {totalPremium:N2} {ccy}"
                               : totalPremium < 0 ? $"Client pays {Math.Abs(totalPremium):N2} {ccy}"
                               : "Net zero cost";
            sb.AppendLine($"<p style='font-weight: bold; font-size: 14px;'>{description}</p>");
        }

        // Hedge section
        if (hasHedge)
        {
            sb.AppendLine(@"<div style='background-color: #4472C4; color: white; padding: 8px 20px;
                font-weight: bold; margin-top: 15px;'>Delta Hedge</div>");

            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (leg.Hedge == HedgeType.No) continue;

                if (legs.Count(l => l.Hedge != HedgeType.No) > 1)
                    sb.AppendLine($"<h3 style='color: #4472C4;'>Leg {i + 1}</h3>");

                sb.AppendLine("<table style='border-collapse: collapse; margin: 10px 0;'>");
                AddRow(sb, "Hedge Type", leg.Hedge.ToString());
                AddRow(sb, "Notional Amount", FormatNotional(leg.HedgeNotional, leg.HedgeNotionalCurrency));
                AddRow(sb, "Client Buy/Sell", leg.HedgeBuySell.ToString());
                AddRow(sb, "Hedge Rate", leg.HedgeRate?.ToString("0.0000") ?? Highlight("N/A"));
                AddRow(sb, "Delivery Date", leg.HedgeSettlementDate?.ToString("yyyy-MM-dd") ?? Highlight("N/A"));
                sb.AppendLine("</table>");
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AddRow(StringBuilder sb, string label, string value)
    {
        sb.AppendLine($@"<tr>
            <td style='padding: 3px 15px 3px 5px; font-weight: bold; color: #333;'>{label}</td>
            <td style='padding: 3px 5px;'>{value}</td>
        </tr>");
    }

    private static string Highlight(string text) =>
        $"<span style='color: red; font-weight: bold;'>{text}</span>";

    private static string FormatNotional(decimal? notional, string currency) =>
        notional.HasValue ? $"{notional.Value:N0} {currency}" : Highlight("N/A");

    private static string FormatPremium(TradeLeg leg) =>
        leg.Premium.HasValue
            ? leg.PremiumStyle == PremiumStyle.Pips
                ? $"{leg.Premium.Value:N4} pips"
                : $"{leg.Premium.Value:N4}%"
            : Highlight("N/A");

    private static string FormatPremiumAmount(TradeLeg leg) =>
        leg.PremiumAmount.HasValue
            ? $"{leg.PremiumAmount.Value:N2} {leg.PremiumCurrency}"
            : Highlight("N/A");

    private static string GetCurrentUserEmail() =>
        $"{Environment.UserName}@swedbank.se"; // Adjust domain as needed
}
