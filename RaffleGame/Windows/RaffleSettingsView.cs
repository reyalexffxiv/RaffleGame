using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaffleGame.Windows;

/// <summary>Setup content hosted inside the main REY shell. No second window, modal, or style stack.</summary>
internal sealed class RaffleSettingsView
{
    private const float MacroEditorHeight = 195f;

    private readonly Plugin plugin;
    private readonly Action requestMacroReset;
    private int section;
    private int macroSection;

    public RaffleSettingsView(Plugin plugin, Action requestMacroReset)
    {
        this.plugin = plugin;
        this.requestMacroReset = requestMacroReset;
    }

    public void Draw()
    {
        RaffleGameTheme.Heading("SETUP");
        if (ReyLayout.Button("##cfgRules", "RULES", new Vector2(120, 32),
            this.section == 0 ? ReyLayout.Gold : ReyLayout.Muted, this.section == 0)) this.section = 0;
        ImGui.SameLine(0, 8);
        if (ReyLayout.Button("##cfgMacros", "MACROS", new Vector2(120, 32),
            this.section == 1 ? ReyLayout.Gold : ReyLayout.Muted, this.section == 1)) this.section = 1;
        ImGui.Dummy(new Vector2(0, 8));
        var locked = !this.plugin.CanOperate || this.plugin.DrawPending ||
            (this.section == 0 && this.plugin.RaffleSession.HasWinner);
        ImGui.BeginDisabled(locked);
        if (this.section == 0) this.DrawRaffleConfig();
        else this.DrawMacrosTab();
        ImGui.EndDisabled();
        if (locked) ImGui.TextColored(ReyLayout.Rose, "Settings locked while drawing, after a winner, or during data recovery.");
    }

    private void DrawRaffleConfig()
    {
        var config = this.plugin.Configuration;

        ImGui.TextColored(ReyLayout.Gold, "PRIZE & PRICING");
        ImGui.Separator();

        this.DrawLongInput(
            "Base Prize Pot",
            config.BasePot,
            value =>
            {
                config.BasePot = Math.Max(0, value);
                config.Save();
            });

        this.DrawLongInput(
            "Price Per Ticket",
            config.PricePerTicket,
            value =>
            {
                config.PricePerTicket = Math.Max(0, value);
                config.Save();
            });

        this.DrawPercentInput(
            "Venue / Host Share (%)",
            config.VenueHostSalesPercent,
            value =>
            {
                config.VenueHostSalesPercent = ClampPercent(value);
                config.Save();
            });


        ImGui.Spacing();
        ImGui.TextColored(ReyLayout.Gold, "TICKET LIMITS");
        ImGui.Separator();

        this.DrawIntInput(
            "Max Tickets Per Purchase",
            config.MaxTicketsPerPurchase,
            value =>
            {
                config.MaxTicketsPerPurchase = Math.Max(0, value);
                config.Save();
            });


    }

