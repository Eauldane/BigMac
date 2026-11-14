using System;
using System.Collections.Generic;
using BigMac.Scripting;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Numerics;
using ElezenTools.UI;

namespace BigMac.UI;

public sealed class BigMacHelpWindow : Window
{
    private int _selectedSectionIndex;

    public BigMacHelpWindow()
        : base("BigMac Help")
    {
        ElezenFonts.Get(24);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 480),
            MaximumSize = new Vector2(1800, 1400),
        };
    }

    public override void Draw()
    {
        var sections = ScriptHelpReference.Sections;
        if (sections.Count == 0)
        {
            ImGui.TextDisabled("No help topics available.");
            return;
        }

        _selectedSectionIndex = Math.Clamp(_selectedSectionIndex, 0, sections.Count - 1);

        var sidebarWidth = 240f * ImGuiHelpers.GlobalScale;
        ImGui.BeginChild("##bigmac-help-sidebar", new Vector2(sidebarWidth, 0), true);
        DrawSidebar(sections);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##bigmac-help-detail", new Vector2(0, 0), true);
        DrawSection(_selectedSectionIndex, sections[_selectedSectionIndex]);
        ImGui.EndChild();
    }

    private void DrawSidebar(IReadOnlyList<ScriptHelpSection> sections)
    {
        ModernSection.Header(FontAwesomeIcon.Book, "Scripting guide");
        ModernSidebar.DrawSeparator();

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            ImGui.PushID(index);
            if (ModernSidebar.DrawRow(FontAwesomeIcon.Book, section.Title, _selectedSectionIndex == index))
            {
                _selectedSectionIndex = index;
            }
            ImGui.PopID();
        }
    }

    private void DrawSection(int sectionIndex, ScriptHelpSection section)
    {
        ImGui.TextDisabled("Reference");
        using (ElezenFonts.Push(24))
        {
            ElezenImgui.ColouredText(section.Title, BigMacTheme.Palette.Accent);
        }
        ModernSection.SoftSeparator();
        ImGui.Spacing();
        ElezenImgui.WrappedText(section.Summary);
        ImGui.Spacing();

        for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
        {
            DrawEntry(sectionIndex, entryIndex, section.Entries[entryIndex]);
            if (entryIndex < section.Entries.Count - 1)
            {
                ModernSection.SoftSeparator();
                ImGui.Spacing();
            }
        }

        ImGui.Spacing();
    }

    private void DrawEntry(int sectionIndex, int entryIndex, ScriptHelpEntry entry)
    {
        ImGui.PushID($"{sectionIndex}-{entryIndex}");
        ModernSection.Header(FontAwesomeIcon.Code, entry.Name);
        ElezenImgui.WrappedText(entry.Description);

        if (!string.IsNullOrWhiteSpace(entry.Syntax))
        {
            ImGui.Spacing();
            DrawCodeBlock("Syntax", entry.Syntax);
        }

        if (!string.IsNullOrWhiteSpace(entry.Example))
        {
            ImGui.Spacing();
            DrawCodeBlock("Example", entry.Example);
        }

        ImGui.PopID();
    }

    private static void DrawCodeBlock(string label, string text)
    {
        ImGui.TextDisabled(label);
        var lineCount = CountLines(text);
        var padding = ImGui.GetStyle().WindowPadding;
        var height = Math.Max(42f * ImGuiHelpers.GlobalScale,
            lineCount * ImGui.GetTextLineHeightWithSpacing() + padding.Y * 2);
        if (ImGui.CalcTextSize(text).X > ImGui.GetContentRegionAvail().X - padding.X * 2)
            height += ImGui.GetStyle().ScrollbarSize;
        ImGui.BeginChild($"##{label}", new Vector2(0, height), true,
            ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.TextUnformatted(text);
        ImGui.EndChild();
    }

    private static int CountLines(string text)
    {
        var lines = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
