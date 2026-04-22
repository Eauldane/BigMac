using System;
using System.Collections.Generic;
using System.Linq;
using BigMac.Config;
using Dalamud.Plugin;

namespace BigMac.Services;

public sealed class ConfigService
{
    private readonly IDalamudPluginInterface _pluginInterface;

    public ConfigService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Current = pluginInterface.GetPluginConfig() as BigMacConfig ?? new BigMacConfig();
        Current.Version = Math.Max(Current.Version, 2);
        Current.Macros ??= [];
        foreach (var macro in Current.Macros)
        {
            macro.MacroLocation ??= new MacroGameLocation();
            macro.MacroLocation = macro.MacroLocation.Sanitize();
            macro.PreferredHotbarPlacement ??= new HotbarPlacement();
            macro.PreferredHotbarPlacement = macro.PreferredHotbarPlacement.Sanitize();
        }

        Save();
    }

    public BigMacConfig Current { get; }

    public IReadOnlyList<SavedMacroDefinition> Macros
        => Current.Macros;

    public SavedMacroDefinition? GetMacro(Guid id)
        => Current.Macros.FirstOrDefault(macro => macro.Id == id);

    public SavedMacroDefinition UpsertMacro(SavedMacroDefinition macro)
    {
        var existingIndex = Current.Macros.FindIndex(existing => existing.Id == macro.Id);
        if (existingIndex >= 0)
        {
            Current.Macros[existingIndex] = macro;
        }
        else
        {
            Current.Macros.Add(macro);
        }

        Current.Macros = Current.Macros
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Command, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Save();
        return macro;
    }

    public bool DeleteMacro(Guid id)
    {
        var removed = Current.Macros.RemoveAll(macro => macro.Id == id) > 0;
        if (removed)
        {
            Save();
        }

        return removed;
    }

    public void Save()
        => _pluginInterface.SavePluginConfig(Current);
}
