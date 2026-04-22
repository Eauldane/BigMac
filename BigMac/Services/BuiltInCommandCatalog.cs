using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace BigMac.Services;

public sealed class BuiltInCommandCatalog
{
    private readonly HashSet<string> _commands = new(StringComparer.OrdinalIgnoreCase);

    public BuiltInCommandCatalog(IDataManager dataManager)
    {
        var sheet = dataManager.GetExcelSheet<TextCommand>();
        if (sheet is null)
        {
            return;
        }

        foreach (var row in sheet)
        {
            Add(row.Command.ToString());
            Add(row.ShortCommand.ToString());
            Add(row.Alias.ToString());
            Add(row.ShortAlias.ToString());
        }
    }

    public bool Contains(string command)
        => _commands.Contains(command);

    private void Add(string? commandText)
    {
        var normalized = NormalizeCommand(commandText);
        if (normalized is not null)
        {
            _commands.Add(normalized);
        }
    }

    private static string? NormalizeCommand(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var trimmed = commandText.Trim();
        var firstWhitespace = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        if (firstWhitespace >= 0)
        {
            trimmed = trimmed[..firstWhitespace];
        }

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.ToLowerInvariant();
    }
}
