using System;
using System.Collections.Generic;
using System.Numerics;
using BigMac.Scripting;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ElezenTools.Data;
using ElezenTools.Data.Classes;
using ElezenTools.Data.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using ClientStructsActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BigMac.Services;

public sealed class ScriptRuntimeResolver : IScriptRuntimeResolver
{
    private sealed record ActionLookup(
        Func<ScriptRuntimeResolver, bool> IsUnlocked,
        Func<ScriptRuntimeResolver, bool> IsAvailable,
        Func<ScriptRuntimeResolver, bool> IsReady);

    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPlayerState _playerState;
    private readonly IUnlockState _unlockState;
    private readonly ITargetManager _targetManager;
    private readonly IPartyList _partyList;
    private readonly ICondition _condition;
    private readonly object _syncRoot = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<string, IReadOnlyList<ActionLookup>>> _actionLookupsByLanguage = [];
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<string, IReadOnlyList<uint>>> _statusIdsByLanguage = [];

    public ScriptRuntimeResolver(
        IDataManager dataManager,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IUnlockState unlockState,
        ITargetManager targetManager,
        IPartyList partyList,
        ICondition condition)
    {
        _dataManager = dataManager;
        _clientState = clientState;
        _objectTable = objectTable;
        _playerState = playerState;
        _unlockState = unlockState;
        _targetManager = targetManager;
        _partyList = partyList;
        _condition = condition;
    }

