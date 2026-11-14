using System;
using System.Collections.Generic;
using System.Linq;
using BigMac.Config;
using BigMac.Services;
using BigMac.Scripting;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using ElezenTools.UI;

namespace BigMac.UI;

public sealed class BigMacWindow : Window
{
    private const int MaxNameLength = 128;
    private const int MaxCommandLength = 64;
    private const int MaxScriptLength = 32_768;
    private const float LabelColumnWidth = 180f;
    private const float CompactFieldWidth = 140f;
    private const string PvpDisabledMessage = "BigMac is disabled in PVP";

    private readonly ConfigService _config;
    private readonly MacroRegistry _macroRegistry;
    private readonly MacroHotbarService _hotbarService;
    private readonly Action _openHelp;
    private readonly ScriptEditor _scriptEditor = new();

    private Guid? _selectedMacroId;
    private string _name = "New Macro";
    private string _command = "/newmacro";
    private string _script = string.Empty;
    private bool _allowDangerousCommands = false;
    private MacroStorageKind _macroStorageKind = MacroStorageKind.Individual;
    private int _macroSlot = 1;
    private int _hotbarNumber = 1;
    private int _hotbarSlot = 1;

    private IReadOnlyList<ScriptPreviewNode> _previewNodes = [];
    private string? _parseError;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _isPvpDisabled;
    private float _settingsContentHeight;

    public BigMacWindow(
        ConfigService config,
        MacroRegistry macroRegistry,
        MacroHotbarService hotbarService,
        Action openHelp,
        Action openSupport)
        : base("BigMac")
    {
        _config = config;
        _macroRegistry = macroRegistry;
        _hotbarService = hotbarService;
        _openHelp = openHelp;

        this.TitleBarButtons =
        [
            new()
            {
                Icon = FontAwesomeIcon.GlobeEurope,
                ShowTooltip = () => ImGui.SetTooltip("Discord"),
                Click = (btn) => Util.OpenLink("https://discord.gg/elznmods")
            },
            new()
            {
                Icon = FontAwesomeIcon.Heart,
                ShowTooltip = () => ImGui.SetTooltip("Support BigMac"),
                Click = (btn) => openSupport()
            }
        ];
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(860, 520),
            MaximumSize = new Vector2(2200, 1400),
        };

