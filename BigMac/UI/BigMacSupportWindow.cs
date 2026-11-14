using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ElezenTools.UI;

namespace BigMac.UI;

internal sealed class BigMacSupportWindow : Window
{
    private const float MinimumWidth = 320f;
    private const string SupportCopy = "Thank you for thinking about supporting BigMac!\n\n"
        + "Your support helps keep BigMac and other Elezen created projects going. "
        + "If you'd like to support development monthly, you can join us on Patreon.\n\n"
        + "Alternatively, if you'd like to throw a one-off tip my way to grab dinner, feel free to donate on Ko-fi.\n\n"
        + "Thank you once again!";

    private static float ButtonHeight => 38f * ImGuiHelpers.GlobalScale;

    public BigMacSupportWindow() : base("Support BigMac!###BigMacSupport")
    {
        Flags |= ImGuiWindowFlags.NoDocking;
        Size = new Vector2(380, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var wrapWidth = MathF.Max(1f, MinimumWidth * scale - style.WindowPadding.X * 4);
        var copyHeight = ImGui.CalcTextSize(SupportCopy, false, wrapWidth).Y;
        var minimumHeight = (ImGui.GetFrameHeight() + style.WindowPadding.Y * 4
            + copyHeight + ButtonHeight + style.ItemSpacing.Y * 2) / scale;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(MinimumWidth, MathF.Ceiling(minimumHeight)),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var buttonRowHeight = ButtonHeight + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##support-copy", new Vector2(0, -buttonRowHeight), false,
                ImGuiWindowFlags.AlwaysUseWindowPadding))
            ElezenImgui.WrappedText(SupportCopy);
        ImGui.EndChild();

        var buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;
        if (DrawSupportButton(FontAwesomeIcon.Heart, "Patreon", buttonWidth))
            Util.OpenLink("https://patreon.com/elznmods");
        ImGui.SameLine();
        if (DrawSupportButton(FontAwesomeIcon.ExternalLinkAlt, "Ko-fi", buttonWidth))
            Util.OpenLink("https://ko-fi.com/eauldane");
    }

    private static bool DrawSupportButton(FontAwesomeIcon icon, string label, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, ButtonHeight);
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##support-{label}", size);
        var hovered = ImGui.IsItemHovered();
        var palette = BigMacTheme.Palette;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, min + size,
            Colour.Vector4ToColour(hovered ? palette.ButtonHovered : palette.CompactPanelAlt), 3f * scale);
        drawList.AddRect(min, min + size, Colour.Vector4ToColour(palette.Accent), 3f * scale);

        var iconText = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(iconText);
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(label);
        var contentWidth = iconSize.X + 10f * scale + textSize.X;
        var iconPos = min + new Vector2((size.X - contentWidth) / 2, (size.Y - iconSize.Y) / 2);
        ImGui.PushFont(UiBuilder.IconFont);
        drawList.AddText(iconPos, Colour.Vector4ToColour(palette.Accent), iconText);
        ImGui.PopFont();
        drawList.AddText(new Vector2(iconPos.X + iconSize.X + 10f * scale, min.Y + (size.Y - textSize.Y) / 2),
            Colour.Vector4ToColour(BigMacTheme.Text), label);
        return clicked;
    }
}
