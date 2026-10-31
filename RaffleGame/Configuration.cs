using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Configuration;
using Dalamud.Plugin;
using RaffleGame.Models;

namespace RaffleGame;

/// <summary>
/// User-editable and persisted plugin settings. Values are intentionally simple so Dalamud can serialize them safely.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentConfigVersion = 1;

    public const string DefaultTellTargetMessageTemplate = "Your tickets are {TicketRange}, Good Luck!";

    public const string DefaultTellOtherNameMessageTemplate = "{OtherName}'s tickets are {TicketRange}, Good Luck!";

    public const string DefaultRaffleAdMacro =
        "/sh Hello Everyone ♥\n" +
        "/sh We're running a boosted raffle with a current prize of {Pot} Gil\n" +
        "/sh You can buy tickets from me! Price is {PricePerTicket} gil per ticket\n" +
        "/sh Ticket limit per purchase: {MaxTickets}\n" +
        "/sh Results will be shown here at the end of the night and posted on Discord https://discord.gg/urbanclub";

    public const string DefaultWinnerDrawMacro =
        "/sh !\n" +
        "/sh !\n" +
        "/sh !\n" +
        "/sh !\n" +
        "/sh !";

    public const string DefaultWinnerResultMacro =
        "/y  is \"{WinnerName}\" with the ticket number {WinningTicket}!\n" +
        "/y Your Prize is \"{Pot} Gil\"!";

    public int Version { get; set; } = CurrentConfigVersion;

    /// <summary>
    /// Player receiving the next ticket purchase.
    /// </summary>
    public string TargetPlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Price for one raffle ticket.
    /// </summary>
    public long PricePerTicket { get; set; } = 50_000;

    /// <summary>
    /// Starting/boosted pot before ticket sales are added.
    /// </summary>
    public long BasePot { get; set; } = 30_000_000;

    /// <summary>
    /// Percentage of ticket sales kept by the venue/host before calculating the winner prize.
    /// Example: 10 means 10% of ticket sales goes to the venue/host, 90% goes into the prize pot.
    /// </summary>
    public float VenueHostSalesPercent { get; set; } = 0f;

    /// <summary>
    /// Maximum tickets allowed per single purchase.
    /// </summary>
    public int MaxTicketsPerPurchase { get; set; } = 40;



    /// <summary>
    /// Message template sent to the current target for their own tickets.
    /// Variables: {TicketRange}
    /// </summary>
    public string TellTargetMessageTemplate { get; set; } = DefaultTellTargetMessageTemplate;

    /// <summary>
    /// Message template sent to the current target when they bought tickets for another named person.
    /// Variables: {OtherName}, {TicketRange}
    /// </summary>
    public string TellOtherNameMessageTemplate { get; set; } = DefaultTellOtherNameMessageTemplate;

    /// <summary>
    /// Public announcement macro. One chat command per line. The delay field below waits after each line.
    /// </summary>
    public string RaffleAdMacro { get; set; } = DefaultRaffleAdMacro;

    /// <summary>
    /// Delay in seconds after each raffle ad macro line.
    /// </summary>
    public float RaffleAdDelaySeconds { get; set; } = 3f;

    /// <summary>
    /// Winner draw macro queued before listening for the /random result.
    /// Variables: {TotalTickets}
    /// </summary>
    public string WinnerDrawMacro { get; set; } = DefaultWinnerDrawMacro;

    /// <summary>
    /// Delay in seconds after each winner draw macro line.
    /// </summary>
    public float WinnerDrawDelaySeconds { get; set; } = 3f;

    /// <summary>
    /// Winner result macro queued after the plugin detects your /random result.
    /// Variables: {WinnerName}, {WinningTicket}, {Pot}, {TicketRange}
    /// </summary>
    public string WinnerResultMacro { get; set; } = DefaultWinnerResultMacro;

    /// <summary>
    /// Delay in seconds after each winner result macro line.
    /// </summary>
    public float WinnerResultDelaySeconds { get; set; } = 3f;

    /// <summary>
    /// True after old macro wait-only lines have been cleaned once during upgrade.
    /// </summary>
    public bool LegacyManualWaitLinesCleaned { get; set; }

    /// <summary>
    /// Persistent ticket sales ledger.
    /// Each purchase is stored separately, even when the same player buys again.
    /// </summary>
    public List<RaffleSaleRecord> SalesLedger { get; set; } = new();

    /// <summary>
    /// Whether the current raffle session is marked as open.
    /// </summary>
    public long LastStateChangeUtcTicks { get; set; }
    public int LastWinningTicket { get; set; }
    public string LastWinnerName { get; set; } = string.Empty;

    public bool ActiveSessionWasRunning { get; set; }

    /// <summary>
    /// Last visible session notice.
    /// </summary>
    public string ActiveSessionLastNotice { get; set; } = string.Empty;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public bool CleanLegacyManualWaitLinesOnce()
    {
        var changed = false;
        // Upgrade only untouched built-in defaults. Never rewrite a host's customized announcement.
        var formerDefault = DefaultRaffleAdMacro.Replace(
            "/sh Ticket limit per purchase: {MaxTickets}", "/sh {MaxTickets} MAX tickets per person",
            StringComparison.Ordinal);
        if (string.Equals(this.RaffleAdMacro, formerDefault, StringComparison.Ordinal))
        {
            this.RaffleAdMacro = DefaultRaffleAdMacro;
            changed = true;
        }
        var formerDrawDefault = DefaultWinnerDrawMacro + "\n/random {TotalTickets}";
        if (string.Equals(this.WinnerDrawMacro, formerDrawDefault, StringComparison.Ordinal))
        {
            this.WinnerDrawMacro = DefaultWinnerDrawMacro;
            changed = true;
        }
        if (this.LegacyManualWaitLinesCleaned) return changed;
        this.LegacyManualWaitLinesCleaned = true;
        changed = true;

        var raffleAdMacro = this.RaffleAdMacro;
        if (RemoveManualWaitLines(ref raffleAdMacro))
        {
            this.RaffleAdMacro = raffleAdMacro;
            changed = true;
        }

        var winnerDrawMacro = this.WinnerDrawMacro;
        if (RemoveManualWaitLines(ref winnerDrawMacro))
        {
            this.WinnerDrawMacro = winnerDrawMacro;
            changed = true;
        }

        var winnerResultMacro = this.WinnerResultMacro;
        if (RemoveManualWaitLines(ref winnerResultMacro))
        {
            this.WinnerResultMacro = winnerResultMacro;
            changed = true;
        }

        return changed;
    }

    private static bool RemoveManualWaitLines(ref string macroText)
    {
        if (string.IsNullOrWhiteSpace(macroText))
            return false;

        var original = macroText;
        var cleanedLines = new List<string>();

        foreach (var rawLine in macroText.Replace("\r", string.Empty).Split('\n'))
        {
            if (Regex.IsMatch(rawLine.Trim(), @"^<wait\.\d+(?:\.\d+)?>$", RegexOptions.IgnoreCase))
                continue;

            cleanedLines.Add(rawLine);
        }

        macroText = string.Join("\n", cleanedLines);
        return !string.Equals(original, macroText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Restores only the editable chat macro templates to the built-in defaults.
    /// Raffle prices, ticket limits, and ticket entries are left untouched.
    /// </summary>
    public void ResetDefaultMacros()
    {
        this.TellTargetMessageTemplate = DefaultTellTargetMessageTemplate;
        this.TellOtherNameMessageTemplate = DefaultTellOtherNameMessageTemplate;
        this.RaffleAdMacro = DefaultRaffleAdMacro;
        this.RaffleAdDelaySeconds = 3f;
        this.WinnerDrawMacro = DefaultWinnerDrawMacro;
        this.WinnerDrawDelaySeconds = 3f;
        this.WinnerResultMacro = DefaultWinnerResultMacro;
        this.WinnerResultDelaySeconds = 3f;
    }

    /// <summary>Saves the current configuration through Dalamud.</summary>
    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}
