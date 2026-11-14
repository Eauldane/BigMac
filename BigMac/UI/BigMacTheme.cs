using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ElezenTools.UI;

namespace BigMac.UI;

internal static class BigMacTheme
{
    public static readonly Vector4 Text = Colour.HexToVector4("#F3EBDD");
    public static readonly ModernPalette Palette = new(
        Accent: Colour.HexToVector4("#E8AD52"),
        BooleanTrue: Colour.HexToVector4("#9BCB94"),
        BooleanFalse: Colour.HexToVector4("#ED9288"),
        CompactBg: Colour.HexToVector4("#201E1B"),
        CompactPanel: Colour.HexToVector4("#292621"),
        CompactPanelAlt: Colour.HexToVector4("#363028"),
        CompactBorder: Colour.HexToVector4("#746047"),
        CompactBorderSubtle: Colour.HexToVector4("#514638"),
        CompactTextMuted: Colour.HexToVector4("#BCAF9D"),
        CompactOffline: Colour.HexToVector4("#A99D8D"),
        TitleBg: Colour.HexToVector4("#191715"),
        TitleBgActive: Colour.HexToVector4("#30271C"),
        TitleBgCollapsed: Colour.HexToVector4("#191715"),
        Button: Colour.HexToVector4("#584126"),
        ButtonHovered: Colour.HexToVector4("#755532"),
        ButtonActive: Colour.HexToVector4("#89643A"),
        TabActive: Colour.HexToVector4("#755532"),
        TabHovered: Colour.HexToVector4("#89643A"));

    public static IDisposable Push()
        => new ThemeScope();

    private sealed class ThemeScope : IDisposable
    {
        private readonly IDisposable _theme;
        private readonly IDisposable _content;
        private readonly IDisposable _colours;
        private readonly IDisposable _spacing;

        public ThemeScope()
        {
            _theme = ModernTheme.Push(Palette);
            ModernWindowStyle.PushTitleBar();
            _content = ModernWindowStyle.PushContentPalette();
            var scale = ImGuiHelpers.GlobalScale;
            _spacing = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * scale)
                .Push(ImGuiStyleVar.FramePadding, new Vector2(8f, 5f) * scale);
            // Override the blues from ElezenTools, should probably move those out in api16
            _colours = ImRaii.PushColor(ImGuiCol.Text, Text)
                .Push(ImGuiCol.TextDisabled, Palette.CompactTextMuted)
                .Push(ImGuiCol.ChildBg, Palette.CompactPanel)
                .Push(ImGuiCol.PopupBg, Palette.CompactPanel)
                .Push(ImGuiCol.FrameBg, Palette.CompactBg)
                .Push(ImGuiCol.FrameBgHovered, Palette.CompactPanelAlt)
                .Push(ImGuiCol.FrameBgActive, Palette.CompactPanelAlt)
                .Push(ImGuiCol.Header, Palette.Button)
                .Push(ImGuiCol.HeaderHovered, Palette.ButtonHovered)
                .Push(ImGuiCol.HeaderActive, Palette.ButtonActive)
                .Push(ImGuiCol.Separator, Palette.CompactBorderSubtle)
                .Push(ImGuiCol.SeparatorHovered, Palette.Accent)
                .Push(ImGuiCol.SeparatorActive, Palette.Accent)
                .Push(ImGuiCol.ResizeGrip, Colour.WithAlpha(Palette.Accent, 0.25f))
                .Push(ImGuiCol.ResizeGripHovered, Colour.WithAlpha(Palette.Accent, 0.65f))
                .Push(ImGuiCol.ResizeGripActive, Palette.Accent)
                .Push(ImGuiCol.TextSelectedBg, Colour.WithAlpha(Palette.Accent, 0.30f))
                .Push(ImGuiCol.ScrollbarBg, Palette.CompactBg)
                .Push(ImGuiCol.ScrollbarGrab, Palette.CompactBorder)
                .Push(ImGuiCol.ScrollbarGrabHovered, Palette.ButtonHovered)
                .Push(ImGuiCol.ScrollbarGrabActive, Palette.Accent)
                .Push(ImGuiCol.NavHighlight, Palette.Accent);
        }

        public void Dispose()
        {
            _colours.Dispose();
            _spacing.Dispose();
            _content.Dispose();
            ModernWindowStyle.PopTitleBar();
            _theme.Dispose();
        }
    }
}
