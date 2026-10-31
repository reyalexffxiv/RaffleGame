using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaffleGame.Windows;

/// <summary>Scoped editable-control styling for REY UI/LAYOUT. Never modifies global ImGui state.</summary>
internal static class RaffleGameTheme
{
    private const int Colors = 20;
    private const int Vars = 5;
    public static void Push()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ReyLayout.Ivory);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ReyLayout.Muted);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ReyLayout.Canvas);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ReyLayout.Rail);
        ImGui.PushStyleColor(ImGuiCol.Border, ReyLayout.SoftLine);
        ImGui.PushStyleColor(ImGuiCol.Separator, ReyLayout.SoftLine);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ReyLayout.Raised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.19f, 0.18f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.20f, 0.21f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, ReyLayout.Raised);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.21f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.32f, 0.26f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.24f, 0.20f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ReyLayout.Raised);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.30f, 0.24f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ReyLayout.Rail);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, ReyLayout.Surface);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, ReyLayout.Raised);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, ReyLayout.Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 9f));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
    }

    public static void Pop()
    {
        ImGui.PopStyleVar(Vars);
        ImGui.PopStyleColor(Colors);
    }

    public static void Heading(string title)
    {
        ImGui.TextColored(ReyLayout.Gold, title);
        ImGui.Spacing();
    }
}
