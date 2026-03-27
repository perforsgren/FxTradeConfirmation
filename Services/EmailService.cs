using System.Globalization;
using System.Text;
using FxTradeConfirmation.Models;
using System.Runtime.InteropServices;

namespace FxTradeConfirmation.Services;

public class EmailService : IEmailService
{
    private const string Orange = "#E26B0A";
    private const int ColLabelWidth = 115;
    private const int ColBadgeWidth = 65;
    private const int ColLegWidth = 90;

    public void SendTradeConfirmation(IReadOnlyList<TradeLeg> legs, ReferenceData referenceData)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Outlook is not installed or not registered.");

        dynamic? app = null;
        dynamic? mail = null;

        try
        {
            app = Activator.CreateInstance(outlookType)!;
            mail = app.CreateItem(0); // olMailItem

            var ccyPair = legs[0].CurrencyPair;
            var tradeDate = DateTime.Now.ToShortDateString();

            mail.Subject = $"FX Option Trade Confirmation {tradeDate} - {ccyPair}";

            // BCC all email addresses from DB
            var bccAddresses = string.Join(";", referenceData.EmailAddresses);
            if (!string.IsNullOrEmpty(bccAddresses))
                mail.BCC = bccAddresses;

            mail.CC = $"{Environment.UserName}";

            // DRAX broker gets specific To-addresses
            if (legs.Any(l => l.Broker == "DRAX"))
                mail.To = "Fxsales@jbdh.com; middleoffice@jbdh.com; fxpb1@natwestmarkets.com";

            mail.HTMLBody = BuildHtmlBody(legs);
            mail.Display();
        }
        finally
        {
            if (mail is not null)
                Marshal.ReleaseComObject(mail);

            if (app is not null)
                Marshal.ReleaseComObject(app);
        }
    }

    private static string BuildHtmlBody(IReadOnlyList<TradeLeg> legs)
    {
        int legCount = legs.Count;
        int totalCols = legCount + 2; // label + badge + N legs
        bool isDrax = legs.Any(l => l.Broker == "DRAX");
        bool hasHedge = legs.Any(l => l.Hedge != HedgeType.No);
        string ccyPair = legs[0].CurrencyPair;

        var sb = new StringBuilder();
        sb.AppendLine("<p></p>");

        if (isDrax)
        {
            sb.AppendLine("<span style='font-size:11pt; font-weight:bold; background-color:yellow;'>Manual give up please</span>");
            sb.AppendLine("<br><br>");
        }

        sb.AppendLine("<table border='0' cellpadding='0' cellspacing='0'>");

        // ── Header: "Swedbank" ──
        sb.AppendLine("<tr style='font-size: 12pt'><td><b>Swedbank</b></td></tr>");

        // ── Orange banner: "Trade Confirmation" ──
        sb.Append($"<tr style='font-size: 12pt; background-color:{Orange}; color:#ffffff'>");
        sb.Append("<td style='border-bottom: 1px solid black;' colspan='2'><b>Trade Confirmation</b></td>");
        for (int i = 0; i < legCount; i++)
            sb.Append("<td style='border-bottom: 1px solid black;'></td>");
        sb.AppendLine("</tr>");

        // ── Sub-header: "FX Vanilla Option" or leg labels ──
        if (legCount == 1)
        {
            sb.AppendLine($"<tr style='font-size: 11pt; padding-bottom: 5pt; padding-top: 5pt;'><td><font color='{Orange}'><b>FX Vanilla Option</b></font></td></tr>");
        }
        else
        {
            sb.Append($"<tr style='font-size: 10pt'><font color='{Orange}'><td></td><td></td>");
            for (int i = 0; i < legCount; i++)
                sb.Append("<td style='padding-bottom: 0pt; padding-top: 3pt;'><b>Vanilla</b></td>");
            sb.AppendLine("</font></tr>");

            sb.Append("<tr style='font-size: 10pt'><td></td><td></td>");
            for (int i = 0; i < legCount; i++)
                sb.Append($"<td style='padding-bottom: 3pt; padding-top: 1pt;'><b>Leg {i + 1}</b></td>");
            sb.AppendLine("</tr>");
        }

        // ── Double-line separator ──
        sb.Append("<tr>");
        for (int i = 0; i < totalCols; i++)
            sb.Append("<td style='border-bottom: 3pt double black; padding-bottom: 0pt;'></td>");
        sb.AppendLine("</tr>");
        SpacerRow(sb, totalCols);

        // ══════════════════════════════════════════
        //  Section 1: Trade Date, Contract, Buy/Sell
        // ══════════════════════════════════════════

        RowStart(sb, "Trade Date");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCell(sb, DateTime.Now.ToShortDateString());
        RowEnd(sb);

        RowStart(sb, "Contract");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, ccyPair);
        RowEnd(sb);

        RowStart(sb, "Client Buy/Sell");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCell(sb, legs[i].BuySell.ToString());
        RowEnd(sb);

        // ── Empty row ──
        EmptyRow(sb);

        // ══════════════════════════════════════════
        //  Section 2: Call/Put, Strike, Notional
        // ══════════════════════════════════════════

        RowStart(sb, "Call/Put");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCell(sb, legs[i].CallPut.ToString());
        RowEnd(sb);

        RowStart(sb, "Strike");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, legs[i].Strike?.ToString("0.0000"));
        RowEnd(sb);

        RowStart(sb, "Notional Amount");
        BadgeCell(sb, legs[0].NotionalCurrency);
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, FormatNumber(legs[i].Notional, false));
        RowEnd(sb);

        // ── Empty row ──
        EmptyRow(sb);

        // ══════════════════════════════════════════
        //  Section 3: Cut, Expiry, Delivery
        // ══════════════════════════════════════════

        RowStart(sb, "Expiry Cut");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCell(sb, legs[i].Cut);
        RowEnd(sb);

        RowStart(sb, "Expiry Date");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, legs[i].ExpiryDate?.ToShortDateString());
        RowEnd(sb);

        RowStart(sb, "Delivery Date");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, legs[i].SettlementDate?.ToShortDateString());
        RowEnd(sb);

        // ── Empty row ──
        EmptyRow(sb);

        // ══════════════════════════════════════════
        //  Section 4: Option Price, Premium, Premium Date
        // ══════════════════════════════════════════

        decimal totalPremium = 0m;

        RowStart(sb, "Option Price");
        BadgeCell(sb, GetPremiumStyleBadge(legs[0]));
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, FormatPremium(legs[i]));
        RowEnd(sb);

        RowStart(sb, "Premium");
        BadgeCell(sb, legs[0].PremiumCurrency);
        for (int i = 0; i < legCount; i++)
        {
            if (legs[i].PremiumAmount.HasValue)
            {
                totalPremium += legs[i].PremiumAmount.Value;
                LegCell(sb, FormatNumber(legs[i].PremiumAmount, false));
            }
            else
            {
                LegCellNA(sb);
            }
        }
        RowEnd(sb);

        RowStart(sb, "Premium Date");
        BadgeCell(sb, "");
        for (int i = 0; i < legCount; i++)
            LegCellOrNA(sb, legs[i].PremiumDate?.ToShortDateString());
        RowEnd(sb);

        // ── Empty row ──
        EmptyRow(sb);

        sb.AppendLine("</table>");

        // ══════════════════════════════════════════
        //  Total Premium (multi-leg only)
        // ══════════════════════════════════════════

        if (legCount > 1)
        {
            var premCcy = legs[0].PremiumCurrency;

            sb.AppendLine("<table border='0' cellpadding='0' cellspacing='0'>");
            sb.Append($"<tr style='font-size: 10pt'><font color='{Orange}'><td width='{ColLabelWidth}'><b>Total Premium: </b></font></td>");

            if (totalPremium > 0)
                sb.Append($"<td><font color='{Orange}'><b>Client receives {FormatNumber(totalPremium, true)} {premCcy}</b></font></td>");
            else if (totalPremium < 0)
                sb.Append($"<td><font color='{Orange}'><b>Client pays {FormatNumber(Math.Abs(totalPremium), true)} {premCcy}</b></font></td>");
            else
                sb.Append($"<td><font color='{Orange}'><b>Net zero cost</b></font></td>");

            sb.AppendLine("</tr>");
            EmptyRow(sb);
            sb.AppendLine("</table>");
        }

        // ══════════════════════════════════════════
        //  Hedge section
        // ══════════════════════════════════════════

        if (hasHedge)
        {
            sb.AppendLine("<p></p>");
            sb.AppendLine("<table border='0' cellpadding='0' cellspacing='0'>");

            // Orange hedge banner
            sb.Append($"<tr style='font-size: 12pt; background-color:{Orange};'>");
            sb.Append("<td style='color: white; border-bottom: 1px solid black;'><b>Delta Hedge</b></td>");
            for (int i = 1; i < totalCols; i++)
                sb.Append("<td style='border-bottom: 1px solid black;'></td>");
            sb.AppendLine("</tr>");
            SpacerRow(sb, totalCols);

            // Hedge Type
            RowStart(sb, "Hedge Type");
            BadgeCell(sb, "");
            for (int i = 0; i < legCount; i++)
            {
                var h = legs[i].Hedge;
                LegCell(sb, h == HedgeType.No ? "" : h.ToString());
            }
            RowEnd(sb);

            // Notional Amount (hedge)
            RowStart(sb, "Notional Amount");
            BadgeCell(sb, legs.FirstOrDefault(l => l.Hedge != HedgeType.No)?.HedgeNotionalCurrency ?? "");
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i].Hedge != HedgeType.No)
                    LegCellOrNA(sb, FormatNumber(legs[i].HedgeNotional.HasValue ? Math.Abs(legs[i].HedgeNotional.Value) : null, false));
                else
                    LegCell(sb, "");
            }
            RowEnd(sb);

            // Client Buy/Sell (hedge)
            RowStart(sb, "Client Buy/Sell");
            BadgeCell(sb, "");
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i].Hedge != HedgeType.No)
                    LegCellOrNA(sb, legs[i].HedgeBuySell.ToString());
                else
                    LegCell(sb, "");
            }
            RowEnd(sb);

            // Hedge Rate
            RowStart(sb, "Hedge Rate");
            BadgeCell(sb, "");
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i].Hedge != HedgeType.No)
                    LegCellOrNA(sb, legs[i].HedgeRate?.ToString("0.0000"));
                else
                    LegCell(sb, "");
            }
            RowEnd(sb);

            // Delivery Date (hedge)
            RowStart(sb, "Delivery Date");
            BadgeCell(sb, "");
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i].Hedge != HedgeType.No)
                    LegCellOrNA(sb, legs[i].HedgeSettlementDate?.ToShortDateString());
                else
                    LegCell(sb, "");
            }
            RowEnd(sb);

            sb.AppendLine("</table>");
        }
        else
        {
            sb.AppendLine("<p></p>");
            sb.AppendLine("<span style='font-size: 10pt'>Deal done without delta hedge</span>");
        }

        return sb.ToString();
    }

    // ── Table helpers ──

    private static void RowStart(StringBuilder sb, string label)
    {
        sb.Append($"<tr style='font-size: 10pt'><td width='{ColLabelWidth}'><b>{label}</b></td>");
    }

    private static void BadgeCell(StringBuilder sb, string badge)
    {
        if (!string.IsNullOrEmpty(badge))
            sb.Append($"<td width='{ColBadgeWidth}' style='text-align:center; vertical-align:middle;'><font color='{Orange}'><b>{badge}</b></font></td>");
        else
            sb.Append($"<td width='{ColBadgeWidth}'></td>");
    }

    private static void LegCell(StringBuilder sb, string? value)
    {
        sb.Append($"<td width='{ColLegWidth}'>{value}</td>");
    }

    private static void LegCellNA(StringBuilder sb)
    {
        sb.Append($"<td width='{ColLegWidth}'><font color='red'>N/A</font></td>");
    }

    private static void LegCellOrNA(StringBuilder sb, string? value)
    {
        if (string.IsNullOrEmpty(value))
            LegCellNA(sb);
        else
            LegCell(sb, value);
    }

    private static void RowEnd(StringBuilder sb) => sb.AppendLine("</tr>");

    private static void SpacerRow(StringBuilder sb, int totalCols)
    {
        sb.AppendLine($"<tr><td colspan='{totalCols}' style='height: 5px;'></td></tr>");
    }

    private static void EmptyRow(StringBuilder sb)
    {
        sb.AppendLine("<tr style='font-size: 10pt' bgcolor='#ffffff'>&nbsp</tr>");
    }

    // ── Formatting helpers ──

    private static string? FormatNumber(decimal? value, bool useDecimals)
    {
        if (!value.HasValue) return null;
        var v = value.Value;

        if (useDecimals)
        {
            var raw = v.ToString("G29", CultureInfo.InvariantCulture);
            int dotIdx = raw.IndexOf('.');
            int actualDecimals = dotIdx >= 0 ? raw.Length - dotIdx - 1 : 0;
            return v.ToString($"N{actualDecimals}", SwedishFormat);
        }

        return v.ToString("N0", SwedishFormat);
    }

    private static string? FormatPremium(TradeLeg leg)
    {
        if (!leg.Premium.HasValue) return null;

        // Premium (pips/pct) is stored as absolute value — derive the sign from PremiumAmount.
        // Buy = client pays = negative, Sell = client receives = positive.
        decimal sign = leg.PremiumAmount.HasValue && leg.PremiumAmount.Value < 0 ? -1m : 1m;
        var v = leg.Premium.Value * sign;

        bool isPct = leg.PremiumStyle is PremiumStyle.PctBase or PremiumStyle.PctQuote;

        if (isPct)
            return v.ToString("G29", CultureInfo.InvariantCulture) + "%";

        return FormatNumber(v, true);
    }

    private static string GetPremiumStyleBadge(TradeLeg leg)
    {
        return leg.PremiumStyle switch
        {
            PremiumStyle.PipsQuote => $"{leg.QuoteCurrency} pips",
            PremiumStyle.PctBase => $"%{leg.BaseCurrency}",
            PremiumStyle.PctQuote => $"%{leg.QuoteCurrency}",
            _ => ""
        };
    }

    private static readonly NumberFormatInfo SwedishFormat = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3]
    };
}