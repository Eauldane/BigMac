using System;

namespace BigMac.Scripting;

public interface IScriptConditionResolver
{
    bool IsActionUnlocked(string actionName);

    bool IsActionAvailable(string actionName);

    bool IsActionReady(string actionName);

    bool IsCurrentClass(string abbreviation);

    bool HasStatus(string statusName);

    bool TargetHasStatus(string statusName);

    bool IsTargetEnemy();
}

public static class ScriptConditionEvaluator
{
    public static void Validate(ExpressionNode expression)
        => ValidateExpression(expression);

    public static bool Evaluate(ExpressionNode expression, IScriptRuntimeResolver resolver)
        => ExpressionEvaluator.ToBoolean(ExpressionEvaluator.Evaluate(expression, Random.Shared, resolver, resolver));

    internal static bool TryEvaluatePredicate(FunctionCallExpression function, IScriptConditionResolver resolver, out bool result)
    {
        result = false;
        if (!IsPredicateFunction(function.Name))
        {
            return false;
        }

        result = function.Name.ToLowerInvariant() switch
        {
            "action_unlocked" => resolver.IsActionUnlocked(GetSingleStringArgument(function)),
            "action_available" => resolver.IsActionAvailable(GetSingleStringArgument(function)),
            "action_ready" => resolver.IsActionReady(GetSingleStringArgument(function)),
            "class" or "job" => resolver.IsCurrentClass(GetSingleStringArgument(function)),
            "has_status" or "have_status" => resolver.HasStatus(GetSingleStringArgument(function)),
            "target_has_status" => resolver.TargetHasStatus(GetSingleStringArgument(function)),
            "target_is_enemy" => resolver.IsTargetEnemy(),
            _ => throw new InvalidOperationException($"Unsupported condition '{function.Name}'."),
        };

        return true;
    }

    private static bool IsPredicateFunction(string name)
        => name.ToLowerInvariant() is
            "action_unlocked" or
            "action_available" or
            "action_ready" or
            "class" or
            "job" or
            "has_status" or
            "have_status" or
            "target_has_status" or
            "target_is_enemy";

    private static string GetSingleStringArgument(FunctionCallExpression function)
    {
        if (function.Arguments.Count != 1 || function.Arguments[0] is not StringLiteralExpression stringLiteral)
        {
            throw new InvalidOperationException($"{function.Name}(...) requires a single quoted string argument.");
        }

        return stringLiteral.Value;
    }

    private static void ValidateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case StringLiteralExpression:
            case NumberLiteralExpression:
            case BooleanLiteralExpression:
            case VariableExpression:
                return;
            case ArrayExpression array:
                foreach (var item in array.Items)
                {
                    ValidateExpression(item);
                }

                return;
            case UnaryExpression unary:
                ValidateExpression(unary.Operand);
                return;
            case BinaryExpression binary:
                ValidateExpression(binary.Left);
                ValidateExpression(binary.Right);
                return;
            case LogicalExpression logical:
                ValidateExpression(logical.Left);
                ValidateExpression(logical.Right);
                return;
            case FunctionCallExpression function:
                ValidateFunction(function);
                return;
            default:
                throw new InvalidOperationException("Conditions must use a supported predicate, variable, or comparison.");
        }
    }

    private static void ValidateFunction(FunctionCallExpression function)
    {
        if (TryGetSupportedFunctionArity(function.Name, out var arity))
        {
            ValidateArgumentCount(function, arity);
            foreach (var argument in function.Arguments)
            {
                ValidateExpression(argument);
            }

            return;
        }

        if (TryValidateBuiltInExpressionFunction(function))
        {
            return;
        }

        throw new InvalidOperationException($"Unsupported condition '{function.Name}'.");
    }

    private static bool TryValidateBuiltInExpressionFunction(FunctionCallExpression function)
    {
        switch (function.Name.ToLowerInvariant())
        {
            case "random":
            case "min":
            case "max":
                ValidateMinimumArgumentCount(function, 1);
                break;
            case "choose":
            case "clamp":
                ValidateArgumentCount(function, 3);
                break;
            case "contains":
            case "starts_with":
            case "ends_with":
            case "default":
                ValidateArgumentCount(function, 2);
                break;
            case "lower":
            case "upper":
            case "trim":
                ValidateArgumentCount(function, 1);
                break;
            default:
                return false;
        }

        foreach (var argument in function.Arguments)
        {
            ValidateExpression(argument);
        }

        return true;
    }

    private static bool TryGetSupportedFunctionArity(string name, out int arity)
    {
        arity = name.ToLowerInvariant() switch
        {
            "action_unlocked" or
            "action_available" or
            "action_ready" or
            "class" or
            "job" or
            "has_status" or
            "have_status" or
            "target_has_status" => 1,
            "target_is_enemy" or "distance_to_target" => 0,
            _ => -1,
        };

        return arity >= 0;
    }

    private static void ValidateArgumentCount(FunctionCallExpression function, int expectedCount)
    {
        if (function.Arguments.Count != expectedCount)
        {
            throw new InvalidOperationException(
                expectedCount == 0
                    ? $"{function.Name}(...) does not take any arguments."
                    : $"{function.Name}(...) requires exactly {expectedCount} argument{(expectedCount == 1 ? string.Empty : "s")}.");
        }
    }

    private static void ValidateMinimumArgumentCount(FunctionCallExpression function, int minimumCount)
    {
        if (function.Arguments.Count < minimumCount)
        {
            throw new InvalidOperationException(
                $"{function.Name}(...) requires at least {minimumCount} argument{(minimumCount == 1 ? string.Empty : "s")}.");
        }
    }
}

public static class ScriptConditionDescriber
{
    public static string Describe(ExpressionNode expression)
        => expression is FunctionCallExpression function && IsSupportedConditionFunction(function.Name)
            ? DescribeFunction(function)
            : ExpressionDescriber.Describe(expression);

    private static string DescribeFunction(FunctionCallExpression function)
    {
        return function.Name.ToLowerInvariant() switch
        {
            "action_unlocked" => DescribeSingleArgument(function, "action unlocked"),
            "action_available" => DescribeSingleArgument(function, "action available"),
            "action_ready" => DescribeSingleArgument(function, "action ready"),
            "class" or "job" => DescribeSingleArgument(function, "class is"),
            "has_status" or "have_status" => DescribeSingleArgument(function, "has status"),
            "target_has_status" => DescribeSingleArgument(function, "target has status"),
            "target_is_enemy" => "target is an enemy",
            "distance_to_target" => "distance to target",
            _ => $"condition {function.Name}(...)",
        };
    }

    private static string DescribeSingleArgument(FunctionCallExpression function, string prefix)
        => function.Arguments.Count == 1
            ? $"{prefix} {ExpressionDescriber.Describe(function.Arguments[0])}"
            : $"condition {function.Name}(...)";

    private static bool IsSupportedConditionFunction(string name)
        => name.ToLowerInvariant() is
            "action_unlocked" or
            "action_available" or
            "action_ready" or
            "class" or
            "job" or
            "has_status" or
            "have_status" or
            "target_has_status" or
            "target_is_enemy" or
            "distance_to_target";

    private static string Quote(string value)
        => $"\"{value}\"";
}
