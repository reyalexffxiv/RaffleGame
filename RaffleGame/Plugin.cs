using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RaffleGame.Models;
using RaffleGame.Services;
using RaffleGame.Windows;

namespace RaffleGame;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IReliableFileStorage ReliableFileStorage { get; private set; } = null!;

    private static readonly string[] Commands = ["/rafflegame", "/raffle"];
    private static readonly Regex OwnRoll = new(
        @"^(?:Random!\s*)?You\s+roll\s+(?:a|an)\s+(?<roll>\d{1,3})\.$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RandomLine = new(@"^/random(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private bool drawQueued;
    private bool waitingForRoll;
    private DateTime drawDeadlineUtc;
    private int drawTicketCount;

    // A brief guard prevents accidental double-click purchases in compact mode.
    // Repeat purchases for an intentionally selected player remain valid.
    private DateTime lastQuickSaleAtUtc = DateTime.MinValue;

    public Configuration Configuration { get; }
    public RaffleSession RaffleSession { get; }
    public RaffleStateStorage RaffleStateStorage { get; }
    public GameChatService GameChatService { get; }
    public string MigrationNotice { get; }
    public bool DrawPending => this.drawQueued || this.waitingForRoll;
    public bool MigrationFailed => this.MigrationNotice.StartsWith("Legacy VenueRaffle settings could not", StringComparison.Ordinal);
    public bool CanOperate => !this.RaffleStateStorage.StateLoadFailed &&
        !this.RaffleStateStorage.StateSaveFailed && !this.MigrationFailed;
    public ISharedImmediateTexture? MascotTexture { get; }
    public readonly WindowSystem WindowSystem = new("RaffleGame");
    private MainWindow MainWindow { get; }

    public Plugin()
    {
        var config = PluginInterface.GetPluginConfig() as Configuration;
        string migrationNotice = string.Empty;
        var migrationSucceeded = false;
        var existingNewState = System.IO.File.Exists(System.IO.Path.Combine(
            PluginInterface.ConfigDirectory.FullName, "raffle-state.json"));
        if (config is null && !existingNewState)
        {
            config = LegacyMigration.TryImport(PluginInterface, Log, out migrationNotice);
            migrationSucceeded = config is not null;
        }
        config ??= new Configuration();
        this.MigrationNotice = migrationNotice;
        this.Configuration = config;
        this.Configuration.Initialize(PluginInterface);
        if (this.Configuration.CleanLegacyManualWaitLinesOnce()) this.Configuration.Save();
        this.RaffleStateStorage = new RaffleStateStorage(PluginInterface, ReliableFileStorage, Log);
        this.RaffleStateStorage.LoadInto(this.Configuration);
        // Capture imported configuration in the new identity only. Legacy files stay untouched.
        if (migrationSucceeded && !this.RaffleStateStorage.StateLoadFailed)
        {
            this.Configuration.Save();
            this.RaffleStateStorage.Save(this.Configuration);
        }

        try
        {
            this.MascotTexture = TextureProvider.GetFromManifestResource(
                Assembly.GetExecutingAssembly(), "RaffleGame.Assets.peepoblankie.png");
        }
        catch (Exception ex) { Log.Warning(ex, "Could not load RaffleGame mascot."); }

        this.GameChatService = new GameChatService(Log);
        this.RaffleSession = new RaffleSession(this.Configuration, this.RaffleStateStorage);
        this.MainWindow = new MainWindow(this);
        this.WindowSystem.AddWindow(this.MainWindow);

        foreach (var command in Commands)
            CommandManager.AddHandler(command, new CommandInfo(this.OnCommand)
            { HelpMessage = "Open RaffleGame; use 'draw', 'target', 'tell', 'announce', or 'reset'." });
        Framework.Update += this.OnFrameworkUpdate;
        ChatGui.ChatMessage += this.OnChatMessage;
        PluginInterface.UiBuilder.Draw += this.WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUi;
        Log.Information("RaffleGame loaded.");
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= this.OnChatMessage;
        Framework.Update -= this.OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= this.WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMainUi;
        foreach (var command in Commands) CommandManager.RemoveHandler(command);
        this.GameChatService.ClearPendingCommands();
        this.WindowSystem.RemoveAllWindows();
        this.MainWindow.Dispose();
        if (this.MascotTexture is IDisposable texture) texture.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (this.DrawPending && DateTime.UtcNow > this.drawDeadlineUtc)
            this.CancelDraw("Winner draw timed out. No winner selected; try again.");
        this.GameChatService.FlushOnePendingCommand();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            // Destructive commands open a local confirmation, never reset silently.
            case "reset": this.MainWindow.RequestResetConfirmation(); this.MainWindow.IsOpen = true; break;
            case "draw": this.QueueWinnerDraw(); break;
            case "tell": this.TellLastBuyerTickets(); break;
            case "announce": this.ShoutRaffleAnnouncement(); break;
            case "target": this.SetTargetFromCurrentGameTarget(); break;
            default: this.MainWindow.Toggle(); break;
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!this.waitingForRoll || message.LogKind != XivChatType.RandomNumber ||
            DateTime.UtcNow > this.drawDeadlineUtc) return;
        var text = CleanChatText(message.Message.TextValue);
        var match = OwnRoll.Match(text);
        if (!match.Success || !int.TryParse(match.Groups["roll"].Value, out var ticket) ||
            ticket < 1 || ticket > this.drawTicketCount) return;
        this.drawQueued = false;
        this.waitingForRoll = false;
        this.drawDeadlineUtc = DateTime.MinValue;
        var winner = this.RaffleSession.FindTicket(ticket);
        if (winner is null)
        {
            this.CancelDraw($"Rolled {ticket}, but no matching ticket exists. Review entries and redraw.");
            return;
        }
        this.CompleteDraw(ticket, winner);
    }

    private static string CleanChatText(string text) => text
        .Replace('\u00A0', ' ').Replace("", " ").Replace("", " ")
        .Replace("", " ").Replace("", " ").Replace("", " ").Replace("", " ").Trim();

    public void SetTargetFromCurrentGameTarget()
    {
        if (TargetManager.Target is not IPlayerCharacter target)
        {
            ChatGui.Print("[RaffleGame] Select a player target first.");
            return;
        }
        this.Configuration.TargetPlayerName = target.Name.TextValue.Trim();
        this.Configuration.Save();
    }

    public bool AddTicketsForConfiguredTarget(int tickets, string? giftedTo = null)
    {
        if (!this.CanOperate) return false;
        if (this.DrawPending)
        {
            ChatGui.Print("[RaffleGame] Ticket sales are locked during a draw.");
            return false;
        }
        var buyer = this.Configuration.TargetPlayerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(buyer))
        {
            ChatGui.Print("[RaffleGame] Choose the buyer before selling or gifting tickets.");
            return false;
        }
        var owner = string.IsNullOrWhiteSpace(giftedTo) ? buyer : giftedTo.Trim();
        if (owner.Contains('<') || owner.Contains('>') || owner.Contains('\r') || owner.Contains('\n'))
        {
            ChatGui.Print("[RaffleGame] Invalid beneficiary name.");
            return false;
        }
        if (!this.RaffleSession.TryAddTicketSale(owner, tickets, out _, out var error, buyer))
        {
            ChatGui.Print($"[RaffleGame] {error}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Compact quick sale always uses the player actually targeted in the game.
    /// It never consumes Configuration.TargetPlayerName, which may be stale.
    /// </summary>
    public bool CanQuickSellMax(out int amount, out string reason)
    {
        amount = 0;
        reason = string.Empty;
        if (!this.CanOperate) { reason = "Saved raffle data needs recovery."; return false; }
        if (this.DrawPending || this.RaffleSession.HasWinner)
        { reason = "Sales are locked during/after the winner draw."; return false; }
        if (this.RaffleSession.IsPotOverflow)
        { reason = "The prize pot exceeds the supported gil range."; return false; }
        if (TargetManager.Target is not IPlayerCharacter player)
        { reason = "Target a player in FFXIV before pressing SELL MAX."; return false; }
        var name = player.Name.TextValue.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Contains('<') || name.Contains('>') ||
            name.Contains('\r') || name.Contains('\n'))
        { reason = "The current player target has an invalid name."; return false; }
        if ((DateTime.UtcNow - this.lastQuickSaleAtUtc).TotalMilliseconds < 900)
        { reason = "A compact sale was just recorded. Avoid duplicate clicks."; return false; }
        amount = this.Configuration.MaxTicketsPerPurchase > 0
            ? Math.Min(this.Configuration.MaxTicketsPerPurchase, this.RaffleSession.RemainingTickets)
            : this.RaffleSession.RemainingTickets;
        if (amount <= 0)
        { reason = "No raffle tickets remain."; return false; }
        if (this.Configuration.PricePerTicket < 0 ||
            this.Configuration.PricePerTicket > long.MaxValue / amount)
        { reason = "The ticket price is invalid or exceeds the supported gil range."; return false; }
        return true;
    }

    public void QuickSellMax()
    {
        if (!this.CanQuickSellMax(out var amount, out var reason))
        { ChatGui.Print($"[RaffleGame] {reason}"); return; }
        if (TargetManager.Target is not IPlayerCharacter player) return;
        var buyer = player.Name.TextValue.Trim();
        if (!this.RaffleSession.TryAddTicketSale(buyer, amount, out var sale, out var error, buyer))
        { ChatGui.Print($"[RaffleGame] {error}"); return; }
        if (sale is null) return;
        // Sale was durably persisted before the ticket tell is queued. If target
        // changes before send, GameChatService cancels the tell, not the sale.
        this.lastQuickSaleAtUtc = DateTime.UtcNow;
        this.Configuration.TargetPlayerName = buyer;
        this.Configuration.Save();
        this.TellTicketSale(sale);
        ChatGui.Print($"[RaffleGame] Sold {amount} tickets ({sale.TicketRange}) to {buyer}. " +
            "Ticket tell queued; keep the buyer targeted until it is sent.");
    }

    public void TellLastBuyerTickets()
    {
        if (this.RaffleSession.LastSale is { } sale) this.TellTicketSale(sale);
    }
    public void TellTicketSaleToCurrentTarget(RaffleSaleRecord sale) => this.TellTicketSale(sale);

    private void TellTicketSale(RaffleSaleRecord sale)
    {
        if (TargetManager.Target is not IPlayerCharacter target)
        {
            ChatGui.Print("[RaffleGame] Select the intended tell recipient first.");
            return;
        }
        var expectedName = target.Name.TextValue.Trim();
        // A tell confirms a ticket purchase to its buyer. Never use a stale/unrelated target.
        if (!string.Equals(expectedName, sale.BuyerName, StringComparison.OrdinalIgnoreCase))
        {
            ChatGui.Print($"[RaffleGame] Select the buyer ({sale.BuyerName}) before sending this ticket tell.");
            return;
        }
        var expectedId = target.GameObjectId;
        var message = this.RenderMacro(sale.IsGift
            ? this.Configuration.TellOtherNameMessageTemplate : this.Configuration.TellTargetMessageTemplate,
            sale, sale.IsGift ? sale.PlayerName : null);
        if (!this.GameChatService.QueueTellCurrentTarget(message, () =>
            TargetManager.Target is IPlayerCharacter current &&
            current.GameObjectId == expectedId &&
            string.Equals(current.Name.TextValue.Trim(), expectedName, StringComparison.OrdinalIgnoreCase),
            () => ChatGui.Print("[RaffleGame] Ticket tell cancelled: target changed or send failed.")))
            ChatGui.Print("[RaffleGame] Ticket tell could not be queued. Select the buyer and use TELL to retry.");
    }

    public void ShoutRaffleAnnouncement()
    {
        if (!this.CanOperate || this.DrawPending || this.RaffleSession.IsPotOverflow) return;
        this.GameChatService.QueueMacroCommands(this.RenderMacro(this.Configuration.RaffleAdMacro),
            SafeDelay(this.Configuration.RaffleAdDelaySeconds));
    }

    public void QueueWinnerDraw()
    {
        if (!this.CanOperate || this.DrawPending || this.RaffleSession.HasWinner || this.RaffleSession.IsPotOverflow) return;
        if (this.GameChatService.HasPendingTicketTells)
        {
            ChatGui.Print("[RaffleGame] Pending buyer tells must finish (or be cancelled) before drawing.");
            return;
        }
        var tickets = this.RaffleSession.TotalTickets;
        if (tickets is < 1 or > Services.RaffleSession.MaxTotalTickets) return;
        // A raffle with just one ticket has a deterministic winner. /random 1 is not a valid FFXIV command.
        if (tickets == 1)
        {
            var only = this.RaffleSession.FindTicket(1);
            if (only is not null)
            {
                this.RaffleSession.SetDrawing(true);
                this.CompleteDraw(1, only);
            }
            return;
        }
        // Never accept a user macro's arbitrary /random argument. There must be exactly one,
        // plugin-controlled random command, sent only after countdown lines have finished.
        var lines = this.RenderMacro(this.Configuration.WinnerDrawMacro)
            .Replace("\r", string.Empty).Split('\n');
        var countdown = string.Join("\n", Array.FindAll(lines, line => !RandomLine.IsMatch(line.Trim())));
        this.GameChatService.ClearPendingCommands();
        this.RaffleSession.SetDrawing(true);
        this.drawQueued = true;
        this.drawTicketCount = tickets;
        var delay = SafeDelay(this.Configuration.WinnerDrawDelaySeconds);
        this.GameChatService.QueueMacroCommands(countdown, delay);
        if (!this.GameChatService.QueueCommandWithCallbacks($"/random {tickets}", TimeSpan.Zero,
            () => { this.drawQueued = false; this.waitingForRoll = true;
                    this.drawDeadlineUtc = DateTime.UtcNow.AddSeconds(30); },
            () => this.CancelDraw("The /random command could not be sent."))) return;
        // Wait for the countdown queue. Deadline begins when /random is actually dispatched.
        this.drawDeadlineUtc = DateTime.UtcNow.AddSeconds(
            Math.Max(30, Math.Min(300, (lines.Length + 1) * delay.TotalSeconds + 35)));
    }

    private void CompleteDraw(int ticket, RaffleSaleRecord winner)
    {
        this.drawQueued = false;
        this.waitingForRoll = false;
        if (!this.RaffleSession.CompleteDraw(ticket, winner))
        {
            ChatGui.Print("[RaffleGame] WINNER NOT SAVED. No announcement sent; check dalamud.log and recover the raffle state.");
            return;
        }
        this.GameChatService.QueueMacroCommands(
            this.RenderMacro(this.Configuration.WinnerResultMacro, winner, winningTicket: ticket),
            SafeDelay(this.Configuration.WinnerResultDelaySeconds));
        ChatGui.Print($"[RaffleGame] Winner: {winner.PlayerName}, ticket {ticket}.");
    }

    public void CancelDraw(string reason)
    {
        this.drawQueued = false;
        this.waitingForRoll = false;
        this.drawDeadlineUtc = DateTime.MinValue;
        this.RaffleSession.SetDrawing(false);
        this.GameChatService.ClearPendingCommands();
        ChatGui.Print($"[RaffleGame] {reason}");
    }

    public void ResetRaffle()
    {
        if (!this.CanOperate) return;
        if (this.GameChatService.HasPendingTicketTells)
        {
            ChatGui.Print("[RaffleGame] Pending buyer tells must finish before resetting.");
            return;
        }
        if (!this.RaffleSession.Reset())
        {
            ChatGui.Print("[RaffleGame] Reset was NOT saved. Raffle entries were preserved.");
            return;
        }
        this.GameChatService.ClearPendingCommands();
        this.drawQueued = false;
        this.waitingForRoll = false;
        this.drawDeadlineUtc = DateTime.MinValue;
        this.lastQuickSaleAtUtc = DateTime.MinValue;
    }

    private string RenderMacro(string? template, RaffleSaleRecord? sale = null,
        string? otherName = null, int? winningTicket = null)
    {
        var maxTickets = this.Configuration.MaxTicketsPerPurchase > 0
            ? this.Configuration.MaxTicketsPerPurchase.ToString(CultureInfo.InvariantCulture) : "unlimited";
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{Pot}"] = this.RaffleSession.TotalPot.ToString("N0", CultureInfo.InvariantCulture),
            ["{PricePerTicket}"] = this.Configuration.PricePerTicket.ToString("N0", CultureInfo.InvariantCulture),
            ["{MaxTickets}"] = maxTickets,
            ["{TotalTickets}"] = this.RaffleSession.TotalTickets.ToString(CultureInfo.InvariantCulture),
            ["{TicketSales}"] = this.RaffleSession.GrossTicketSalesTotalGil.ToString("N0", CultureInfo.InvariantCulture),
            ["{GrossTicketSales}"] = this.RaffleSession.GrossTicketSalesTotalGil.ToString("N0", CultureInfo.InvariantCulture),
            ["{PrizeTicketSales}"] = this.RaffleSession.PrizeTicketSalesGil.ToString("N0", CultureInfo.InvariantCulture),
            ["{VenueHostPercent}"] = this.RaffleSession.VenueHostSalesPercent.ToString("0.#", CultureInfo.InvariantCulture),
            ["{VenueHostShare}"] = this.RaffleSession.VenueHostShareGil.ToString("N0", CultureInfo.InvariantCulture),
            ["{Entries}"] = this.RaffleSession.EntryCount.ToString(CultureInfo.InvariantCulture),
            ["{TargetName}"] = this.Configuration.TargetPlayerName ?? string.Empty,
            ["{OtherName}"] = otherName ?? string.Empty,
            ["{WinnerName}"] = sale?.PlayerName ?? string.Empty,
            ["{WinningTicket}"] = winningTicket?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["{TicketRange}"] = sale?.TicketRange ?? string.Empty,
        };
        var result = template ?? string.Empty;
        foreach (var replacement in replacements)
            result = result.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static TimeSpan SafeDelay(float seconds) => TimeSpan.FromSeconds(
        float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 60f) : 0f);

    public void ToggleConfigUi() => this.MainWindow.OpenSetup();
    public void ToggleMainUi() => this.MainWindow.OpenRaffle();
}
