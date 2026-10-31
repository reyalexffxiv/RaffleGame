using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace RaffleGame.Services;

/// <summary>Single FIFO for outgoing game commands. All calls execute on the framework update thread.</summary>
public sealed class GameChatService
{
    private readonly IPluginLog log;
    private readonly Queue<QueuedCommand> pending = new();
    private DateTime nextAllowed = DateTime.MinValue;
    public bool HasPendingTicketTells
    {
        get
        {
            foreach (var item in this.pending)
                if (item.IsTicketTell) return true;
            return false;
        }
    }

    public GameChatService(IPluginLog log) => this.log = log;

    public bool QueueTellCurrentTarget(string message, Func<bool> stillSameTarget, Action? onCancelled = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return this.Enqueue("/tell <t> " + SingleLine(message), TimeSpan.Zero, null,
            onCancelled, stillSameTarget, priority: true, ticketTell: true);
    }

    public void QueueMacroCommands(string? text, TimeSpan delay)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 0, 60));
        string? pendingLine = null;
        var after = delay;
        foreach (var rawLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var wait = Regex.Match(line, @"^<wait\.(?<seconds>\d+(?:\.\d+)?)>$", RegexOptions.IgnoreCase);
            if (wait.Success && double.TryParse(wait.Groups["seconds"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                if (pendingLine is not null)
                    after = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 60));
                continue;
            }
            if (pendingLine is not null) this.Enqueue(pendingLine, after);
            pendingLine = line;
            after = delay;
        }
        if (pendingLine is not null) this.Enqueue(pendingLine, after);
    }

    public bool QueueCommandWithCallbacks(string command, TimeSpan delay,
        Action beforeSend, Action failed) => this.Enqueue(command, delay, beforeSend, failed);

    private bool Enqueue(string command, TimeSpan delay, Action? before = null,
        Action? failed = null, Func<bool>? canSend = null, bool priority = false, bool ticketTell = false)
    {
        var cleaned = SingleLine(command);
        if (cleaned.Length == 0 || !cleaned.StartsWith('/') || cleaned.Length > 500)
        {
            if (!ticketTell) failed?.Invoke();
            return false;
        }
        if (this.pending.Count >= 200)
        {
            if (!ticketTell) failed?.Invoke();
            return false;
        }
        var item = new QueuedCommand(cleaned, delay, before, failed, canSend, ticketTell);
        if (priority && this.pending.Count > 0)
        {
            // Ticket confirms take the next available chat slot rather than
            // waiting behind a multi-line raffle ad. Preserve macro order.
            var rest = this.pending.ToArray();
            this.pending.Clear();
            this.pending.Enqueue(item);
            foreach (var queued in rest) this.pending.Enqueue(queued);
        }
        else this.pending.Enqueue(item);
        return true;
    }

    public void ClearPendingCommands()
    {
        while (this.pending.Count > 0)
        {
            var item = this.pending.Dequeue();
            if (item.IsTicketTell) item.Failed?.Invoke();
        }
        // Retain a short minimum gap after cancellation; do not burst on the next tick.
        this.nextAllowed = DateTime.Now.AddSeconds(1);
    }

    public void FlushOnePendingCommand()
    {
        if (this.pending.Count == 0 || DateTime.Now < this.nextAllowed) return;
        var item = this.pending.Dequeue();
        if (item.CanSend is not null && !item.CanSend())
        {
            item.Failed?.Invoke();
            return;
        }
        item.Before?.Invoke();
        var sent = this.TrySend(item.Command);
        if (!sent) item.Failed?.Invoke();
        this.nextAllowed = DateTime.Now + (sent ? item.DelayAfter : TimeSpan.FromSeconds(1));
    }

    private unsafe bool TrySend(string command)
    {
        try
        {
            var shell = RaptureShellModule.Instance();
            var ui = UIModule.Instance();
            if (shell is null || ui is null) return false;
            using var text = new Utf8String(command);
            if (text.Length is <= 0 or > 500) return false;
            shell->ExecuteCommandInner(&text, ui);
            return true;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Native game chat dispatch failed.");
            return false;
        }
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private sealed record QueuedCommand(string Command, TimeSpan DelayAfter,
        Action? Before, Action? Failed, Func<bool>? CanSend, bool IsTicketTell);
}
