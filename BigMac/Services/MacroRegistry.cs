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
    private readonly object _executionSync = new();
    private readonly HashSet<ActiveMacroExecution> _activeExecutions = [];
    private readonly Dictionary<Guid, ActiveMacroExecution> _activeLoopExecutions = new();
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
        CancelActiveMacros();

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
        if (!isEnabled)
        {
            CancelActiveMacros();
        }

        Reload();
    }

    public void CancelLoopingMacrosForMovement()
        => CancelActiveMacros(static execution => execution.IsLooping);

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

        _ = ExecuteOrToggleMacroAsync(macro, arguments);
    }

    private async Task ExecuteOrToggleMacroAsync(SavedMacroDefinition macro, string arguments)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (TryCancelActiveLoop(macro.Id))
        {
            return;
        }

        ActiveMacroExecution? execution = null;

        try
        {
            var program = ScriptParser.Parse(macro.Script);
            var isLoopingMacro = ScriptStructureInspector.ContainsLoop(program);
            if (!TryBeginExecution(macro.Id, isLoopingMacro, out execution))
            {
                return;
            }

            await _executor.ExecuteAsync(program, new LiveCommandSink(macro.AllowDangerousCommands), execution.Token, arguments).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (execution?.IsCancellationRequested == true)
        {
        }
        catch (ScriptParseException parseException)
        {
            _chatGui.Print($"[BigMac] Could not run {macro.Command}: {parseException.Message}");
        }
        catch (Exception exception)
        {
            _chatGui.Print($"[BigMac] Could not run {macro.Command}: {exception.Message}");
        }
        finally
        {
            if (execution is not null)
            {
                CompleteExecution(macro.Id, execution);
            }
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
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ElezenCommands.SendAsync(commandText, _allowDangerousCommands).ConfigureAwait(false);
        }
    }

    private bool TryBeginExecution(Guid macroId, bool isLoopingMacro, out ActiveMacroExecution? execution)
    {
        lock (_executionSync)
        {
            if (isLoopingMacro && _activeLoopExecutions.TryGetValue(macroId, out var existing))
            {
                existing.Cancel();
                execution = null;
                return false;
            }

            execution = new ActiveMacroExecution(isLoopingMacro);
            _activeExecutions.Add(execution);
            if (isLoopingMacro)
            {
                _activeLoopExecutions[macroId] = execution;
            }

            return true;
        }
    }

    private bool TryCancelActiveLoop(Guid macroId)
    {
        lock (_executionSync)
        {
            if (!_activeLoopExecutions.TryGetValue(macroId, out var existing))
            {
                return false;
            }

            existing.Cancel();
            return true;
        }
    }

    private void CompleteExecution(Guid macroId, ActiveMacroExecution execution)
    {
        lock (_executionSync)
        {
            _activeExecutions.Remove(execution);
            if (execution.IsLooping
             && _activeLoopExecutions.TryGetValue(macroId, out var activeLoopExecution)
             && ReferenceEquals(activeLoopExecution, execution))
            {
                _activeLoopExecutions.Remove(macroId);
            }
        }

        execution.Dispose();
    }

    private void CancelActiveMacros(Func<ActiveMacroExecution, bool>? predicate = null)
    {
        ActiveMacroExecution[] executions;
        lock (_executionSync)
        {
            executions = _activeExecutions
                .Where(execution => predicate?.Invoke(execution) ?? true)
                .ToArray();
        }

        foreach (var execution in executions)
        {
            execution.Cancel();
        }
    }

    private sealed class ActiveMacroExecution : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public ActiveMacroExecution(bool isLooping)
            => IsLooping = isLooping;

        public bool IsLooping { get; }

        public CancellationToken Token
            => _cancellationTokenSource.Token;

        public bool IsCancellationRequested
            => _cancellationTokenSource.IsCancellationRequested;

        public void Cancel()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
            => _cancellationTokenSource.Dispose();
    }
}