        if (_config.Macros.Count > 0)
        {
            LoadMacro(_config.Macros[0]);
        }
        else
        {
            RefreshPreview();
        }
    }

    public override void Draw()
    {
        if (_isPvpDisabled)
        {
            DrawPvpDisabledState();
            return;
        }

        DrawMacroList();
        ImGui.SameLine();
        DrawEditor();
    }

    public void SetPvpDisabled(bool isPvpDisabled)
        => _isPvpDisabled = isPvpDisabled;

    private void DrawMacroList()
    {
        var sidebarWidth = 240f * ImGuiHelpers.GlobalScale;
        var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;

        ImGui.BeginGroup();
        ImGui.BeginChild("##bigmac-macro-list", new Vector2(sidebarWidth, -footerHeight), true);
        ModernSection.Header(FontAwesomeIcon.Scroll, "BigMac");
        if (BigMacUi.IconButton(FontAwesomeIcon.Plus, "New Macro", ImGui.GetContentRegionAvail().X))
        {
            StartNewMacro();
        }

        ModernSidebar.DrawSeparator();
        ImGui.TextDisabled("Saved macros");

        foreach (var macro in _config.Macros)
        {
            var selected = _selectedMacroId == macro.Id;
            ImGui.PushID(macro.Id.ToString());
            var rowStart = ImGui.GetCursorScreenPos();
            ImGui.PushClipRect(rowStart, rowStart + new Vector2(ImGui.GetContentRegionAvail().X, 34f * ImGuiHelpers.GlobalScale), true);
            if (ModernSidebar.DrawRow(FontAwesomeIcon.Scroll, macro.Name, selected))
            {
                LoadMacro(macro);
            }
            ImGui.PopClipRect();
            ElezenImgui.AttachTooltip($"{macro.Name}\n{macro.Command}");
            ImGui.PopID();
        }

        ImGui.EndChild();

        var sidebarPadding = ImGui.GetStyle().WindowPadding.X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + sidebarPadding);
        if (BigMacUi.IconButton(FontAwesomeIcon.Book, "Help", sidebarWidth - sidebarPadding * 2))
        {
            _openHelp();
        }

        ImGui.EndGroup();
    }

    private static void DrawPvpDisabledState()
    {
        var available = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize(PvpDisabledMessage);
        var position = new Vector2(
            MathF.Max((available.X - textSize.X) * 0.5f, 0f),
            MathF.Max((available.Y - textSize.Y) * 0.5f, 0f));

        ImGui.SetCursorPos(position);
        ImGui.TextUnformatted(PvpDisabledMessage);
    }

    private void DrawEditor()
    {
        ImGui.BeginChild("##bigmac-editor", new Vector2(0, 0), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding);

        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        // Keep configuration scrollable at smaller sizes so it cannot consume the script/footer area.
        var preferredSettingsHeight = _settingsContentHeight > 0 ? _settingsContentHeight
            : ImGui.GetFrameHeightWithSpacing() * 8 + 24f * scale;
        var settingsHeight = MathF.Min(ImGui.GetContentRegionAvail().Y * 0.36f, preferredSettingsHeight);
        ImGui.BeginChild("##bigmac-settings", new Vector2(0, settingsHeight), false);
        ModernSection.Header(FontAwesomeIcon.Edit, "Macro editor");
        var changed = false;
        DrawFieldLabel("Name");
        changed |= ImGui.InputText("##name", ref _name, MaxNameLength);

        DrawFieldLabel("Slash Command");
        changed |= ImGui.InputText("##slash-command", ref _command, MaxCommandLength);

        DrawFieldLabel("Dangerous Commands");
        changed |= ImGui.Checkbox("##allow-dangerous-commands", ref _allowDangerousCommands);
        ElezenImgui.DrawHelpText("Enabling this option allows BigMac to send chat messages. Badly written macros could expose your mod usage!");

        DrawMacroSlotEditor();
        DrawHotbarEditor();
        _settingsContentHeight = ImGui.GetCursorPosY();
        ImGui.EndChild();

        ModernSection.Header(FontAwesomeIcon.Code, "Script");
        var actionRows = CountActionRows(ImGui.GetContentRegionAvail().X);
        var statusHeight = ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y * 2;
        var drilldownHeaderHeight = MathF.Max(ImGui.GetTextLineHeight(), ElezenImgui.GetIconSize(FontAwesomeIcon.List).Y)
            + 7f * scale + spacing;
        var reservedHeight = actionRows * ImGui.GetFrameHeightWithSpacing() + statusHeight
            + drilldownHeaderHeight + spacing * 6;
        // Share the remaining space with Drilldown instead of enforcing a fixed script minimum.
        var scriptHeight = MathF.Max(1f, (ImGui.GetContentRegionAvail().Y - reservedHeight) * 0.65f);
        changed |= _scriptEditor.Draw(ref _script, new Vector2(0, scriptHeight), MaxScriptLength);

        if (changed)
        {
            RefreshPreview();
            _statusMessage = null;
            _statusIsError = false;
        }

        ImGui.Spacing();
        if (BigMacUi.IconButton(FontAwesomeIcon.Save, "Save Macro"))
        {
            SaveMacro();
        }

        ContinueActionRow(FontAwesomeIcon.Plus, "Add To Hotbar");
        if (BigMacUi.IconButton(FontAwesomeIcon.Plus, "Add To Hotbar"))
        {
            SaveAndAddToHotbar();
        }

        if (_selectedMacroId.HasValue)
        {
            ContinueActionRow(FontAwesomeIcon.Copy, "Duplicate Macro");
            if (BigMacUi.IconButton(FontAwesomeIcon.Copy, "Duplicate Macro"))
            {
                DuplicateSelectedMacro();
            }

            ContinueActionRow(FontAwesomeIcon.Trash, "Delete Macro");
            if (BigMacUi.IconButton(FontAwesomeIcon.Trash, "Delete Macro"))
            {
                DeleteSelectedMacro();
            }
        }

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            // Only occupy space for an actual message; cap long messages so they scroll.
            var padding = ImGui.GetStyle().WindowPadding;
            var wrapWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - padding.X * 2
                - ImGui.GetStyle().ScrollbarSize);
            var messageHeight = MathF.Min(statusHeight,
                ImGui.CalcTextSize(_statusMessage, false, wrapWidth).Y + padding.Y * 2);
            ImGui.BeginChild("##bigmac-status", new Vector2(0, messageHeight), false,
                ImGuiWindowFlags.AlwaysUseWindowPadding);
            var colour = _statusIsError ? BigMacTheme.Palette.BooleanFalse : BigMacTheme.Palette.BooleanTrue;
            ElezenImgui.ColouredWrappedText(_statusMessage, colour);
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ModernSection.Header(FontAwesomeIcon.List, "Drilldown");
        ImGui.BeginChild("##bigmac-preview", new Vector2(0, 0), true, ImGuiWindowFlags.HorizontalScrollbar);
        if (!string.IsNullOrWhiteSpace(_parseError))
        {
            ElezenImgui.ColouredWrappedText(_parseError, BigMacTheme.Palette.BooleanFalse);
        }
        else if (_previewNodes.Count == 0)
        {
            ImGui.TextDisabled("Script is empty.");
        }
        else
        {
            DrawPreviewNodes(_previewNodes);
        }

        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void SaveMacro()
    {
        var previousMacro = _selectedMacroId.HasValue ? _config.GetMacro(_selectedMacroId.Value) : null;
        if (!TryBuildDraft(previousMacro, out var macro, out var error))
        {
            SetStatus(error, isError: true);
            return;
        }

        _config.UpsertMacro(macro);
        _macroRegistry.Reload();
        LoadMacro(macro);
        var suffix = previousMacro is not null
                  && previousMacro.MacroLocation.Sanitize().ToCommandId() != macro.MacroLocation.Sanitize().ToCommandId()
            ? " Re-add it to a hotbar if you moved the backing game macro slot."
            : string.Empty;
        SetStatus($"Saved {macro.Command}.{suffix}", isError: false);
    }

    private void SaveAndAddToHotbar()
    {
        var previousMacro = _selectedMacroId.HasValue ? _config.GetMacro(_selectedMacroId.Value) : null;
        if (!TryBuildDraft(previousMacro, out var macro, out var error))
        {
            SetStatus(error, isError: true);
            return;
        }

        _config.UpsertMacro(macro);
        _macroRegistry.Reload();
        LoadMacro(macro);
        if (!_hotbarService.TryPlaceMacro(macro, previousMacro?.Command, out var message))
        {
            SetStatus(message, isError: true);
            return;
        }

        SetStatus(message, isError: false);
    }

    private void DeleteSelectedMacro()
    {
        if (!_selectedMacroId.HasValue)
        {
            return;
        }

        if (!_config.DeleteMacro(_selectedMacroId.Value))
        {
            SetStatus("Could not delete the selected macro.", isError: true);
            return;
        }

        _macroRegistry.Reload();
        StartNewMacro();
        SetStatus("Deleted macro.", isError: false);
    }

    private void DuplicateSelectedMacro()
    {
        if (!_selectedMacroId.HasValue)
        {
            return;
        }

        _selectedMacroId = null;
        _name = MacroDuplicatePlanner.CreateDuplicateName(_name, _config.Macros.Select(static macro => macro.Name));
        _command = BuildDuplicateCommand(_command);

        var foundMacroLocation = MacroDuplicatePlanner.TryFindDuplicateMacroLocation(
            (int)_macroStorageKind,
            _macroSlot,
            _config.Macros.Select(static macro =>
            {
                var location = macro.MacroLocation.Sanitize();
                return ((int)location.StorageKind, location.Slot);
            }),
            out var duplicateBookId,
            out var duplicateSlot);

        _macroStorageKind = (MacroStorageKind)duplicateBookId;
        _macroSlot = duplicateSlot;

        RefreshPreview();
        if (foundMacroLocation)
        {
            SetStatus("Loaded duplicate as a new draft.", isError: false);
            return;
        }

        SetStatus("Loaded duplicate as a new draft. Choose an unused macro book and slot before saving.", isError: true);
    }

    private bool TryBuildDraft(SavedMacroDefinition? previousMacro, out SavedMacroDefinition macro, out string error)
    {
        macro = new SavedMacroDefinition();
        error = string.Empty;

        var name = _name.Trim();
        if (name.Length == 0)
        {
            error = "Macro name is required.";
            return false;
        }

        if (!_macroRegistry.TryValidateCommand(_command, _selectedMacroId, out var normalizedCommand, out error))
        {
            return false;
        }

        var macroLocation = new MacroGameLocation
        {
            StorageKind = _macroStorageKind,
            Slot = _macroSlot,
        }.Sanitize();
        if (!TryValidateMacroLocation(macroLocation, previousMacro, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_script))
        {
            error = "Script is required.";
            return false;
        }

        if (!ScriptCompiler.TryParse(_script, out _, out var parseError))
        {
            error = parseError!.Message;
            return false;
        }

        macro = new SavedMacroDefinition
        {
            Id = _selectedMacroId ?? Guid.NewGuid(),
            Name = name,
            Command = normalizedCommand,
            Script = _script.Trim(),
            AllowDangerousCommands = _allowDangerousCommands,
            MacroLocation = macroLocation,
            PreferredHotbarPlacement = new HotbarPlacement
            {
                Hotbar = _hotbarNumber,
                Slot = _hotbarSlot,
            }.Sanitize(),
        };
        return true;
    }

    private void LoadMacro(SavedMacroDefinition macro)
    {
        _selectedMacroId = macro.Id;
        _name = macro.Name;
        _command = macro.Command;
        _script = macro.Script;
        _scriptEditor.SetText(_script);
        _allowDangerousCommands = macro.AllowDangerousCommands;
        _macroStorageKind = macro.MacroLocation.StorageKind;
        _macroSlot = macro.MacroLocation.Sanitize().Slot;
        _hotbarNumber = macro.PreferredHotbarPlacement.Sanitize().Hotbar;
        _hotbarSlot = macro.PreferredHotbarPlacement.Sanitize().Slot;
        RefreshPreview();
    }

    private void StartNewMacro()
    {
        _selectedMacroId = null;
        _name = "New Macro";
        _command = "/newmacro";
        _script = string.Empty;
        _scriptEditor.SetText(_script);
        _allowDangerousCommands = true;
        _macroStorageKind = MacroStorageKind.Individual;
        _macroSlot = 1;
        _hotbarNumber = 1;
        _hotbarSlot = 1;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!ScriptCompiler.TryParse(_script, out var program, out var parseError))
        {
            _previewNodes = [];
            _parseError = parseError!.Message;
            _scriptEditor.SetError(parseError);
            return;
        }

        _parseError = null;
        _scriptEditor.SetError(null);
        _previewNodes = ScriptPreviewTreeBuilder.Build(program);
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }

    private void DrawPreviewNodes(IReadOnlyList<ScriptPreviewNode> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            DrawPreviewNode(nodes[index], $"preview-{index}");
        }
    }

    private static void DrawPreviewNode(ScriptPreviewNode node, string id)
    {
        var hasChildren = node.Children.Count > 0;
        var flags = hasChildren
            ? ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen
            : ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        var opened = ImGui.TreeNodeEx($"{node.Label}##{id}", flags);
        if (!hasChildren || !opened)
        {
            return;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            DrawPreviewNode(node.Children[index], $"{id}-{index}");
        }

        ImGui.TreePop();
    }

    private void DrawMacroSlotEditor()
    {
        var storageKind = (int)_macroStorageKind;
        DrawFieldLabel("Macro Book");
        ImGui.SetNextItemWidth(CompactFieldWidth * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("##macro-book", ref storageKind, "Individual\0Shared\0"))
        {
            _macroStorageKind = (MacroStorageKind)storageKind;
        }

        DrawFieldLabel("Macro Slot");
        ImGui.SetNextItemWidth(CompactFieldWidth * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##macro-slot", ref _macroSlot);
        _macroSlot = Math.Clamp(_macroSlot, 1, 100);
        ElezenImgui.DrawHelpText("The game stores macros in individual and shared \"books\". Use this to decide where it's saved if you use the Add to Hotbar option.");
    }

    private void DrawHotbarEditor()
    {
        ImGui.Spacing();
        DrawFieldLabel("Hotbar");
        ImGui.SetNextItemWidth(CompactFieldWidth * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##hotbar-number", ref _hotbarNumber);
        _hotbarNumber = Math.Clamp(_hotbarNumber, 1, 10);

        DrawFieldLabel("Hotbar Slot");
        ImGui.SetNextItemWidth(CompactFieldWidth * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##hotbar-slot", ref _hotbarSlot);
        _hotbarSlot = Math.Clamp(_hotbarSlot, 1, 12);
        ElezenImgui.DrawHelpText("When using the \"Add to Hotbar\" button, a one-line in-game macro is added, then placed on the chosen hotbar slot.");
    }

    private static void DrawFieldLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(LabelColumnWidth * ImGuiHelpers.GlobalScale);
        ImGui.SetNextItemWidth(-1f);
    }

    private static void ContinueActionRow(FontAwesomeIcon icon, string label)
    {
        var rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var nextWidth = BigMacUi.IconButtonWidth(icon, label);
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= rightEdge)
        {
            ImGui.SameLine();
        }
    }

    private static int CountActionRows(float availableWidth)
    {
        // Reserve all four actions even for a new draft: saving can select it in this frame.
        (FontAwesomeIcon Icon, string Label)[] actions =
        [
            (FontAwesomeIcon.Save, "Save Macro"),
            (FontAwesomeIcon.Plus, "Add To Hotbar"),
            (FontAwesomeIcon.Copy, "Duplicate Macro"),
            (FontAwesomeIcon.Trash, "Delete Macro"),
        ];
        var rows = 1;
        var usedWidth = 0f;
        foreach (var (icon, label) in actions)
        {
            var width = BigMacUi.IconButtonWidth(icon, label);
            var gap = usedWidth > 0 ? ImGui.GetStyle().ItemSpacing.X : 0f;
            if (usedWidth > 0 && usedWidth + gap + width > availableWidth)
            {
                rows++;
                usedWidth = 0f;
                gap = 0f;
            }

            usedWidth += gap + width;
        }

        return rows;
    }

    private string BuildDuplicateCommand(string sourceCommand)
    {
        string? lastCandidate = null;
        foreach (var candidate in MacroDuplicatePlanner.EnumerateCommandCandidates(sourceCommand, MaxCommandLength).Take(2048))
        {
            lastCandidate = candidate;
            if (_macroRegistry.TryValidateCommand(candidate, macroId: null, out var normalizedCommand, out _))
            {
                return normalizedCommand;
            }
        }

        return lastCandidate ?? "/newmacro2";
    }

    private bool TryValidateMacroLocation(MacroGameLocation macroLocation, SavedMacroDefinition? previousMacro, out string error)
    {
        error = string.Empty;
        var collision = _config.Macros.FirstOrDefault(existing =>
            existing.Id != _selectedMacroId
         && existing.MacroLocation.Sanitize().ToCommandId() == macroLocation.ToCommandId());
        if (collision is not null)
        {
            error = $"{macroLocation.DisplayText} is already used by '{collision.Name}'.";
            return false;
        }

        if (previousMacro is not null
         && previousMacro.MacroLocation.Sanitize().ToCommandId() == macroLocation.ToCommandId())
        {
            return true;
        }

        return true;
    }
}