    /// <summary>
    /// Draws editable chat templates.
    /// Multiline macros intentionally store only chat commands, while wait fields store timing.
    /// This keeps user macros readable and avoids FFXIV rejecting rapid chat messages.
    /// </summary>
    private void DrawMacrosTab()
    {
        var config = this.plugin.Configuration;
        // One shared navigation row; the reset action remains in a fixed position.
        var rowStart = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        string[] tabs = ["TELLS", "ADVERTISEMENT", "DRAW", "WINNER"];
        for (int i = 0; i < tabs.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 7);
            if (ReyLayout.Button($"##macroGroup{i}", tabs[i], new Vector2(i == 1 ? 147 : 116, 31),
                this.macroSection == i ? ReyLayout.Gold : ReyLayout.Muted,
                this.macroSection == i)) this.macroSection = i;
        }
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + rowWidth - 160f, rowStart.Y));
        if (ReyLayout.Button("##macroDefault", "RESTORE DEFAULTS", new Vector2(160, 32), ReyLayout.Rose))
            this.requestMacroReset();
        ImGui.Dummy(new Vector2(0, 5));
        if (this.macroSection == 0)
        {
            this.DrawSingleLineMacro("Tickets to buyer", config.TellTargetMessageTemplate, value =>
            { config.TellTargetMessageTemplate = value; config.Save(); });
            this.DrawSingleLineMacro("Gift tickets to buyer", config.TellOtherNameMessageTemplate, value =>
            { config.TellOtherNameMessageTemplate = value; config.Save(); });
        }
        else if (this.macroSection == 1)
        {
            this.DrawMacroWaitInput("Raffle ad delay", config.RaffleAdDelaySeconds, value =>
            { config.RaffleAdDelaySeconds = ClampWaitSeconds(value); config.Save(); });
            this.DrawMultilineMacro("Raffle ad", config.RaffleAdMacro, value =>
            { config.RaffleAdMacro = value; config.Save(); });
        }
        else if (this.macroSection == 2)
        {
            this.DrawMacroWaitInput("Countdown delay", config.WinnerDrawDelaySeconds, value =>
            { config.WinnerDrawDelaySeconds = ClampWaitSeconds(value); config.Save(); });
            this.DrawMultilineMacro("Countdown lines", config.WinnerDrawMacro, value =>
            { config.WinnerDrawMacro = value; config.Save(); });
        }
        else
        {
            this.DrawMacroWaitInput("Winner announcement delay", config.WinnerResultDelaySeconds, value =>
            { config.WinnerResultDelaySeconds = ClampWaitSeconds(value); config.Save(); });
            this.DrawMultilineMacro("Winner announcement", config.WinnerResultMacro, value =>
            { config.WinnerResultMacro = value; config.Save(); });
        }
    }

    private void DrawMacroWaitInput(string label, float value, Action<float> onChanged)
    {
        var mutableValue = value;
        var visibleLabel = label;
        var idSuffix = label;
        var hiddenIdIndex = label.IndexOf("##", StringComparison.Ordinal);
        if (hiddenIdIndex >= 0)
        {
            visibleLabel = label[..hiddenIdIndex];
            idSuffix = label[(hiddenIdIndex + 2)..];
        }

        ImGui.SetNextItemWidth(128);
        if (ImGui.InputFloat($"##MacroWait{idSuffix}", ref mutableValue, 0.5f, 1f, "%.1f"))
            onChanged(mutableValue);

        ImGui.SameLine();
        ImGui.TextUnformatted($"{visibleLabel} (seconds)");
    }

    private static float ClampWaitSeconds(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;

        return Math.Clamp(value, 0f, 60f);
    }

    private static float ClampPercent(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;

        return Math.Clamp(value, 0f, 100f);
    }

    private void DrawPercentInput(string label, float value, Action<float> onChanged)
    {
        var mutableValue = value;

        ImGui.SetNextItemWidth(170);
        if (ImGui.InputFloat($"##{label}", ref mutableValue, 0.5f, 5f, "%.1f"))
            onChanged(mutableValue);

        ImGui.SameLine(0, 12);
        ImGui.TextUnformatted(label);
    }

    private void DrawSingleLineMacro(string label, string value, Action<string> onChanged)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1);
        var text = value ?? string.Empty;
        if (ImGui.InputText($"##{label}", ref text, 512))
            onChanged(text);
    }

    private void DrawMultilineMacro(string label, string value, Action<string> onChanged)
    {
        ImGui.SetNextItemWidth(-1);
        var text = value ?? string.Empty;
        if (ImGui.InputTextMultiline($"##{label}", ref text, 8192, new Vector2(-1, MacroEditorHeight)))
            onChanged(text);
    }

    private void DrawLongInput(string label, long value, Action<long> onChanged)
    {
        var text = value.ToString("N0", CultureInfo.InvariantCulture);

        ImGui.SetNextItemWidth(180);
        if (!ImGui.InputText(label, ref text, 32))
            return;

        text = text.Trim();
        if (long.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
            onChanged(parsed);
    }

    private void DrawIntInput(string label, int value, Action<int> onChanged)
    {
        var mutableValue = value;

        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt(label, ref mutableValue))
            onChanged(mutableValue);
    }
}
