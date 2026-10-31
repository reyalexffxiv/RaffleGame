using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaffleGame.Windows;

/// <summary>
/// REY UI/LAYOUT, ported from the VenueHost 1.0b reference implementation. Draws only within the
/// current Dalamud/ImGui window; it does not install global styles or textures.
/// All rendering is immediate-mode and produces no persistent GPU allocations.
/// </summary>
internal static class ReyLayout
{
    public const float WindowCornerRadius = 14f;
    public static Vector4 Canvas => new(0.055f, 0.063f, 0.077f, 1f);
    public static Vector4 Rail => new(0.076f, 0.083f, 0.095f, 1f);
    public static Vector4 Surface => new(0.105f, 0.107f, 0.123f, 1f);
    public static Vector4 Raised => new(0.135f, 0.132f, 0.148f, 1f);
    public static Vector4 Line => new(0.24f, 0.23f, 0.255f, 1f);
    public static Vector4 SoftLine => new(0.21f, 0.21f, 0.23f, 0.70f);
    public static Vector4 Ivory => new(0.963f, 0.945f, 0.912f, 1f);
    public static Vector4 Muted => new(0.69f, 0.69f, 0.72f, 1f);
    public static Vector4 Dim => new(0.50f, 0.51f, 0.55f, 1f);
    public static Vector4 Gold => new(0.94f, 0.735f, 0.47f, 1f);
    public static Vector4 Rose => new(0.94f, 0.60f, 0.67f, 1f);
    public static Vector4 Mint => new(0.45f, 0.84f, 0.72f, 1f);
    public static Vector4 Mist => new(0.55f, 0.72f, 0.81f, 1f);

    public static uint Color(Vector4 value) => ImGui.ColorConvertFloat4ToU32(value);

    public static void Fill(ImDrawListPtr draw, Vector2 p, Vector2 size, Vector4 color, float round = 0f)
        => draw.AddRectFilled(p, p + size, Color(color), round);

    public static void Frame(ImDrawListPtr draw, Vector2 p, Vector2 size, Vector4 stroke, float round = 0f)
        => draw.AddRect(p, p + size, Color(stroke), round, ImDrawFlags.None, 1f);

    public static void Rule(ImDrawListPtr draw, Vector2 a, Vector2 b, Vector4 color)
        => draw.AddLine(a, b, Color(color));

    public static void Ambient(Vector2 p, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        // Draw ambient light over a single opaque, rounded REY shell.
        ImGui.PushClipRect(p, p + size, true);
        // Original VenueHost visual language: dim theater spotlights and
        // brass architectural linework, rather than a combat-tech dashboard.
        var a = p + new Vector2(size.X * 0.86f, size.Y * 0.24f);
        var b = p + new Vector2(size.X * 0.23f, size.Y * 0.93f);
        draw.AddCircleFilled(a, 300f, Color(new Vector4(0.45f, 0.25f, 0.22f, 0.055f)), 72);
        draw.AddCircleFilled(a, 214f, Color(new Vector4(0.57f, 0.34f, 0.22f, 0.043f)), 72);
        draw.AddCircleFilled(a, 118f, Color(new Vector4(0.63f, 0.41f, 0.26f, 0.045f)), 72);
        draw.AddCircleFilled(b, 260f, Color(new Vector4(0.26f, 0.39f, 0.42f, 0.035f)), 72);
        draw.AddCircleFilled(b, 145f, Color(new Vector4(0.25f, 0.47f, 0.44f, 0.03f)), 72);
        for (var i = 1; i <= 5; i++)
        {
            var x = p.X + i * size.X / 6f;
            Rule(draw, new Vector2(x, p.Y), new Vector2(x, p.Y + size.Y),
                new Vector4(0.56f, 0.43f, 0.37f, 0.022f));
        }
        ImGui.PopClipRect();
    }

    // VenueHost's owner-relative floating window placement, kept reusable.
    public static Vector2 CenteredWindowPosition(Vector2 ownerPosition, Vector2 ownerSize, Vector2 popupSize)
    {
        var requested = ownerPosition + (ownerSize - popupSize) * 0.5f;
        var viewport = ImGui.GetMainViewport();
        var min = viewport.WorkPos;
        var max = viewport.WorkPos + viewport.WorkSize - popupSize;
        return new Vector2(Math.Clamp(requested.X, min.X, MathF.Max(min.X, max.X)),
            Math.Clamp(requested.Y, min.Y, MathF.Max(min.Y, max.Y)));
    }

    public static void SurfacePanel(Vector2 p, Vector2 size, Vector4? fill = null, float rounding = 12f)
    {
        var draw = ImGui.GetWindowDrawList();
        Fill(draw, p, size, fill ?? Surface, rounding);
        Frame(draw, p, size, SoftLine, rounding);
        Rule(draw, p + new Vector2(rounding, 1f),
            p + new Vector2(size.X - rounding, 1f),
            new Vector4(1f, 0.84f, 0.73f, 0.05f));
    }

    public static void StatusDot(Vector2 center, Vector4 color, float radius = 4f)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddCircleFilled(center, radius + 3f, Color(color with { W = 0.085f }), 20);
        draw.AddCircleFilled(center, radius, Color(color), 20);
    }

    public static bool FlatButton(string id, string label, Vector2 p, Vector2 size,
        Vector4 accent, bool highlighted = false)
    {
        ImGui.SetCursorScreenPos(p);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = highlighted ? new Vector4(accent.X * 0.27f, accent.Y * 0.27f, accent.Z * 0.27f, 1f)
            : hovered ? Raised : Surface;
        var draw = ImGui.GetWindowDrawList();
        Fill(draw, p, size, fill, 8f);
        Frame(draw, p, size, highlighted ? accent with { W = 0.72f } : SoftLine, 8f);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(p + new Vector2(MathF.Max(4f, (size.X - textSize.X) * 0.5f),
            (size.Y - textSize.Y) * 0.5f), Color(highlighted ? Ivory : (hovered ? Ivory : Muted)), label);
        return clicked;
    }

    public static bool ChevronButton(string id, Vector2 p, Vector2 size, Vector4 accent, bool up)
    {
        var clicked = FlatButton(id, string.Empty, p, size, accent);
        var middle = p + size * 0.5f;
        var y = up ? 2f : -2f;
        var a = new Vector2(middle.X - 5f, middle.Y + y);
        var b = new Vector2(middle.X, middle.Y - y);
        var c = new Vector2(middle.X + 5f, middle.Y + y);
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(a, b, Color(accent), 1.7f);
        dl.AddLine(b, c, Color(accent), 1.7f);
        return clicked;
    }

    public static void LabelAt(Vector2 p, string label, Vector4 color)
    {
        ImGui.SetCursorScreenPos(p);
        ImGui.TextColored(color, label);
    }

    public static bool Button(string id, string label, Vector2 size, Vector4 accent, bool highlighted = false)
        => FlatButton(id, label, ImGui.GetCursorScreenPos(), size, accent, highlighted);

    public static bool BeginPanel(string id, Vector2 size, float padding = 16f,
        ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        var p = ImGui.GetCursorScreenPos();
        SurfacePanel(p, size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding));
        var result = ImGui.BeginChild(id, size, false, ImGuiWindowFlags.AlwaysUseWindowPadding | flags);
        ImGui.PopStyleVar();
        return result;
    }

    public static void EndPanel() => ImGui.EndChild();
}
