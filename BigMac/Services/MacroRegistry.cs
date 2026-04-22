using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigMac.Config;
using BigMac.Scripting;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using ElezenTools.Commands;

namespace BigMac.Services;

public sealed class MacroRegistry : IDisposable
{
    private const string EditorCommand = "/bigmac";
    private const string DisabledMessage = "BigMac is disabled in PVP.";

    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly ConfigService _config;
    private readonly BuiltInCommandCatalog _builtInCommands;
    private readonly Dictionary<string, SavedMacroDefinition> _registeredMacros = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _commandsByMacroId = new();
    private readonly ScriptExecutor _executor;
    private bool _isEnabled = true;

    public MacroRegistry(
        ICommandManager commandManager,
        IChatGui chatGui,
        ConfigService config,
        BuiltInCommandCatalog builtInCommands,
        IScriptRuntimeResolver runtimeResolver)
    {
        _commandManager = commandManager;
        _chatGui = chatGui;
        _config = config;
        _builtInCommands = builtInCommands;
        _executor = new ScriptExecutor(runtimeResolver);

        Reload();
    }

    public void Dispose()
    {
        foreach (var command in _registeredMacros.Keys.ToArray())
        {
            _commandManager.RemoveHandler(command);
        }

        _registeredMacros.Clear();
        _commandsByMacroId.Clear();
    }

    public void Reload()
    {
        var desired = new Dictionary<string, SavedMacroDefinition>(StringComparer.OrdinalIgnoreCase);
        if (_isEnabled)
        {
            foreach (var macro in _config.Macros)
            {
                var command = NormalizeCommand(macro.Command);
                if (command is null)
                {
                    continue;
                }

                if (!desired.TryAdd(command, macro))
                {
                    _chatGui.Print($"[BigMac] Skipping duplicate saved command {command}.");
                }
            }
        }

        foreach (var command in _registeredMacros.Keys.Except(desired.Keys, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            _commandManager.RemoveHandler(command);
            if (_registeredMacros.TryGetValue(command, out var removedMacro))
            {
                _commandsByMacroId.Remove(removedMacro.Id);
            }

            _registeredMacros.Remove(command);
        }

        foreach (var (command, macro) in desired)
        {
            if (_builtInCommands.Contains(command))
            {
                _chatGui.Print($"[BigMac] Skipping {command}: it conflicts with a built-in game command.");
                continue;
            }

            if (!_registeredMacros.ContainsKey(command))
            {
                try
                {
                    _commandManager.AddHandler(command, new CommandInfo(OnMacroCommand)
                    {
                        HelpMessage = $"Run the BigMac macro '{macro.Name}'.",
                        ShowInHelp = false,
                    });
                }
                catch (Exception exception)
                {
                    _chatGui.Print($"[BigMac] Could not register {command}: {exception.Message}");
                    continue;
                }
            }

            _registeredMacros[command] = macro;
            _commandsByMacroId[macro.Id] = command;
        }
    }

    public void SetEnabled(bool isEnabled)
    {
        if (_isEnabled == isEnabled)
        {
            return;
        }

        _isEnabled = isEnabled;
        Reload();
    }

    public bool TryValidateCommand(string? commandText, Guid? macroId, out string normalizedCommand, out string error)
    {
        if (!_isEnabled)
        {
            normalizedCommand = string.Empty;
            error = DisabledMessage;
            return false;
        }

        var normalized = NormalizeCommand(commandText);
        if (normalized is null)
        {
            normalizedCommand = string.Empty;
            error = "Slash commands must start with '/' and may not contain spaces.";
            return false;
        }

        if (string.Equals(normalized, EditorCommand, StringComparison.OrdinalIgnoreCase))
        {
            normalizedCommand = string.Empty;
            error = $"{EditorCommand} is reserved for the BigMac editor.";
            return false;
        }

        if (_builtInCommands.Contains(normalized))
        {
            normalizedCommand = string.Empty;
            error = $"{normalized} is already used by a built-in game command.";
            return false;
        }

        if (_commandsByMacroId.TryGetValue(macroId ?? Guid.Empty, out var existingForMacro)
         && string.Equals(existingForMacro, normalized, StringComparison.OrdinalIgnoreCase))
        {
            normalizedCommand = normalized;
            error = string.Empty;
            return true;
        }

        if (_registeredMacros.TryGetValue(normalized, out var existingMacro)
         && existingMacro.Id != macroId)
        {
            normalizedCommand = string.Empty;
            error = $"{normalized} is already used by the saved macro '{existingMacro.Name}'.";
            return false;
        }

        if (_commandManager.Commands.ContainsKey(normalized)
         && (!_registeredMacros.TryGetValue(normalized, out var ownedMacro) || ownedMacro.Id != macroId))
        {
            normalizedCommand = string.Empty;
            error = $"{normalized} is already registered by another plugin.";
            return false;
        }

        normalizedCommand = normalized;
        error = string.Empty;
        return true;
    }

    private void OnMacroCommand(string command, string arguments)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (!_registeredMacros.TryGetValue(command, out var macro))
        {
            return;
        }

        _ = ExecuteMacroAsync(macro);
    }

    private async Task ExecuteMacroAsync(SavedMacroDefinition macro)
    {
        if (!_isEnabled)
        {
            return;
        }

        try
        {
            var program = ScriptParser.Parse(macro.Script);
            await _executor.ExecuteAsync(program, new LiveCommandSink(macro.AllowDangerousCommands)).ConfigureAwait(false);
        }
        catch (ScriptParseException parseException)
        {
            _chatGui.Print($"[BigMac] Could not run {macro.Command}: {parseException.Message}");
        }
        catch (Exception exception)
        {
            _chatGui.Print($"[BigMac] Could not run {macro.Command}: {exception.Message}");
        }
    }

    private static string? NormalizeCommand(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var normalized = commandText.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.Length <= 1 || normalized.Any(char.IsWhiteSpace))
        {
            return null;
        }

        return normalized.ToLowerInvariant();
    }

    private sealed class LiveCommandSink : ICommandSink
    {
        private readonly bool _allowDangerousCommands;

        public LiveCommandSink(bool allowDangerousCommands)
            => _allowDangerousCommands = allowDangerousCommands;

        public async ValueTask ExecuteAsync(string commandText, CancellationToken cancellationToken)
            => await ElezenCommands.SendAsync(commandText, _allowDangerousCommands).ConfigureAwait(false);
    }
}
