using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BigMac.Config;

public sealed class BigMacConfig : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public List<SavedMacroDefinition> Macros { get; set; } = [];
}

public enum MacroStorageKind
{
    Individual = 0,
    Shared = 1,
}

public sealed class SavedMacroDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New Macro";

    public string Command { get; set; } = string.Empty;

    public string Script { get; set; } = string.Empty;

    public bool AllowDangerousCommands { get; set; } = true;

    public MacroGameLocation MacroLocation { get; set; } = new();

    public HotbarPlacement PreferredHotbarPlacement { get; set; } = new();
}

public sealed class MacroGameLocation
{
    public MacroStorageKind StorageKind { get; set; } = MacroStorageKind.Individual;

    public int Slot { get; set; } = 1;

    public MacroGameLocation Sanitize()
        => new()
        {
            StorageKind = StorageKind,
            Slot = Math.Clamp(Slot, 1, 100),
        };

    public uint ToPageId()
        => StorageKind == MacroStorageKind.Shared ? 1u : 0u;

    public byte ToPageIdByte()
        => (byte)ToPageId();

    public int ToZeroBasedSlot()
        => Math.Clamp(Slot, 1, 100) - 1;

    public uint ToCommandId()
        => (uint)(ToZeroBasedSlot() + (StorageKind == MacroStorageKind.Shared ? 100 : 0));

    public string DisplayText
        => $"{StorageKind} #{Math.Clamp(Slot, 1, 100)}";
}

public sealed class HotbarPlacement
{
    public int Hotbar { get; set; } = 1;

    public int Slot { get; set; } = 1;

    public HotbarPlacement Sanitize()
        => new()
        {
            Hotbar = Math.Clamp(Hotbar, 1, 10),
            Slot = Math.Clamp(Slot, 1, 12),
        };

    public int ToZeroBasedHotbar()
        => Math.Clamp(Hotbar, 1, 10) - 1;

    public int ToZeroBasedSlot()
        => Math.Clamp(Slot, 1, 12) - 1;

    public string DisplayText
        => $"Hotbar {Math.Clamp(Hotbar, 1, 10)} / Slot {Math.Clamp(Slot, 1, 12)}";
}
