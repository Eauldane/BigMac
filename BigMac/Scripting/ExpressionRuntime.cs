using System;
using System.Collections.Generic;

namespace BigMac.Scripting;

public interface IExpressionResolver
{
    bool TryResolveVariable(string name, out ExpressionValue value);
}

public interface IExpressionFunctionResolver
{
    bool TryEvaluateFunction(string name, IReadOnlyList<ExpressionValue> arguments, out ExpressionValue value);
}

public interface IScriptRuntimeResolver : IScriptConditionResolver, IExpressionResolver, IExpressionFunctionResolver
{
}

public static class ExpressionVariableDescriber
{
    private static readonly HashSet<string> BuiltInVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "job_abbr",
        "job_id",
        "job_name",
        "level",
        "hp_pct",
        "mp_pct",
        "player_name",
        "has_target",
        "target_name",
        "target_hp_pct",
        "zone_name",
        "world_name",
        "role",
        "party_size",
        "in_combat",
        "mounted",
        "gposing",
    };

    public static bool IsBuiltInVariableName(string name)
        => !string.IsNullOrWhiteSpace(name) && BuiltInVariables.Contains(name);

    public static string Describe(string name)
        => name.ToLowerInvariant() switch
        {
            "job_abbr" => "the current job abbreviation",
            "job_id" => "the current job id",
            "job_name" => "the current job name",
            "level" => "the current effective level",
            "hp_pct" => "the player's current hp percent",
            "mp_pct" => "the player's current mp percent",
            "player_name" => "the player's name",
            "has_target" => "whether the player currently has a target",
            "target_name" => "the current target's name",
            "target_hp_pct" => "the current target's hp percent",
            "zone_name" => "the current zone name",
            "world_name" => "the current world name",
            "role" => "the current role",
            "party_size" => "the current party size",
            "in_combat" => "whether the player is in combat",
            "mounted" => "whether the player is mounted",
            "gposing" => "whether the player is in group pose",
            _ => $"the value of {name}",
        };
}