    public bool IsActionUnlocked(string actionName)
    {
        var normalized = NormalizeLookup(actionName)
            ?? throw new InvalidOperationException("action_unlocked(...) requires a non-empty action name.");
        var actions = ResolveActionLookups(normalized)
            ?? throw new InvalidOperationException($"Unknown action '{actionName}'.");

        foreach (var action in actions)
        {
            if (action.IsUnlocked(this))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsActionAvailable(string actionName)
    {
        var normalized = NormalizeLookup(actionName)
            ?? throw new InvalidOperationException("action_available(...) requires a non-empty action name.");
        var actions = ResolveActionLookups(normalized)
            ?? throw new InvalidOperationException($"Unknown action '{actionName}'.");

        foreach (var action in actions)
        {
            if (action.IsAvailable(this))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsActionReady(string actionName)
    {
        var normalized = NormalizeLookup(actionName)
            ?? throw new InvalidOperationException("action_ready(...) requires a non-empty action name.");
        var actions = ResolveActionLookups(normalized)
            ?? throw new InvalidOperationException($"Unknown action '{actionName}'.");

        foreach (var action in actions)
        {
            if (action.IsReady(this))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsCurrentClass(string abbreviation)
    {
        var normalized = NormalizeLookup(abbreviation)
            ?? throw new InvalidOperationException("class(...) requires a non-empty class abbreviation.");
        var targetJob = ResolveJob(normalized)
            ?? throw new InvalidOperationException($"Unknown class abbreviation '{abbreviation}'.");

        return _playerState.IsLoaded && _playerState.ClassJob.RowId == targetJob.Id;
    }

    public bool HasStatus(string statusName)
        => HasStatusOn(_objectTable.LocalPlayer as IBattleChara, statusName, "has_status");

    public bool TargetHasStatus(string statusName)
        => HasStatusOn(GetTarget() as IBattleChara, statusName, "target_has_status");

    private bool HasStatusOn(IBattleChara? battleChara, string statusName, string functionName)
    {
        var normalized = NormalizeLookup(statusName)
            ?? throw new InvalidOperationException($"{functionName}(...) requires a non-empty status name.");
        var statusIds = ResolveStatusIds(normalized)
            ?? throw new InvalidOperationException($"Unknown status '{statusName}'.");

        if (battleChara is null)
        {
            return false;
        }

        var statusIdSet = new HashSet<uint>(statusIds);
        foreach (var activeStatus in battleChara.StatusList)
        {
            if (activeStatus.StatusId != 0 && statusIdSet.Contains(activeStatus.StatusId))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTargetEnemy()
    {
        var target = GetTarget();
        if (target is null)
        {
            return false;
        }

        if (target is ICharacter character && (character.StatusFlags & StatusFlags.Hostile) != 0)
        {
            return true;
        }

        return target is IBattleNpc battleNpc && battleNpc.BattleNpcKind == BattleNpcSubKind.Enemy;
    }

    public bool TryEvaluateFunction(string name, IReadOnlyList<ExpressionValue> arguments, out ExpressionValue value)
    {
        value = name.ToLowerInvariant() switch
        {
            "action_unlocked" => new BooleanExpressionValue(IsActionUnlocked(GetRequiredStringArgument(name, arguments))),
            "action_available" => new BooleanExpressionValue(IsActionAvailable(GetRequiredStringArgument(name, arguments))),
            "action_ready" => new BooleanExpressionValue(IsActionReady(GetRequiredStringArgument(name, arguments))),
            "class" or "job" => new BooleanExpressionValue(IsCurrentClass(GetRequiredStringArgument(name, arguments))),
            "has_status" or "have_status" => new BooleanExpressionValue(HasStatus(GetRequiredStringArgument(name, arguments))),
            "target_has_status" => new BooleanExpressionValue(TargetHasStatus(GetRequiredStringArgument(name, arguments))),
            "target_is_enemy" => new BooleanExpressionValue(GetRequiredNoArgumentValue(name, arguments, IsTargetEnemy)),
            "distance_to_target" => new NumberExpressionValue(GetRequiredNoArgumentValue(name, arguments, GetDistanceToTarget)),
            _ => null!,
        };

        return value is not null;
    }

    public bool TryResolveVariable(string name, out ExpressionValue value)
    {
        value = name.ToLowerInvariant() switch
        {
            "job_abbr" => new StringExpressionValue(GetCurrentJob()?.Abbreviation ?? string.Empty),
            "job_id" => new NumberExpressionValue(_playerState.IsLoaded ? (int)_playerState.ClassJob.RowId : 0),
            "job_name" => new StringExpressionValue(GetCurrentJob()?.Name ?? string.Empty),
            "level" => new NumberExpressionValue(_playerState.IsLoaded ? _playerState.EffectiveLevel : 0),
            "hp_pct" => new NumberExpressionValue(GetHpPercentage(_objectTable.LocalPlayer)),
            "mp_pct" => new NumberExpressionValue(GetMpPercentage(_objectTable.LocalPlayer)),
            "player_name" => new StringExpressionValue(_playerState.IsLoaded ? _playerState.CharacterName : string.Empty),
            "has_target" => new BooleanExpressionValue(GetTarget() is not null),
            "target_name" => new StringExpressionValue(_targetManager.Target?.Name.ToString() ?? string.Empty),
            "target_hp_pct" => new NumberExpressionValue(GetHpPercentage(GetTarget() as ICharacter)),
            "zone_name" => new StringExpressionValue(GetCurrentZoneName()),
            "world_name" => new StringExpressionValue(GetCurrentWorldName()),
            "role" => new StringExpressionValue(GetCurrentRoleName()),
            "party_size" => new NumberExpressionValue(GetPartySize()),
            "in_combat" => new BooleanExpressionValue(_condition[ConditionFlag.InCombat]),
            "mounted" => new BooleanExpressionValue(_condition.Any(ConditionFlag.Mounted, ConditionFlag.RidingPillion)),
            "gposing" => new BooleanExpressionValue(_clientState.IsGPosing),
            _ => null!,
        };

        return value is not null;
    }

    private IReadOnlyList<ActionLookup>? ResolveActionLookups(string actionName)
    {
        var clientLanguage = _clientState.ClientLanguage;
        if (TryGetActionLookups(actionName, clientLanguage, out var actions))
        {
            return actions;
        }

        return clientLanguage != ClientLanguage.English
            && TryGetActionLookups(actionName, ClientLanguage.English, out actions)
                ? actions
                : null;
    }

    private IReadOnlyList<uint>? ResolveStatusIds(string statusName)
    {
        var clientLanguage = _clientState.ClientLanguage;
        if (TryGetStatusIds(statusName, clientLanguage, out var statusIds))
        {
            return statusIds;
        }

        return clientLanguage != ClientLanguage.English
            && TryGetStatusIds(statusName, ClientLanguage.English, out statusIds)
                ? statusIds
                : null;
    }

    private JobData? ResolveJob(string abbreviation)
    {
        var clientLanguage = _clientState.ClientLanguage;
        var resolved = FindJobByAbbreviation(abbreviation, clientLanguage);
        if (resolved is not null)
        {
            return resolved;
        }

        return clientLanguage == ClientLanguage.English
            ? null
            : FindJobByAbbreviation(abbreviation, ClientLanguage.English);
    }

    private bool TryGetActionLookups(string actionName, ClientLanguage language, out IReadOnlyList<ActionLookup> actions)
        => GetActionLookups(language).TryGetValue(actionName, out actions!);

    private bool TryGetStatusIds(string statusName, ClientLanguage language, out IReadOnlyList<uint> statusIds)
        => GetStatusIds(language).TryGetValue(statusName, out statusIds!);

    private IReadOnlyDictionary<string, IReadOnlyList<ActionLookup>> GetActionLookups(ClientLanguage language)
    {
        lock (_syncRoot)
        {
            if (_actionLookupsByLanguage.TryGetValue(language, out var actions))
            {
                return actions;
            }

            actions = BuildActionLookups(language);
            _actionLookupsByLanguage[language] = actions;
            return actions;
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<uint>> GetStatusIds(ClientLanguage language)
    {
        lock (_syncRoot)
        {
            if (_statusIdsByLanguage.TryGetValue(language, out var statusIds))
            {
                return statusIds;
            }

            statusIds = BuildStatusIds(language);
            _statusIdsByLanguage[language] = statusIds;
            return statusIds;
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<ActionLookup>> BuildActionLookups(ClientLanguage language)
    {
        var actions = new Dictionary<string, IReadOnlyList<ActionLookup>>(StringComparer.OrdinalIgnoreCase);

        AddActionRows(
            actions,
            _dataManager.GetExcelSheet<LuminaAction>(language),
            static row => row.IsPlayerAction,
            static row => row.Name.ToString(),
            static (resolver, row) => resolver.IsPlayerActionUnlocked(row, accountForLevelSync: false),
            static (resolver, row) => resolver.IsPlayerActionAvailable(row),
            static (resolver, row) => resolver.IsPlayerActionReady(row));
        AddActionRows(
            actions,
            _dataManager.GetExcelSheet<CraftAction>(language),
            static _ => true,
            static row => row.Name.ToString(),
            static (resolver, row) => resolver._clientState.IsLoggedIn && resolver._unlockState.IsCraftActionUnlocked(row),
            static (resolver, row) => resolver.IsCraftActionAvailable(row),
            static (resolver, row) => resolver.IsCraftActionReady(row));
        AddActionRows(
            actions,
            _dataManager.GetExcelSheet<GeneralAction>(language),
            static _ => true,
            static row => row.Name.ToString(),
            static (resolver, row) => resolver._clientState.IsLoggedIn && resolver._unlockState.IsGeneralActionUnlocked(row),
            static (resolver, row) => resolver.IsGeneralActionAvailable(row),
            static (resolver, row) => resolver.IsGeneralActionReady(row));

        return actions;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<uint>> BuildStatusIds(ClientLanguage language)
    {
        var statusIds = new Dictionary<string, IReadOnlyList<uint>>(StringComparer.OrdinalIgnoreCase);
        var statuses = _dataManager.GetExcelSheet<LuminaStatus>(language);
        if (statuses is null)
        {
            return statusIds;
        }

        foreach (var status in statuses)
        {
            var name = NormalizeLookup(status.Name.ToString());
            if (name is null || status.RowId == 0)
            {
                continue;
            }

            if (!statusIds.TryGetValue(name, out var existing))
            {
                existing = new List<uint>();
                statusIds[name] = existing;
            }

            ((List<uint>)existing).Add(status.RowId);
        }

        return statusIds;
    }

    private static void AddActionRows<T>(
        IDictionary<string, IReadOnlyList<ActionLookup>> actions,
        IEnumerable<T>? sheet,
        Func<T, bool> shouldInclude,
        Func<T, string> getName,
        Func<ScriptRuntimeResolver, T, bool> isUnlocked,
        Func<ScriptRuntimeResolver, T, bool> isAvailable,
        Func<ScriptRuntimeResolver, T, bool> isReady)
        where T : struct
    {
        if (sheet is null)
        {
            return;
        }

        foreach (var row in sheet)
        {
            if (!shouldInclude(row))
            {
                continue;
            }

            var name = NormalizeLookup(getName(row));
            if (name is null)
            {
                continue;
            }

            var capturedRow = row;
            if (!actions.TryGetValue(name, out var existing))
            {
                existing = new List<ActionLookup>();
                actions[name] = existing;
            }

            // Several jobs can expose distinct action rows with the same displayed name.
            ((List<ActionLookup>)existing).Add(new ActionLookup(
                resolver => isUnlocked(resolver, capturedRow),
                resolver => isAvailable(resolver, capturedRow),
                resolver => isReady(resolver, capturedRow)));
        }
    }

    private static string? NormalizeLookup(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private bool IsPlayerActionUnlocked(LuminaAction action, bool accountForLevelSync)
    {
        if (!_clientState.IsLoggedIn || !_playerState.IsLoaded || action.IsPvP || !action.IsPlayerAction)
        {
            return false;
        }

        if (!IsCurrentJobMatch(action, nameof(LuminaAction.ClassJob), nameof(LuminaAction.ClassJobCategory)))
        {
            return false;
        }

        var currentLevel = accountForLevelSync ? _playerState.EffectiveLevel : _playerState.Level;
        if (action.ClassJobLevel != 0 && currentLevel < action.ClassJobLevel)
        {
            return false;
        }

        var unlockLink = GetRowId(action.UnlockLink);
        return unlockLink == 0 || _unlockState.IsUnlockLinkUnlocked(unlockLink);
    }

    private bool IsPlayerActionAvailable(LuminaAction action)
        => IsPlayerActionUnlocked(action, accountForLevelSync: true);

    private bool IsPlayerActionReady(LuminaAction action)
        => IsPlayerActionAvailable(action)
           && IsGameActionReady(ClientStructsActionType.Action, action.RowId, adjustPlayerAction: true);

    private bool IsCraftActionAvailable(CraftAction action)
    {
        if (!_clientState.IsLoggedIn || !_playerState.IsLoaded || !_unlockState.IsCraftActionUnlocked(action))
        {
            return false;
        }

        return IsCurrentJobMatch(action, nameof(CraftAction.ClassJob), nameof(CraftAction.ClassJobCategory))
            && MeetsRequiredLevel(action.ClassJobLevel);
    }

    private bool IsCraftActionReady(CraftAction action)
        => IsCraftActionAvailable(action)
           && IsGameActionReady(ClientStructsActionType.CraftAction, action.RowId);

    private bool IsGeneralActionAvailable(GeneralAction action)
    {
        if (!_clientState.IsLoggedIn || !_unlockState.IsGeneralActionUnlocked(action))
        {
            return false;
        }

        return TryGetReferencedRow(action, nameof(GeneralAction.Action), out LuminaAction linkedAction)
            ? !_playerState.IsLoaded || linkedAction.IsPvP || !linkedAction.IsPlayerAction
                ? false
                : IsCurrentJobMatch(linkedAction, nameof(LuminaAction.ClassJob), nameof(LuminaAction.ClassJobCategory))
                  && MeetsRequiredLevel(linkedAction.ClassJobLevel)
            : true;
    }

    private bool IsGeneralActionReady(GeneralAction action)
        => IsGeneralActionAvailable(action)
           && IsGameActionReady(ClientStructsActionType.GeneralAction, action.RowId);

    private bool IsCurrentJobMatch(object row, string classJobPropertyName, string classJobCategoryPropertyName)
    {
        if (!_playerState.IsLoaded)
        {
            return false;
        }

        var currentJobId = _playerState.ClassJob.RowId;
        var exactClassJobId = GetRowId(GetPropertyValue(row, classJobPropertyName));
        var category = GetPropertyValue(row, classJobCategoryPropertyName);
        if (GetRowId(category) == 0)
        {
            return exactClassJobId == 0 || exactClassJobId == currentJobId;
        }

        if (category is not null && TryGetCurrentJobCategoryFlag(out var flagName))
        {
            var categoryRow = GetPropertyValue(category, "Value") ?? GetPropertyValue(category, "ValueNullable");
            var allowed = categoryRow is not null ? GetPropertyValue(categoryRow, flagName) : null;
            if (allowed is not null)
            {
                return ConvertToBoolean(allowed);
            }
        }

        return exactClassJobId != 0 && exactClassJobId == currentJobId;
    }

    private bool MeetsRequiredLevel(byte requiredLevel)
        => _playerState.IsLoaded && (requiredLevel == 0 || _playerState.EffectiveLevel >= requiredLevel);

    private bool TryGetCurrentJobCategoryFlag(out string flagName)
    {
        flagName = string.Empty;
        if (!_playerState.IsLoaded)
        {
            return false;
        }

        var job = ElezenData.Jobs.GetById(_playerState.ClassJob.RowId, ClientLanguage.English);
        var abbreviation = NormalizeLookup(job?.Abbreviation);
        if (abbreviation is null)
        {
            return false;
        }

        flagName = abbreviation.ToUpperInvariant();
        return true;
    }

    private static bool TryGetReferencedRow<TRow>(object row, string propertyName, out TRow referencedRow)
        where TRow : struct
    {
        referencedRow = default;

        var rowRef = GetPropertyValue(row, propertyName);
        if (rowRef is null || GetRowId(rowRef) == 0)
        {
            return false;
        }

        var value = GetPropertyValue(rowRef, "Value") ?? GetPropertyValue(rowRef, "ValueNullable");
        if (value is not TRow typedValue)
        {
            return false;
        }

        referencedRow = typedValue;
        return true;
    }

    private static uint GetRowId(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return value switch
        {
            uint uintValue => uintValue,
            int intValue when intValue > 0 => (uint)intValue,
            ushort ushortValue => ushortValue,
            short shortValue when shortValue > 0 => (uint)shortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue when sbyteValue > 0 => (uint)sbyteValue,
            _ => GetRowId(
                GetPropertyValue(value, "RowId")
                ?? GetPropertyValue(value, "RowID")
                ?? GetPropertyValue(value, "Id")
                ?? GetPropertyValue(value, "Key")
                ?? GetPropertyValue(value, "Value")),
        };
    }

    private static bool ConvertToBoolean(object value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            byte byteValue => byteValue != 0,
            sbyte sbyteValue => sbyteValue != 0,
            short shortValue => shortValue != 0,
            ushort ushortValue => ushortValue != 0,
            int intValue => intValue != 0,
            uint uintValue => uintValue != 0,
            _ => false,
        };
    }

    private static object? GetPropertyValue(object target, string name)
        => target.GetType().GetProperty(name)?.GetValue(target);

    private unsafe bool IsGameActionReady(ClientStructsActionType actionType, uint actionId, bool adjustPlayerAction = false)
    {
        if (!_clientState.IsLoggedIn)
        {
            return false;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return false;
        }

        var resolvedActionId = adjustPlayerAction
            ? actionManager->GetAdjustedActionId(actionId)
            : actionId;

        if (!actionManager->IsActionOffCooldown(actionType, resolvedActionId))
        {
            return false;
        }

        if (actionManager->GetCurrentCharges(resolvedActionId) == 0)
        {
            return false;
        }

        return actionManager->CheckActionResources(actionType, resolvedActionId) == 0;
    }

    private JobData? GetCurrentJob()
        => _playerState.IsLoaded
            ? ElezenData.Jobs.GetById(_playerState.ClassJob.RowId, _clientState.ClientLanguage)
            : null;

    private string GetCurrentZoneName()
    {
        if (!_clientState.IsLoggedIn || _clientState.TerritoryType == 0)
        {
            return string.Empty;
        }

        return ElezenData.Locations.GetByTerritoryId(_clientState.TerritoryType, _clientState.ClientLanguage)?.Name
            ?? string.Empty;
    }

    private string GetCurrentWorldName()
    {
        if (!_playerState.IsLoaded)
        {
            return string.Empty;
        }

        return ElezenData.Worlds.GetById(_playerState.CurrentWorld.RowId, _clientState.ClientLanguage)?.Name
            ?? string.Empty;
    }

    private int GetPartySize()
    {
        if (_partyList.Length > 0)
        {
            return _partyList.Length;
        }

        return _playerState.IsLoaded ? 1 : 0;
    }

    private IGameObject? GetTarget()
    {
        var target = _targetManager.Target;
        return target is not null && target.IsValid() ? target : null;
    }

    private static int GetHpPercentage(ICharacter? character)
        => CalculatePercentage(character?.CurrentHp ?? 0, character?.MaxHp ?? 0);

    private static int GetMpPercentage(ICharacter? character)
        => CalculatePercentage(character?.CurrentMp ?? 0, character?.MaxMp ?? 0);

    private static int CalculatePercentage(uint current, uint max)
    {
        if (max == 0)
        {
            return 0;
        }

        var rounded = ((long)current * 100L + (max / 2)) / max;
        return (int)Math.Clamp(rounded, 0L, 100L);
    }

    private string GetCurrentRoleName()
    {
        var job = GetCurrentJob();
        if (job is null)
        {
            return string.Empty;
        }

        return job.Value.JobClass switch
        {
            JobClass.Tank => "tank",
            JobClass.Healer => "healer",
            JobClass.Dps => "dps",
            _ => "unknown",
        };
    }

    private int GetDistanceToTarget()
    {
        var localPlayer = _objectTable.LocalPlayer;
        var target = GetTarget();
        if (localPlayer is null || target is null)
        {
            return 9999;
        }

        var distance = Vector3.Distance(localPlayer.Position, target.Position) - localPlayer.HitboxRadius - target.HitboxRadius;
        return (int)Math.Max(0, MathF.Ceiling(distance));
    }

    private static string GetRequiredStringArgument(string functionName, IReadOnlyList<ExpressionValue> arguments)
    {
        if (arguments.Count != 1 || arguments[0] is not StringExpressionValue stringValue)
        {
            throw new InvalidOperationException($"{functionName}(...) requires a string argument.");
        }

        return stringValue.Value;
    }

    private static T GetRequiredNoArgumentValue<T>(string functionName, IReadOnlyList<ExpressionValue> arguments, Func<T> valueFactory)
    {
        if (arguments.Count != 0)
        {
            throw new InvalidOperationException($"{functionName}(...) does not take any arguments.");
        }

        return valueFactory();
    }

    private static JobData? FindJobByAbbreviation(string abbreviation, ClientLanguage language)
    {
        foreach (var job in ElezenData.Jobs.GetAll(language).Values)
        {
            if (string.Equals(job.Abbreviation, abbreviation, StringComparison.OrdinalIgnoreCase))
            {
                return job;
            }
        }

        return null;
    }
}
