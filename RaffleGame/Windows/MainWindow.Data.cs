using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace RaffleGame.Windows;

public sealed partial class MainWindow
{
    private void DrawRafflePage()
    {
        var session = this.plugin.RaffleSession;
        var width = ImGui.GetContentRegionAvail().X;
        var jackpotWidth = MathF.Floor((width - 20f) * .44f);
        var otherWidth = MathF.Floor((width - 20f - jackpotWidth) / 2f);
        this.Metric("WINNER POT", $"{session.TotalPot:N0} gil", ReyLayout.Gold, jackpotWidth);
        ImGui.SameLine(0, 10);
        this.Metric("TICKETS SOLD", $"{session.TotalTickets} / 999", ReyLayout.Ivory, otherWidth);
        ImGui.SameLine(0, 10);
        this.Metric("GROSS SALES", $"{session.GrossTicketSalesTotalGil:N0} gil", ReyLayout.Mist,
            width - 20f - jackpotWidth - otherWidth);
        ImGui.Dummy(new Vector2(0, 10));

        var leftWidth = MathF.Floor((width - 12f) * .53f);
        var rightWidth = width - 12f - leftWidth;
        if (ReyLayout.BeginPanel("##rgOperatorPanel", new Vector2(leftWidth, 290), 12f)) this.DrawOperatorPanel();
        ReyLayout.EndPanel();
        ImGui.SameLine(0, 12);
        if (ReyLayout.BeginPanel("##rgLivePanel", new Vector2(rightWidth, 290), 16f)) this.DrawLiveRafflePanel();
        ReyLayout.EndPanel();
    }

