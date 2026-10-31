using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RaffleGame.Models;

namespace RaffleGame.Windows;

/// <summary>REY UI/LAYOUT port: geometry, animation and cards from VenueHost 1.0b / BarGame 1.0b.</summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private enum Page { Raffle, Tickets, Setup }
    private enum Confirmation { None, Draw, Reset, Undo, Clear, Delete, ResetMacros }

    private readonly Plugin plugin;
    private readonly RaffleSettingsView settings;
    private Page page;
    private Confirmation confirmation;
    private RaffleSaleRecord? pendingSale;
    private bool confirmationFocusRequested;
    private int ticketsToAdd = 1;
    private string giftRecipientName = string.Empty;
    private string exportStatus = string.Empty;
    private string lastExportDirectory = string.Empty;

    private const float HeaderHeight = 68f;
    private const float RailWidth = 72f;
    private const float CompactHeight = 60f;
    private const float CompactWidth = 590f;
    private const float CompactDurationMs = 240f;
    // Removing the footer also removes its height; workspace retains its usable area.
    private static readonly Vector2 ExpandedSize = new(990f, 514f);
    private const ImGuiWindowFlags ShellFlags = ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoResize;

    private bool compactRequested;
    private float compactAmount;
    private float transitionFrom;
    private long transitionAt;
    private float animatedRailY = -1f;
    private Vector2 dragDelta;
    private Vector2 lastWindowPosition;
    private Vector2 lastWindowSize;

    public MainWindow(Plugin plugin) : base("RaffleGame | REY###RaffleGameMain", ShellFlags)
    {
        this.plugin = plugin;
        this.settings = new RaffleSettingsView(plugin, () => this.RequestConfirmation(Confirmation.ResetMacros));
        this.Size = ExpandedSize;
        this.SizeCondition = ImGuiCond.Always;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = ExpandedSize, MaximumSize = ExpandedSize };
        this.AllowPinning = false;
        this.AllowClickthrough = false;
    }

    public void Dispose() { }
    public void RequestResetConfirmation()
    {
        this.EnsureExpanded();
        this.IsOpen = true;
        this.RequestConfirmation(Confirmation.Reset);
    }
    public void OpenSetup()
    {
        this.EnsureExpanded();
        this.page = Page.Setup;
        this.IsOpen = true;
    }
    public void OpenRaffle()
    {
        this.EnsureExpanded();
        this.page = Page.Raffle;
        this.IsOpen = true;
    }
    private void EnsureExpanded()
    {
        if (this.compactRequested) this.ToggleCompactMode();
    }

    private void ToggleCompactMode()
    {
        this.compactRequested = !this.compactRequested;
        this.transitionFrom = this.compactAmount;
        this.transitionAt = Environment.TickCount64;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, ReyLayout.WindowCornerRadius);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ReyLayout.Canvas);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Vector4.Zero);
        var target = this.compactRequested ? 1f : 0f;
        if (Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion) this.compactAmount = target;
        else
        {
            var range = MathF.Abs(target - this.transitionFrom);
            var duration = CompactDurationMs * range;
            var t = duration <= 0.01f ? 1f : Math.Clamp((Environment.TickCount64 - this.transitionAt) / duration, 0f, 1f);
            var eased = t < .5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * .5f;
            this.compactAmount = this.transitionFrom + (target - this.transitionFrom) * eased;
        }
        var driven = this.compactRequested || this.compactAmount > .001f;
        this.Flags = ShellFlags;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = driven ? new Vector2(CompactWidth, CompactHeight) : ExpandedSize,
            MaximumSize = driven ? new Vector2(float.MaxValue, float.MaxValue) : ExpandedSize
        };
        var summary = $"{this.plugin.RaffleSession.TotalTickets} / 999 · {ShortGil(this.plugin.RaffleSession.TotalPot)} pot";
        var statusWidth = this.Status.Length > 0 ? ImGui.CalcTextSize(this.Status).X + 30f : 0f;
        var summaryEnd = CompactStatusX() + statusWidth + ImGui.CalcTextSize(summary).X;
        var compactWidth = MathF.Max(CompactWidth, summaryEnd + 18f + 92f + 16f + 86f);
        this.Size = Vector2.Lerp(ExpandedSize, new Vector2(compactWidth, CompactHeight), this.compactAmount);
        this.SizeCondition = ImGuiCond.Always;
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(4);
    }

    public override void Draw()
    {
        RaffleGameTheme.Push();
        try
        {
            this.dragDelta = Vector2.Zero;
            this.lastWindowPosition = ImGui.GetWindowPos();
            this.lastWindowSize = ImGui.GetWindowSize();
            ImGui.BeginDisabled(this.confirmation != Confirmation.None);
            if (this.compactAmount > .001f) this.DrawCompactBar();
            else this.DrawExpanded();
            ImGui.EndDisabled();
            this.DrawConfirmationWindow();
        }
        finally { RaffleGameTheme.Pop(); }
    }

    // Only exceptional draw states earn a status label; selling tickets is the default.
    private string Status => this.plugin.DrawPending ? "DRAWING" :
        this.plugin.RaffleSession.HasWinner ? "WINNER" : string.Empty;
    private Vector4 StatusColor => this.plugin.DrawPending ? ReyLayout.Rose : ReyLayout.Gold;

    private void DrawExpanded()
    {
        var p = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        ReyLayout.Fill(ImGui.GetWindowDrawList(), p, size, ReyLayout.Canvas, ReyLayout.WindowCornerRadius);
        ReyLayout.Ambient(p, size);
        this.DrawHeader(p, size.X);
        this.DrawRail(p, size.Y);
        ImGui.SetCursorScreenPos(p + new Vector2(RailWidth, HeaderHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 18f));
        var flags = ImGuiWindowFlags.AlwaysUseWindowPadding;
        if (this.page == Page.Tickets) flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (ImGui.BeginChild("##raffleWorkspace",
            new Vector2(size.X - RailWidth, size.Y - HeaderHeight), false, flags))
        {
            if (this.page == Page.Raffle) this.DrawRafflePage();
            else if (this.page == Page.Tickets) this.DrawTicketsPage();
            else this.settings.Draw();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        if (this.dragDelta != Vector2.Zero) ImGui.SetWindowPos(p + this.dragDelta, ImGuiCond.Always);
    }

    private void DrawHeader(Vector2 p, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        ReyLayout.Fill(dl, p, new Vector2(width, HeaderHeight), ReyLayout.Rail, ReyLayout.WindowCornerRadius);
        ReyLayout.Fill(dl, p + new Vector2(0, 12), new Vector2(width, HeaderHeight - 12), ReyLayout.Rail);
        ReyLayout.Rule(dl, p + new Vector2(0, 67), p + new Vector2(width, 67), ReyLayout.SoftLine);
        var mascot = this.plugin.MascotTexture?.GetWrapOrDefault();
        if (mascot is not null) dl.AddImage(mascot.Handle, p + new Vector2(19, 17), p + new Vector2(53, 51));
        else dl.AddText(p + new Vector2(28, 25), ReyLayout.Color(ReyLayout.Gold), "R");
        dl.AddText(p + new Vector2(67, 25), ReyLayout.Color(ReyLayout.Ivory), "RAFFLEGAME");
        if (this.Status.Length > 0)
        {
            ReyLayout.StatusDot(p + new Vector2(width - 267, 34), this.StatusColor, 3.5f);
            dl.AddText(p + new Vector2(width - 254, 25), ReyLayout.Color(this.StatusColor), this.Status);
        }
        if (ReyLayout.FlatButton("##rgSetup", this.page == Page.Setup ? "GAME" : "SETUP", p + new Vector2(width - 177, 18),
            new Vector2(74, 32), ReyLayout.Gold, this.page == Page.Setup))
        {
            this.page = this.page == Page.Setup ? Page.Raffle : Page.Setup;
        }
        if (ReyLayout.ChevronButton("##rgCollapse", p + new Vector2(width - 91, 18),
            new Vector2(34, 32), ReyLayout.Gold, up: false)) this.ToggleCompactMode();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Compact (or double-click header)");
        if (ReyLayout.FlatButton("##rgClose", "X", p + new Vector2(width - 47, 18),
            new Vector2(30, 32), ReyLayout.Rose)) this.IsOpen = false;
        ImGui.SetCursorScreenPos(p + new Vector2(186, 0));
        ImGui.InvisibleButton("##rgHeaderDrag", new Vector2(MathF.Max(10, width - 486), 67));
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) this.ToggleCompactMode();
        if (ImGui.IsItemActive()) this.dragDelta = ImGui.GetIO().MouseDelta;
    }

    private void DrawRail(Vector2 p, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p + new Vector2(0, HeaderHeight),
            p + new Vector2(RailWidth, height), ReyLayout.Color(ReyLayout.Rail),
            ReyLayout.WindowCornerRadius, ImDrawFlags.RoundCornersBottomLeft);
        ReyLayout.Rule(dl, p + new Vector2(RailWidth - 1, HeaderHeight),
            p + new Vector2(RailWidth - 1, height), ReyLayout.SoftLine);
        this.DrawRailItem(p, Page.Raffle, 142, "Raffle");
        this.DrawRailItem(p, Page.Tickets, 194, "Sales History");
        var target = this.page == Page.Raffle ? 152f : this.page == Page.Tickets ? 204f : -1f;
        if (target > 0)
        {
            if (this.animatedRailY < 0 || Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion)
                this.animatedRailY = target;
            else this.animatedRailY += (target - this.animatedRailY) *
                (1f - MathF.Exp(-15f * MathF.Min(ImGui.GetIO().DeltaTime, .05f)));
            ReyLayout.Fill(dl, p + new Vector2(10, this.animatedRailY), new Vector2(3, 23), ReyLayout.Gold, 1.5f);
        }
    }

    private void DrawRailItem(Vector2 p, Page item, float y, string label)
    {
        var pos = p + new Vector2(10, y);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton($"##rgRail{item}", new Vector2(52, 43));
        var hover = ImGui.IsItemHovered();
        var selected = this.page == item;
        var dl = ImGui.GetWindowDrawList();
        if (selected || hover) ReyLayout.Fill(dl, pos, new Vector2(52, 43),
            selected ? new Vector4(.25f, .205f, .195f, 1) : ReyLayout.Raised, 9);
        var ink = ReyLayout.Color(selected ? ReyLayout.Gold : hover ? ReyLayout.Ivory : ReyLayout.Muted);
        if (item == Page.Raffle)
        {
            dl.AddRect(pos + new Vector2(17, 12), pos + new Vector2(35, 30), ink, 4, ImDrawFlags.None, 1.5f);
            dl.AddCircleFilled(pos + new Vector2(23, 18), 1.5f, ink);
            dl.AddCircleFilled(pos + new Vector2(29, 24), 1.5f, ink);
        }
        else for (var i = 0; i < 3; i++)
            dl.AddLine(pos + new Vector2(17, 14 + i * 7), pos + new Vector2(35, 14 + i * 7), ink, 1.7f);
        if (hover) ImGui.SetTooltip(label);
        if (clicked) this.page = item;
    }

    private static float CompactStatusX() => 62f + ImGui.CalcTextSize("RAFFLEGAME").X + 23f;
    private void DrawCompactBar()
    {
        var p = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var center = size.Y * .5f;
        var restorePos = p + new Vector2(size.X - 86, center - 16);
        var closePos = p + new Vector2(size.X - 45, center - 16);
        var status = this.Status;
        var summary = $"{this.plugin.RaffleSession.TotalTickets} / 999 · {ShortGil(this.plugin.RaffleSession.TotalPot)} pot";
        var summaryX = CompactStatusX() + (status.Length > 0 ? ImGui.CalcTextSize(status).X + 30f : 0f);
        // Keep SELL MAX next to restore/close, not next to the changing ticket summary.
        // The compact width calculation above guarantees room after the summary.
        var salePos = restorePos - new Vector2(104f, 0f);
        var canSell = this.plugin.CanQuickSellMax(out _, out _);

        ImGui.SetCursorScreenPos(p);
        ImGui.InvisibleButton("##rgCompactDrag", new Vector2(MathF.Max(1f, salePos.X - p.X - 6f), size.Y));
        var restore = ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        var move = ImGui.IsItemActive() ? ImGui.GetIO().MouseDelta : Vector2.Zero;
        ImGui.SetCursorScreenPos(salePos);
        var sell = ImGui.InvisibleButton("##rgCompactSellMax", new Vector2(92f, 32f));
        var sellHover = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(restorePos);
        restore |= ImGui.InvisibleButton("##rgRestore", new Vector2(34, 32));
        var restoreHover = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(closePos);
        var close = ImGui.InvisibleButton("##rgCompactClose", new Vector2(30, 32));
        var closeHover = ImGui.IsItemHovered();

        // Match BarGame: draw foreground chrome with native hitboxes beneath it.
        // Do not use native hover tooltips here (they draw behind foreground).
        var dl = ImGui.GetForegroundDrawList();
        dl.PushClipRect(p, p + size, true);
        ReyLayout.Fill(dl, p, size, ReyLayout.Rail, ReyLayout.WindowCornerRadius);
        var mascot = this.plugin.MascotTexture?.GetWrapOrDefault();
        var mx = 19f + (17f - 19f) * this.compactAmount;
        var my = 17f + (14f - 17f) * this.compactAmount;
        var ms = 34f + (32f - 34f) * this.compactAmount;
        if (mascot is not null) dl.AddImage(mascot.Handle, p + new Vector2(mx, my), p + new Vector2(mx + ms, my + ms));
        else dl.AddText(p + new Vector2(mx + 9, my + 8), ReyLayout.Color(ReyLayout.Gold), "R");
        var titleX = 67f + (62f - 67f) * this.compactAmount;
        var titleY = 25f + (21f - 25f) * this.compactAmount;
        dl.AddText(p + new Vector2(titleX, titleY), ReyLayout.Color(ReyLayout.Ivory), "RAFFLEGAME");
        if (status.Length > 0)
        {
            ReyLayout.StatusDot(p + new Vector2(CompactStatusX(), center), this.StatusColor, 3.5f);
            dl.AddText(p + new Vector2(CompactStatusX() + 10, center - 9), ReyLayout.Color(this.StatusColor), status);
        }
        dl.AddText(p + new Vector2(summaryX, center - 9), ReyLayout.Color(ReyLayout.Gold), summary);
        ReyLayout.Fill(dl, salePos, new Vector2(92, 32),
            canSell ? (sellHover ? ReyLayout.Raised : new Vector4(.31f, .23f, .14f, 1f)) : ReyLayout.Surface, 8);
        ReyLayout.Frame(dl, salePos, new Vector2(92, 32), canSell ? ReyLayout.Gold : ReyLayout.SoftLine, 8);
        var sellLabel = "SELL MAX";
        var textSize = ImGui.CalcTextSize(sellLabel);
        dl.AddText(salePos + new Vector2((92 - textSize.X) / 2f, (32 - textSize.Y) / 2f),
            ReyLayout.Color(canSell ? ReyLayout.Ivory : ReyLayout.Dim), sellLabel);
        ReyLayout.Fill(dl, restorePos, new Vector2(34, 32), restoreHover ? ReyLayout.Raised : ReyLayout.Surface, 8);
        ReyLayout.Frame(dl, restorePos, new Vector2(34, 32), ReyLayout.SoftLine, 8);
        var cc = restorePos + new Vector2(17, 16);
        dl.AddLine(cc + new Vector2(-5, 2), cc + new Vector2(0, -3), ReyLayout.Color(ReyLayout.Gold), 1.7f);
        dl.AddLine(cc + new Vector2(0, -3), cc + new Vector2(5, 2), ReyLayout.Color(ReyLayout.Gold), 1.7f);
        ReyLayout.Fill(dl, closePos, new Vector2(30, 32), closeHover ? ReyLayout.Raised : ReyLayout.Surface, 8);
        ReyLayout.Frame(dl, closePos, new Vector2(30, 32), ReyLayout.SoftLine, 8);
        dl.AddText(closePos + new Vector2(10, 8), ReyLayout.Color(ReyLayout.Rose), "X");
        dl.PopClipRect();
        if (move != Vector2.Zero) ImGui.SetWindowPos(p + move, ImGuiCond.Always);
        if (restore) this.ToggleCompactMode();
        if (close) this.IsOpen = false;
        if (sell) this.plugin.QuickSellMax();
    }

    private static string ShortGil(long value) => value >= 1_000_000
        ? (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : value >= 1_000 ? (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K"
        : value.ToString(CultureInfo.InvariantCulture);

    private void Metric(string heading, string value, Vector4 color, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        ReyLayout.SurfacePanel(pos, new Vector2(width, 80));
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(pos + new Vector2(15, 13), ReyLayout.Color(ReyLayout.Muted), heading);
        dl.PushClipRect(pos + new Vector2(12, 34), pos + new Vector2(width - 9, 76), true);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.24f, pos + new Vector2(15, 42),
            ReyLayout.Color(color), value);
        dl.PopClipRect();
        ImGui.Dummy(new Vector2(width, 80));
    }
}
