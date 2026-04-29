using System;
using System.Collections.Generic;
using BigMac.Scripting;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System.Numerics;
using ElezenTools.UI;

namespace BigMac.UI;

public sealed class BigMacHelpWindow : Window, IDisposable
{
    private int _selectedSectionIndex;
    private readonly IFontHandle _titleFont;

    public BigMacHelpWindow(IDalamudPluginInterface pluginInterface)
        : base("BigMac Help")
    {
        _titleFont = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new()
            {
                SizePx = 24,
                GlyphRanges = [0x20, 0x7E, 0],
            }));
        });

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 480),
            MaximumSize = new Vector2(1800, 1400),
        };
    }

    public void Dispose()
        => _titleFont.Dispose();

    public override void Draw()
    {
        var sections = ScriptHelpReference.Sections;
        if (sections.Count == 0)
        {
            ImGui.TextDisabled("No help topics available.");
            return;
        }

        _selectedSectionIndex = Math.Clamp(_selectedSectionIndex, 0, sections.Count - 1);

        var sidebarWidth = 240f;
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
        ImGui.TextDisabled("Topics");
        ImGui.Separator();
        ImGui.Spacing();

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var itemWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
            if (ImGui.Selectable(
                $"{section.Title}##topic-{index}",
                _selectedSectionIndex == index,
                ImGuiSelectableFlags.None,
                new Vector2(itemWidth, 0f)))
            {
                _selectedSectionIndex = index;
            }
        }
    }

    private void DrawSection(int sectionIndex, ScriptHelpSection section)
    {
        ImGui.TextDisabled("Reference");
        ElezenImgui.FontText(section.Title, _titleFont);
        ImGui.Separator();
        ImGui.Spacing();
        ElezenImgui.WrappedText(section.Summary);
        ImGui.Spacing();

        for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
        {
            DrawEntry(sectionIndex, entryIndex, section.Entries[entryIndex]);
            if (entryIndex < section.Entries.Count - 1)
            {
                ImGui.Separator();
                ImGui.Spacing();
            }
        }

        ImGui.Spacing();
    }

    private void DrawEntry(int sectionIndex, int entryIndex, ScriptHelpEntry entry)
    {
        ImGui.PushID($"{sectionIndex}-{entryIndex}");
        ElezenImgui.FontText(entry.Name, _titleFont);
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
        var height = Math.Max(42f, lineCount * ImGui.GetTextLineHeightWithSpacing() + 10f);
        ImGui.BeginChild($"##{label}", new Vector2(-1, height), true);
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
