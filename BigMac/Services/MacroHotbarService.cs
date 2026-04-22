using System;
using BigMac.Config;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace BigMac.Services;

public sealed unsafe class MacroHotbarService : IDisposable
{
    public MacroHotbarService()
    {
    }

    public void Dispose()
    {
    }

    public bool TryPlaceMacro(SavedMacroDefinition macro, string? previousCommand, out string message)
    {
        var macroLocation = macro.MacroLocation.Sanitize();
        var placement = macro.PreferredHotbarPlacement.Sanitize();

        var macroModule = RaptureMacroModule.Instance();
        var hotbarModule = RaptureHotbarModule.Instance();
        if (macroModule is null || hotbarModule is null)
        {
            message = "The game macro or hotbar module is not available right now.";
            return false;
        }

        var targetMacro = macroModule->GetMacro(macroLocation.ToPageId(), (uint)macroLocation.ToZeroBasedSlot());
        if (targetMacro is null)
        {
            message = $"Could not access {macroLocation.DisplayText}.";
            return false;
        }

        if (!CanOverwriteSlot(targetMacro, macro.Command, previousCommand, out message))
        {
            return false;
        }

        var existingHotbarSlot = hotbarModule->GetSlotById((uint)placement.ToZeroBasedHotbar(), (uint)placement.ToZeroBasedSlot());
        if (existingHotbarSlot is not null
         && !existingHotbarSlot->IsEmpty
         && (existingHotbarSlot->CommandType != RaptureHotbarModule.HotbarSlotType.Macro
          || existingHotbarSlot->CommandId != macroLocation.ToCommandId()))
        {
            message = $"{placement.DisplayText} is already occupied. Pick another hotbar slot.";
            return false;
        }

        targetMacro->Name.SetString(macro.Name);
        var lines = targetMacro->Lines;
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index].SetString(index == 0 ? macro.Command : string.Empty);
        }

        macroModule->SetSavePendingFlag(true, macroLocation.ToPageId());
        macroModule->SaveFile(true);
        hotbarModule->ReloadMacroSlots(macroLocation.ToPageIdByte(), (byte)macroLocation.ToZeroBasedSlot());
        hotbarModule->SetAndSaveSlot(
            (uint)placement.ToZeroBasedHotbar(),
            (uint)placement.ToZeroBasedSlot(),
            RaptureHotbarModule.HotbarSlotType.Macro,
            macroLocation.ToCommandId(),
            ignoreSharedHotbars: false,
            allowSaveToPvP: false);

        var savedSlot = hotbarModule->GetSlotById((uint)placement.ToZeroBasedHotbar(), (uint)placement.ToZeroBasedSlot());
        if (savedSlot is null
         || savedSlot->CommandType != RaptureHotbarModule.HotbarSlotType.Macro
         || savedSlot->CommandId != macroLocation.ToCommandId())
        {
            message = $"Wrote {macroLocation.DisplayText}, but could not confirm placement on {placement.DisplayText}.";
            return false;
        }

        message = $"Placed {macro.Command} on {placement.DisplayText} using {macroLocation.DisplayText}.";
        return true;
    }

    private static bool CanOverwriteSlot(RaptureMacroModule.Macro* targetMacro, string command, string? previousCommand, out string error)
    {
        error = string.Empty;
        var existingLines = targetMacro->Lines;
        string? existingCommand = null;
        var hasContent = !string.IsNullOrWhiteSpace(targetMacro->Name.ToString());
        for (var index = 0; index < existingLines.Length; index++)
        {
            var text = existingLines[index].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            hasContent = true;
            existingCommand ??= text;
        }

        if (!hasContent)
        {
            return true;
        }

        if (string.Equals(existingCommand, command, StringComparison.OrdinalIgnoreCase)
         || (!string.IsNullOrWhiteSpace(previousCommand)
          && string.Equals(existingCommand, previousCommand, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        error = "The selected in-game macro slot is not empty. Pick another slot or reuse the existing BigMac slot.";
        return false;
    }
}