    private void DrawOperatorPanel()
    {
        var config = this.plugin.Configuration;
        var session = this.plugin.RaffleSession;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 5f));
        RaffleGameTheme.Heading("PLAYER & PURCHASE");
        ImGui.TextDisabled("BUYER");
        var name = config.TargetPlayerName;
        ImGui.SetNextItemWidth(MathF.Max(100f, ImGui.GetContentRegionAvail().X - 162f));
        if (ImGui.InputText("##rgBuyer", ref name, 128))
        { config.TargetPlayerName = name; config.Save(); }
        ImGui.SameLine(0, 7);
        if (ReyLayout.Button("##rgUseTarget", "TARGET", new Vector2(78, 32), ReyLayout.Mint, true))
            this.plugin.SetTargetFromCurrentGameTarget();
        ImGui.SameLine(0, 7);
        if (ReyLayout.Button("##rgClearBuyer", "X", new Vector2(31, 32), ReyLayout.Rose))
        { config.TargetPlayerName = string.Empty; config.Save(); }

        ImGui.TextDisabled("GIFT TO (OPTIONAL)");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##rgGiftOwner", ref this.giftRecipientName, 128);

        var limit = config.MaxTicketsPerPurchase > 0
            ? Math.Min(session.RemainingTickets, config.MaxTicketsPerPurchase)
            : session.RemainingTickets;
        ImGui.TextDisabled($"TICKETS (MAX {limit})");
        ImGui.SetNextItemWidth(96);
        ImGui.InputInt("##rgTicketsToAdd", ref this.ticketsToAdd);
        this.ticketsToAdd = Math.Clamp(this.ticketsToAdd, 1, 999);
        var priceFits = config.PricePerTicket >= 0 &&
            config.PricePerTicket <= long.MaxValue / this.ticketsToAdd;
        ImGui.SameLine(0, 8);
        ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending || session.HasWinner ||
            session.RemainingTickets == 0 || this.ticketsToAdd > limit || !priceFits ||
            string.IsNullOrWhiteSpace(config.TargetPlayerName));
        if (ReyLayout.Button("##rgSell", priceFits ? $"SELL · {config.PricePerTicket * this.ticketsToAdd:N0} GIL" : "INVALID PRICE",
            new Vector2(198, 34), ReyLayout.Gold, true) &&
            this.plugin.AddTicketsForConfiguredTarget(this.ticketsToAdd, this.giftRecipientName))
            this.giftRecipientName = string.Empty;
        ImGui.EndDisabled();

        ImGui.Separator();
        RaffleGameTheme.Heading("RAFFLE CONTROLS");
        ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending);
        if (ReyLayout.Button("##rgAd", "SHOUT RAFFLE AD", new Vector2(154, 32), ReyLayout.Mist))
            this.plugin.ShoutRaffleAnnouncement();
        ImGui.SameLine(0, 8);
        if (ReyLayout.Button("##rgReset", "RESET", new Vector2(94, 32), ReyLayout.Rose))
            this.RequestResetConfirmation();
        ImGui.EndDisabled();
        ImGui.PopStyleVar();
    }

    private void DrawLiveRafflePanel()
    {
        var session = this.plugin.RaffleSession;
        RaffleGameTheme.Heading("LIVE RAFFLE");
        if (!this.plugin.CanOperate)
            ImGui.TextColored(ReyLayout.Rose, "Saved raffle data requires recovery; see dalamud.log.");
        else if (session.IsPotOverflow)
            ImGui.TextColored(ReyLayout.Rose, "Prize pot exceeds supported gil range.");
        else if (this.plugin.DrawPending)
            ImGui.TextColored(ReyLayout.Gold, "Drawing winner… ticket edits locked.");
        else if (session.HasWinner)
            ImGui.TextColored(ReyLayout.Gold, $"Winner: {session.LastWinnerName} · Ticket {session.WinningTicket}");
        else if (session.LastSale is { } last)
        {
            ImGui.TextDisabled("LAST SALE");
            ImGui.TextUnformatted($"{last.PlayerName} · {last.TicketRange}");
            if (last.IsGift) ImGui.TextDisabled($"Bought by {last.BuyerName}");
        }
        else ImGui.TextDisabled("Waiting for ticket sales");

        // Actions remain anchored at the bottom, even when there is no sale yet.
        var remaining = ImGui.GetContentRegionAvail().Y;
        if (remaining > 98) ImGui.Dummy(new Vector2(0, remaining - 98));
        ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending ||
            this.plugin.GameChatService.HasPendingTicketTells ||
            session.TotalTickets == 0 || session.HasWinner || session.IsPotOverflow);
        if (ReyLayout.Button("##rgDraw", "DRAW WINNER", new Vector2(168, 34), ReyLayout.Gold, true))
            this.RequestConfirmation(Confirmation.Draw);
        ImGui.EndDisabled();
        ImGui.SameLine(0, 8);
        ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending || session.LastSale is null);
        if (ReyLayout.Button("##rgTell", "TELL LAST BUYER", new Vector2(154, 34), ReyLayout.Mint))
            this.plugin.TellLastBuyerTickets();
        ImGui.EndDisabled();
    }

    private void DrawTicketsPage()
    {
        var session = this.plugin.RaffleSession;
        var config = this.plugin.Configuration;
        RaffleGameTheme.Heading("SALES HISTORY");
        if (ReyLayout.Button("##rgExport", "EXPORT CSV", new Vector2(126, 32), ReyLayout.Mist, true))
            this.ExportSalesLedgerToCsv();
        ImGui.SameLine(0, 7);
        if (ReyLayout.Button("##rgFolder", "OPEN EXPORTS", new Vector2(142, 32), ReyLayout.Muted))
            this.OpenExportFolder();
        ImGui.SameLine(0, 16);
        ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending || session.HasWinner ||
            this.plugin.GameChatService.HasPendingTicketTells || session.EntryCount == 0);
        if (ReyLayout.Button("##rgUndo", "UNDO LAST", new Vector2(126, 32), ReyLayout.Gold, true))
            this.RequestConfirmation(Confirmation.Undo);
        ImGui.SameLine(0, 7);
        if (ReyLayout.Button("##rgClear", "CLEAR SALES", new Vector2(126, 32), ReyLayout.Rose, true))
            this.RequestConfirmation(Confirmation.Clear);
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(this.exportStatus))
        {
            ImGui.TextDisabled(this.exportStatus);
        }
        ImGui.Dummy(new Vector2(0, 5));
        if (session.EntryCount == 0)
        {
            ImGui.TextDisabled("No ticket sales yet.");
            if (ReyLayout.Button("##rgReturnRaffle", "GO TO RAFFLE", new Vector2(156, 32), ReyLayout.Gold))
                this.OpenRaffle();
            return;
        }

        var tableHeight = MathF.Max(1, ImGui.GetContentRegionAvail().Y - 2);
        if (ImGui.BeginTable("##rgSalesLedger", 7,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 32);
            ImGui.TableSetupColumn("Owner");
            ImGui.TableSetupColumn("Buyer");
            ImGui.TableSetupColumn("Tix", ImGuiTableColumnFlags.WidthFixed, 42);
            ImGui.TableSetupColumn("Range", ImGuiTableColumnFlags.WidthFixed, 94);
            ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthFixed, 103);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 152);
            ImGui.TableHeadersRow();
            for (var i = 0; i < config.SalesLedger.Count; i++)
            {
                var sale = config.SalesLedger[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted((i + 1).ToString());
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(sale.PlayerName);
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(sale.BuyerName);
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(sale.Tickets.ToString());
                ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(sale.TicketRange);
                ImGui.TableSetColumnIndex(5); ImGui.TextUnformatted($"{sale.Gil:N0}");
                ImGui.TableSetColumnIndex(6);
                if (ReyLayout.Button($"##rgTell{i}", "TELL", new Vector2(62, 27), ReyLayout.Mint))
                    this.plugin.TellTicketSaleToCurrentTarget(sale);
                ImGui.SameLine(0, 6);
                ImGui.BeginDisabled(!this.plugin.CanOperate || this.plugin.DrawPending || session.HasWinner ||
                    this.plugin.GameChatService.HasPendingTicketTells || i != config.SalesLedger.Count - 1);
                if (ReyLayout.Button($"##rgDelete{i}", "DELETE", new Vector2(76, 27), ReyLayout.Rose))
                { this.pendingSale = sale; this.RequestConfirmation(Confirmation.Delete); }
                ImGui.EndDisabled();
            }
            ImGui.EndTable();
        }
    }

    private void ExportSalesLedgerToCsv()
    {
        var config = this.plugin.Configuration;
        var session = this.plugin.RaffleSession;
        if (session.EntryCount == 0)
        { this.exportStatus = "No raffle sales to export."; return; }
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) throw new IOException("Documents folder unavailable.");
            var directory = Path.Combine(documents, "RaffleGame");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"RaffleGame-sales-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv");
            var csv = new StringBuilder();
            csv.AppendLine("Row,CreatedAtLocal,Owner,Buyer,Tickets,TicketNumbers,Gil");
            for (var i = 0; i < config.SalesLedger.Count; i++)
            {
                var sale = config.SalesLedger[i];
                csv.AppendCsv((i + 1).ToString(CultureInfo.InvariantCulture)); csv.Append(',');
                csv.AppendCsv(sale.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); csv.Append(',');
                csv.AppendCsv(sale.PlayerName); csv.Append(',');
                csv.AppendCsv(sale.BuyerName); csv.Append(',');
                csv.AppendCsv(sale.Tickets.ToString(CultureInfo.InvariantCulture)); csv.Append(',');
                csv.AppendCsv(sale.TicketRange); csv.Append(',');
                csv.AppendCsv(sale.Gil.ToString(CultureInfo.InvariantCulture)); csv.AppendLine();
            }
            csv.AppendLine();
            csv.AppendLine("Summary,Value");
            void Row(string label, string value) { csv.AppendCsv(label); csv.Append(','); csv.AppendCsv(value); csv.AppendLine(); }
            Row("Tickets", session.TotalTickets.ToString(CultureInfo.InvariantCulture));
            Row("GrossSalesGil", session.GrossTicketSalesTotalGil.ToString(CultureInfo.InvariantCulture));
            Row("VenueHostSalesPercent", session.VenueHostSalesPercent.ToString("0.#", CultureInfo.InvariantCulture));
            Row("VenueHostShareGil", session.VenueHostShareGil.ToString(CultureInfo.InvariantCulture));
            Row("PrizeTicketSalesGil", session.PrizeTicketSalesGil.ToString(CultureInfo.InvariantCulture));
            Row("BasePotGil", config.BasePot.ToString(CultureInfo.InvariantCulture));
            Row("TotalPotGil", session.TotalPot.ToString(CultureInfo.InvariantCulture));
            if (session.HasWinner)
            { Row("WinnerName", session.LastWinnerName); Row("WinningTicket", session.WinningTicket!.Value.ToString()); }
            File.WriteAllText(file, csv.ToString(), Encoding.UTF8);
            this.lastExportDirectory = directory;
            this.exportStatus = $"Exported: {file}";
        }
        catch (Exception ex) { this.exportStatus = $"Export failed: {ex.Message}"; }
    }

    private string GetExportDirectory()
    {
        if (!string.IsNullOrWhiteSpace(this.lastExportDirectory)) return this.lastExportDirectory;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents)) throw new IOException("Documents folder unavailable.");
        return Path.Combine(documents, "RaffleGame");
    }
    private void OpenExportFolder()
    {
        try
        {
            var dir = this.GetExportDirectory(); Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            this.exportStatus = $"Opened: {dir}";
        }
        catch (Exception ex) { this.exportStatus = $"Cannot open folder: {ex.Message}"; }
    }

}

internal static class CsvExtensions
{
    public static void AppendCsv(this StringBuilder b, string? value)
    {
        value ??= string.Empty;
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) { b.Append(value); return; }
        b.Append('"'); b.Append(value.Replace("\"", "\"\"")); b.Append('"');
    }
}
