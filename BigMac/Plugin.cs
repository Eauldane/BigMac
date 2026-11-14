using System;
using System.Numerics;
using BigMac.Services;
using BigMac.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ElezenTools;

namespace BigMac;

public sealed class Plugin : IDalamudPlugin
{
    private const string MainCommand = "/bigmac";

    private readonly WindowSystem _windowSystem = new("BigMac");
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;

    private readonly ConfigService _config;
    private readonly BuiltInCommandCatalog _builtInCommandCatalog;
    private readonly MacroRegistry _macroRegistry;
    private readonly MacroHotbarService _macroHotbarService;
    private readonly BigMacWindow _window;
    private readonly BigMacHelpWindow _helpWindow;
    private readonly BigMacSupportWindow _supportWindow;
    private Vector3? _lastObservedPlayerPosition;
    private bool _isPvpLocked;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IDataManager dataManager,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IUnlockState unlockState,
        ITargetManager targetManager,
        IPartyList partyList,
        ICondition condition)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _objectTable = objectTable;

        ElezenInit.Init(pluginInterface, this);

        _config = new ConfigService(pluginInterface);
        _builtInCommandCatalog = new BuiltInCommandCatalog(dataManager);
        _macroRegistry = new MacroRegistry(
            commandManager,
            chatGui,
            _config,
            _builtInCommandCatalog,
            new ScriptRuntimeResolver(dataManager, clientState, objectTable, playerState, unlockState, targetManager, partyList, condition));
        _macroHotbarService = new MacroHotbarService();
        _helpWindow = new BigMacHelpWindow();
        _supportWindow = new BigMacSupportWindow();
        _window = new BigMacWindow(_config, _macroRegistry, _macroHotbarService, OpenHelpUi,
            () => _supportWindow.Toggle());
        _windowSystem.AddWindow(_window);
        _windowSystem.AddWindow(_helpWindow);
        _windowSystem.AddWindow(_supportWindow);

        if (_commandManager.Commands.ContainsKey(MainCommand))
        {
            _commandManager.RemoveHandler(MainCommand);
        }

        _commandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the BigMac macro editor. Use '/bigmac help' to open the scripting reference.",
            ShowInHelp = true,
        });

        _pluginInterface.UiBuilder.Draw += DrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        _pluginInterface.UiBuilder.OpenMainUi += OpenUi;
        _clientState.EnterPvP += OnEnterPvP;
        _clientState.LeavePvP += OnLeavePvP;

        SyncPvpState();
    }

    public void Dispose()
    {
        _clientState.EnterPvP -= OnEnterPvP;
        _clientState.LeavePvP -= OnLeavePvP;
        _pluginInterface.UiBuilder.Draw -= DrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        _commandManager.RemoveHandler(MainCommand);
        _windowSystem.RemoveAllWindows();
        _macroHotbarService.Dispose();
        _macroRegistry.Dispose();
        ElezenInit.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var normalizedArguments = arguments.Trim();
        if (string.Equals(normalizedArguments, "help", StringComparison.OrdinalIgnoreCase)
         || string.Equals(normalizedArguments, "-h", StringComparison.OrdinalIgnoreCase)
         || string.Equals(normalizedArguments, "--help", StringComparison.OrdinalIgnoreCase))
        {
            if (_isPvpLocked)
            {
                OpenUi();
            }
            else
            {
                OpenHelpUi();
            }

            return;
        }

        OpenUi();
    }

    private void OpenUi()
        => _window.IsOpen = true;

    private void OpenHelpUi()
        => _helpWindow.IsOpen = true;

    private void DrawUi()
    {
        SyncPvpState();
        CancelLoopsOnMovement();
        using var theme = BigMacTheme.Push();
        _windowSystem.Draw();
    }

    private void OnEnterPvP()
        => SetPvpLock(true);

    private void OnLeavePvP()
        => SetPvpLock(false);

    private void SyncPvpState()
        => SetPvpLock(_clientState.IsPvP);

    private void SetPvpLock(bool isPvpLocked)
    {
        if (_isPvpLocked == isPvpLocked)
        {
            return;
        }

        _isPvpLocked = isPvpLocked;
        _macroRegistry.SetEnabled(!isPvpLocked);
        _window.SetPvpDisabled(isPvpLocked);

        if (isPvpLocked)
        {
            _helpWindow.IsOpen = false;
        }
    }

    private void CancelLoopsOnMovement()
    {
        const float movementCancelDistanceSquared = 0.0025f;

        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            _lastObservedPlayerPosition = null;
            return;
        }

        var currentPosition = localPlayer.Position;
        if (_lastObservedPlayerPosition is Vector3 previousPosition
         && Vector3.DistanceSquared(previousPosition, currentPosition) > movementCancelDistanceSquared)
        {
            _macroRegistry.CancelLoopingMacrosForMovement();
        }

        _lastObservedPlayerPosition = currentPosition;
    }
}
