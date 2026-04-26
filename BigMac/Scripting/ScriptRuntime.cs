using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMac.Scripting;

public interface ICommandSink
{
    ValueTask ExecuteAsync(string commandText, CancellationToken cancellationToken);
}

public sealed class ScriptExecutor
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Random _random;
    private readonly IScriptRuntimeResolver _runtimeResolver;

    public ScriptExecutor(
        IScriptRuntimeResolver runtimeResolver,
        Random? random = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _runtimeResolver = runtimeResolver;
        _random = random ?? Random.Shared;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task ExecuteAsync(ScriptProgram program, ICommandSink commandSink, CancellationToken cancellationToken = default)
    {
        var scope = new ScopedRuntimeResolver(_runtimeResolver);
        foreach (var statement in program.Statements)
        {
            await ExecuteStatementAsync(statement, commandSink, scope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStatementAsync(
        ScriptStatement statement,
        ICommandSink commandSink,
        ScopedRuntimeResolver scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (statement)
        {
            case CommandStatement command:
                await commandSink.ExecuteAsync(command.Template.Render(_random, scope), cancellationToken).ConfigureAwait(false);
                break;
            case LetStatement binding:
                scope.SetValue(binding.Name, ExpressionEvaluator.Evaluate(binding.Value, _random, scope, scope));
                break;
            case WaitStatement wait:
                await _delayAsync(TimeSpan.FromSeconds(wait.Seconds), cancellationToken).ConfigureAwait(false);
                break;
            case ChanceStatement chance:
            {
                var branch = Roll(chance.Chance) ? chance.ThenBlock : chance.ElseBlock;
                await ExecuteBranchAsync(branch, commandSink, scope, cancellationToken).ConfigureAwait(false);
                break;
            }
            case IfStatement conditional:
            {
                var branch = ScriptConditionEvaluator.Evaluate(conditional.Condition, scope)
                    ? conditional.ThenBlock
                    : conditional.ElseBlock;
                await ExecuteBranchAsync(branch, commandSink, scope, cancellationToken).ConfigureAwait(false);
                break;
            }
            case LoopStatement loop:
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteBranchAsync(loop.Block, commandSink, scope, cancellationToken).ConfigureAwait(false);
                }
        }
    }

    private async Task ExecuteBranchAsync(
        ScriptBlock? block,
        ICommandSink commandSink,
        ScopedRuntimeResolver parentScope,
        CancellationToken cancellationToken)
    {
        if (block is null)
        {
            return;
        }

        var scope = parentScope.CreateChild();
        foreach (var nestedStatement in block.Statements)
        {
            await ExecuteStatementAsync(nestedStatement, commandSink, scope, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool Roll(ChanceCondition chance)
        => _random.NextDouble() < chance.Probability;
}

public sealed record ScriptPreviewNode(string Label, IReadOnlyList<ScriptPreviewNode> Children);

public static class ScriptPreviewTreeBuilder
{
    public static IReadOnlyList<ScriptPreviewNode> Build(ScriptProgram program)
        => program.Statements.Select(BuildStatement).ToList();

    private static ScriptPreviewNode BuildStatement(ScriptStatement statement)
        => statement switch
        {
            CommandStatement command => new ScriptPreviewNode($"command: {command.Template.RawText}", []),
            LetStatement binding => new ScriptPreviewNode($"let {binding.Name} = {ExpressionDescriber.Describe(binding.Value)}", []),
            WaitStatement wait => new ScriptPreviewNode($"wait {FormatSeconds(wait.Seconds)}s", []),
            ChanceStatement chance => BuildChance(chance),
            IfStatement conditional => BuildConditional(conditional),
            LoopStatement loop => BuildLoop(loop),
            _ => new ScriptPreviewNode("unknown", []),
        };

    private static string FormatSeconds(double seconds)
        => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static ScriptPreviewNode BuildChance(ChanceStatement chance)
    {
        var children = new List<ScriptPreviewNode>
        {
            BuildBranch("then", chance.ThenBlock),
        };

        if (chance.ElseBlock is not null)
        {
            children.Add(BuildBranch("else", chance.ElseBlock));
        }

        return new ScriptPreviewNode($"chance ({chance.Chance.DisplayText})", children);
    }

    private static ScriptPreviewNode BuildConditional(IfStatement conditional)
    {
        var children = new List<ScriptPreviewNode>
        {
            BuildBranch("then", conditional.ThenBlock),
        };

        if (conditional.ElseBlock is not null)
        {
            children.Add(BuildBranch("else", conditional.ElseBlock));
        }

        return new ScriptPreviewNode($"if {ScriptConditionDescriber.Describe(conditional.Condition)}", children);
    }

    private static ScriptPreviewNode BuildLoop(LoopStatement loop)
        => new("loop", [BuildBranch("body", loop.Block)]);

    private static ScriptPreviewNode BuildBranch(string name, ScriptBlock block)
    {
        var children = block.Statements.Count == 0
            ? [new ScriptPreviewNode("empty", [])]
            : block.Statements.Select(BuildStatement).ToList();

        return new ScriptPreviewNode(name, children);
    }
}

internal sealed class ScopedRuntimeResolver : IScriptRuntimeResolver
{
    private readonly IScriptRuntimeResolver _runtimeResolver;
    private readonly ScopedRuntimeResolver? _parent;
    private readonly Dictionary<string, ExpressionValue> _locals = new(StringComparer.OrdinalIgnoreCase);

    public ScopedRuntimeResolver(IScriptRuntimeResolver runtimeResolver, ScopedRuntimeResolver? parent = null)
    {
        _runtimeResolver = runtimeResolver;
        _parent = parent;
    }

    public ScopedRuntimeResolver CreateChild()
        => new(_runtimeResolver, this);

    public void SetValue(string name, ExpressionValue value)
    {
        if (!_locals.TryAdd(name, value))
        {
            throw new InvalidOperationException($"'{name}' is already declared in this block.");
        }
    }

    public bool TryResolveVariable(string name, out ExpressionValue value)
        => _locals.TryGetValue(name, out value!)
        || (_parent?.TryResolveVariable(name, out value!) ?? false)
        || _runtimeResolver.TryResolveVariable(name, out value!);

    public bool TryEvaluateFunction(string name, IReadOnlyList<ExpressionValue> arguments, out ExpressionValue value)
        => _runtimeResolver.TryEvaluateFunction(name, arguments, out value);

    public bool IsActionUnlocked(string actionName)
        => _runtimeResolver.IsActionUnlocked(actionName);

    public bool IsActionAvailable(string actionName)
        => _runtimeResolver.IsActionAvailable(actionName);

    public bool IsActionReady(string actionName)
        => _runtimeResolver.IsActionReady(actionName);

    public bool IsCurrentClass(string abbreviation)
        => _runtimeResolver.IsCurrentClass(abbreviation);

    public bool HasStatus(string statusName)
        => _runtimeResolver.HasStatus(statusName);

    public bool TargetHasStatus(string statusName)
        => _runtimeResolver.TargetHasStatus(statusName);

    public bool IsTargetEnemy()
        => _runtimeResolver.IsTargetEnemy();
}
