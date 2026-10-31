using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaffleGame.Windows;

/// <summary>Plugin-local owner-centered confirmation; never dims or locks the entire game.</summary>
public sealed partial class MainWindow
{
    private void RequestConfirmation(Confirmation type)
    {
        if (this.confirmation != Confirmation.None) return;
        this.confirmation = type;
        this.confirmationFocusRequested = true;
    }

    private void DrawConfirmationWindow()
    {
        if (this.confirmation == Confirmation.None) return;
        var current = this.confirmation;
        const float width = 438f;
        const float height = 208f;
        var size = new Vector2(width, height);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowPos(ReyLayout.CenteredWindowPosition(this.lastWindowPosition, this.lastWindowSize, size), ImGuiCond.Always);
        if (this.confirmationFocusRequested)
        {
            ImGui.SetNextWindowFocus(); this.confirmationFocusRequested = false;
        }
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ReyLayout.Canvas);
        ImGui.PushStyleColor(ImGuiCol.Border, ReyLayout.SoftLine);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = true;
        if (ImGui.Begin("RaffleGame confirmation###rgConfirm", ref open,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var p = ImGui.GetWindowPos();
            var dl = ImGui.GetWindowDrawList();
            ReyLayout.Fill(dl, p, new Vector2(width, 50), ReyLayout.Rail, 12);
            ReyLayout.Fill(dl, p + new Vector2(0, 12), new Vector2(width, 38), ReyLayout.Rail);
            ReyLayout.Rule(dl, p + new Vector2(0, 49), p + new Vector2(width, 49), ReyLayout.SoftLine);
            var title = current switch
            {
                Confirmation.Draw => "DRAW WINNER?",
                Confirmation.Reset => "RESET RAFFLE?",
                Confirmation.Undo => "UNDO LAST SALE?",
                Confirmation.Clear => "CLEAR ALL SALES?",
                Confirmation.ResetMacros => "RESTORE DEFAULT MACROS?",
                _ => "DELETE SALE?",
            };
            ImGui.SetCursorScreenPos(p + new Vector2(18, 16));
            ImGui.TextColored(ReyLayout.Gold, title);
            if (ReyLayout.FlatButton("##rgConfirmClose", "X", p + new Vector2(width - 45, 10),
                new Vector2(30, 30), ReyLayout.Rose)) open = false;
            var sale = current == Confirmation.Undo ? this.plugin.RaffleSession.LastSale : this.pendingSale;
            string line1; string line2; string line3;
            switch (current)
            {
                case Confirmation.Draw:
                    line1 = $"Draw from {this.plugin.RaffleSession.TotalTickets} sold tickets?";
                    line2 = "Ticket sales/edits will lock until the roll completes.";
                    line3 = "Queued ads will be cancelled. Buyer tells must finish first.";
                    break;
                case Confirmation.ResetMacros:
                    line1 = "Replace every customized chat template with the defaults?";
                    line2 = "Raffle tickets, prizes and settings will not change.";
                    line3 = "This cannot be undone.";
                    break;
                case Confirmation.Reset:
                    line1 = "Delete all tickets and reset the winner?";
                    line2 = "This cannot be undone. Export first if needed.";
                    line3 = $"{this.plugin.RaffleSession.EntryCount} sale(s) | {this.plugin.RaffleSession.TotalPot:N0} gil pot";
                    break;
                case Confirmation.Clear:
                    line1 = "Permanently clear all sales history?";
                    line2 = "This clears current raffle sales and ticket ranges.";
                    line3 = "The base pot and settings will remain.";
                    break;
                default:
                    line1 = sale is null ? "Selected sale no longer exists."
                        : $"Remove {sale.Tickets} tickets for {sale.PlayerName}?";
                    line2 = sale is null ? "No changes will be made."
                        : $"{sale.Gil:N0} gil will be removed from sales.";
                    line3 = "Only the newest sale can be removed; issued numbers remain fixed.";
                    break;
            }
            ImGui.SetCursorScreenPos(p + new Vector2(18, 66)); ImGui.TextUnformatted(line1);
            ImGui.SetCursorScreenPos(p + new Vector2(18, 91)); ImGui.TextDisabled(line2);
            ImGui.SetCursorScreenPos(p + new Vector2(18, 117)); ImGui.TextDisabled(line3);
            if (ReyLayout.FlatButton("##rgConfirmCancel", "CANCEL", p + new Vector2(18, 156),
                new Vector2(120, 34), ReyLayout.Muted)) open = false;
            var valid = this.plugin.CanOperate && (current switch
            {
                Confirmation.Undo => !this.plugin.DrawPending && !this.plugin.RaffleSession.HasWinner &&
                    !this.plugin.GameChatService.HasPendingTicketTells && this.plugin.RaffleSession.LastSale is not null,
                Confirmation.Delete => !this.plugin.DrawPending && !this.plugin.RaffleSession.HasWinner &&
                    !this.plugin.GameChatService.HasPendingTicketTells && this.pendingSale is not null &&
                    ReferenceEquals(this.plugin.RaffleSession.LastSale, this.pendingSale),
                Confirmation.ResetMacros => !this.plugin.DrawPending,
                Confirmation.Draw => !this.plugin.DrawPending && !this.plugin.RaffleSession.HasWinner &&
                    !this.plugin.GameChatService.HasPendingTicketTells &&
                    this.plugin.RaffleSession.TotalTickets > 0 && !this.plugin.RaffleSession.IsPotOverflow,
                Confirmation.Clear => !this.plugin.DrawPending && !this.plugin.RaffleSession.HasWinner &&
                    !this.plugin.GameChatService.HasPendingTicketTells,
                _ => !this.plugin.GameChatService.HasPendingTicketTells,
            });
            var label = current switch
            {
                Confirmation.Draw => "DRAW NOW",
                Confirmation.Reset => "RESET RAFFLE",
                Confirmation.Undo => "UNDO SALE",
                Confirmation.Clear => "CLEAR ALL",
                Confirmation.ResetMacros => "RESTORE MACROS",
                _ => "DELETE SALE"
            };
            if (valid && ReyLayout.FlatButton("##rgConfirmApply", label,
                p + new Vector2(width - 177, 156), new Vector2(159, 34),
                current == Confirmation.Draw ? ReyLayout.Gold : ReyLayout.Rose, true))
            {
                switch (current)
                {
                    case Confirmation.Draw: this.plugin.QueueWinnerDraw(); break;
                    case Confirmation.Reset:
                        this.plugin.ResetRaffle();
                        this.giftRecipientName = string.Empty;
                        break;
                    case Confirmation.ResetMacros:
                        this.plugin.Configuration.ResetDefaultMacros();
                        this.plugin.Configuration.Save();
                        break;
                    case Confirmation.Undo:
                        if (!this.plugin.RaffleSession.RemoveLastSale())
                            Plugin.ChatGui.Print("[RaffleGame] Could not save undo; no sale removed.");
                        break;
                    case Confirmation.Clear:
                        if (!this.plugin.RaffleSession.ClearSalesLedger())
                            Plugin.ChatGui.Print("[RaffleGame] Could not save clear; sales preserved.");
                        break;
                    case Confirmation.Delete:
                        var index = this.plugin.Configuration.SalesLedger.IndexOf(this.pendingSale!);
                        if (!this.plugin.RaffleSession.RemoveSaleAt(index))
                            Plugin.ChatGui.Print("[RaffleGame] Only the latest sale may be deleted; no numbers reassigned.");
                        break;
                }
                this.exportStatus = string.Empty;
                open = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
        if (!open) { this.confirmation = Confirmation.None; this.pendingSale = null; }
    }
}
