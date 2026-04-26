using BigMac.Scripting;
using Xunit;

namespace BigMac.Tests;

public sealed class CommandTemplateTests
{
    [Fact]
    public void Parse_Render_PreservesQuotedActionNames()
    {
        var template = CommandTemplate.Parse("/ac \"Medica II\"", 1, 1);

        var rendered = template.Render(Random.Shared, NullResolver.Instance);

        Assert.Equal("/ac \"Medica II\"", rendered);
    }

    [Fact]
    public void ActionUnlocked_UsesTheFullQuotedActionName()
    {
        var condition = ExpressionParser.Parse("action_unlocked(\"Medica II\")");
        var resolver = new RecordingConditionResolver();

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Medica II", resolver.LastUnlockedActionName);
    }

    [Fact]
    public void Condition_ActionReady_UsesTheFullQuotedActionName()
    {
        var condition = ExpressionParser.Parse("action_ready(\"Rescue\")");
        var resolver = new RecordingConditionResolver
        {
            ActionReadyResult = true,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Rescue", resolver.LastReadyActionName);
    }

    [Fact]
    public void Condition_HasStatus_UsesTheFullQuotedStatusName()
    {
        var condition = ExpressionParser.Parse("has_status(\"Swiftcast\")");
        var resolver = new RecordingConditionResolver
        {
            HasStatusResult = true,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Swiftcast", resolver.LastStatusName);
    }

    [Fact]
    public void Condition_HaveStatusAlias_UsesTheFullQuotedStatusName()
    {
        var condition = ExpressionParser.Parse("have_status(\"Poison\")");
        var resolver = new RecordingConditionResolver
        {
            HasStatusResult = true,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Poison", resolver.LastStatusName);
    }

    [Fact]
    public void Condition_TargetHasStatus_UsesTheFullQuotedStatusName()
    {
        var condition = ExpressionParser.Parse("target_has_status(\"Vulnerability Up\")");
        var resolver = new RecordingConditionResolver
        {
            TargetHasStatusResult = true,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Vulnerability Up", resolver.LastTargetStatusName);
    }

    [Fact]
    public async Task ScriptExecutor_BareIfCondition_Works()
    {
        var program = ScriptParser.Parse("""
            if have_status("Poison") {
                /ac "Esuna"
            }
            """);
        var resolver = new RecordingConditionResolver
        {
            HasStatusResult = true,
        };
        var sink = new RecordingCommandSink();

        await new ScriptExecutor(resolver, new Random(0)).ExecuteAsync(program, sink, TestContext.Current.CancellationToken);

        Assert.Equal(["/ac \"Esuna\""], sink.Commands);
        Assert.Equal("Poison", resolver.LastStatusName);
    }

    [Fact]
    public void Condition_VariableExpression_UsesTruthiness()
    {
        var condition = ExpressionParser.Parse("in_combat");
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["in_combat"] = new BooleanExpressionValue(true),
            },
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
    }

    [Theory]
    [InlineData("distance_to_target() < 10", false)]
    [InlineData("distance_to_target() <= 10", true)]
    [InlineData("distance_to_target() == 10", true)]
    [InlineData("distance_to_target() != 10", false)]
    [InlineData("distance_to_target() > 10", false)]
    [InlineData("distance_to_target() >= 10", true)]
    public void Condition_FunctionComparisons_Work(string expression, bool expected)
    {
        var condition = ExpressionParser.Parse(expression);
        var resolver = new RecordingConditionResolver
        {
            DistanceToTargetResult = 10,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("party_size < 4", false)]
    [InlineData("party_size <= 4", true)]
    [InlineData("party_size == 4", true)]
    [InlineData("party_size != 4", false)]
    [InlineData("party_size > 4", false)]
    [InlineData("party_size >= 4", true)]
    public void Condition_NumberComparisons_Work(string expression, bool expected)
    {
        var condition = ExpressionParser.Parse(expression);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["party_size"] = new NumberExpressionValue(4),
            },
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Condition_StringEquality_IsCaseInsensitive()
    {
        var condition = ExpressionParser.Parse("world_name == \"balmung\"");
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["world_name"] = new StringExpressionValue("Balmung"),
            },
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
    }

    [Fact]
    public void Condition_BooleanLiteralComparisons_Work()
    {
        var condition = ExpressionParser.Parse("mounted == false");
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["mounted"] = new BooleanExpressionValue(false),
            },
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
    }

    [Fact]
    public void Condition_ZeroArgumentFunctions_Work()
    {
        var condition = ExpressionParser.Parse("target_is_enemy()");
        var resolver = new RecordingConditionResolver
        {
            TargetIsEnemyResult = true,
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
    }

    [Fact]
    public void Condition_FunctionsCanUseStringVariables()
    {
        var condition = ExpressionParser.Parse("action_ready(action_name)");
        var resolver = new RecordingConditionResolver
        {
            ActionReadyResult = true,
            Variables =
            {
                ["action_name"] = new StringExpressionValue("Rescue"),
            },
        };

        var result = ScriptConditionEvaluator.Evaluate(condition, resolver);

        Assert.True(result);
        Assert.Equal("Rescue", resolver.LastReadyActionName);
    }

    [Fact]
    public void Parse_Render_CanUseComparisonExpressionsAsBareArguments()
    {
        var template = CommandTemplate.Parse("/echo world_name == \"balmung\"", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["world_name"] = new StringExpressionValue("Balmung"),
            },
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo true", rendered);
    }

    [Fact]
    public void Parse_Render_CanUseFunctionExpressionsAsBareArguments()
    {
        var template = CommandTemplate.Parse("/echo distance_to_target()", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            DistanceToTargetResult = 12,
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo 12", rendered);
    }

    [Fact]
    public void Parse_Render_ChooseFunction_ShortCircuitsUnusedBranch()
    {
        var template = CommandTemplate.Parse("/echo ${choose(true, \"ready\", missing_value)}", 1, 1);

        var rendered = template.Render(Random.Shared, NullResolver.Instance);

        Assert.Equal("/echo ready", rendered);
    }

    [Fact]
    public void Parse_Render_NewNumericFunctions_Work()
    {
        var template = CommandTemplate.Parse("/echo ${min(hp_pct, mp_pct)} ${max([hp_pct, mp_pct, distance_to_target()])} ${clamp(distance_to_target(), 0, 10)}", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            DistanceToTargetResult = 12,
            Variables =
            {
                ["hp_pct"] = new NumberExpressionValue(42),
                ["mp_pct"] = new NumberExpressionValue(17),
            },
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo 17 42 10", rendered);
    }

    [Fact]
    public void Parse_Render_NewTextFunctions_Work()
    {
        var template = CommandTemplate.Parse("/echo ${contains(lower(zone_name), 'limsa')} ${upper(world_name)}", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["zone_name"] = new StringExpressionValue("Limsa Lominsa"),
                ["world_name"] = new StringExpressionValue("Balmung"),
            },
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo true BALMUNG", rendered);
    }

    [Fact]
    public void Parse_Render_LowRiskUtilityFunctions_Work()
    {
        var template = CommandTemplate.Parse("/echo ${starts_with(zone_name, \"limsa\")} ${ends_with(world_name, \"MUNG\")} ${trim(\"  ready  \")} ${default(target_name, \"none\")}", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["zone_name"] = new StringExpressionValue("Limsa Lominsa"),
                ["world_name"] = new StringExpressionValue("Balmung"),
                ["target_name"] = new StringExpressionValue(string.Empty),
            },
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo true true ready none", rendered);
    }

    [Fact]
    public void Parse_Render_DefaultFunction_ShortCircuitsFallback()
    {
        var template = CommandTemplate.Parse("/echo ${default(player_name, missing_value)}", 1, 1);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["player_name"] = new StringExpressionValue("Alice"),
            },
        };

        var rendered = template.Render(Random.Shared, resolver);

        Assert.Equal("/echo Alice", rendered);
    }

    [Fact]
    public void Parse_Render_BooleanLogic_UsesPrecedenceAndShortCircuiting()
    {
        var template = CommandTemplate.Parse("/echo ${true || false && missing_value} ${false && missing_value} ${!false}", 1, 1);

        var rendered = template.Render(Random.Shared, NullResolver.Instance);

        Assert.Equal("/echo true false true", rendered);
    }

    [Fact]
    public void Condition_Validation_AllowsNewExpressionFunctions()
    {
        var containsExpression = ExpressionParser.Parse("contains(lower(world_name), \"balm\")");
        var mathExpression = ExpressionParser.Parse("clamp(min(hp_pct, mp_pct), 0, 100) < max(30, 40)");
        var chooseExpression = ExpressionParser.Parse("choose(has_target, true, false)");
        var utilityExpression = ExpressionParser.Parse("starts_with(trim(zone_name), \"limsa\") && default(target_name, \"none\") != \"none\"");
        var booleanExpression = ExpressionParser.Parse("!mounted || has_target && target_is_enemy()");

        var exception = Record.Exception(() =>
        {
            ScriptConditionEvaluator.Validate(containsExpression);
            ScriptConditionEvaluator.Validate(mathExpression);
            ScriptConditionEvaluator.Validate(chooseExpression);
            ScriptConditionEvaluator.Validate(utilityExpression);
            ScriptConditionEvaluator.Validate(booleanExpression);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task ScriptExecutor_BooleanLogic_Works()
    {
        var program = ScriptParser.Parse("""
            if (has_target && !target_is_enemy() || mounted) {
                /echo Safe target state.
            }
            """);
        var resolver = new RecordingConditionResolver
        {
            TargetIsEnemyResult = false,
            Variables =
            {
                ["has_target"] = new BooleanExpressionValue(true),
                ["mounted"] = new BooleanExpressionValue(false),
            },
        };
        var sink = new RecordingCommandSink();

        await new ScriptExecutor(resolver, new Random(0)).ExecuteAsync(program, sink, TestContext.Current.CancellationToken);

        Assert.Equal(["/echo Safe target state."], sink.Commands);
    }

    [Fact]
    public async Task ScriptExecutor_LetBindings_CanBeReusedAcrossStatements()
    {
        var program = ScriptParser.Parse("""
            let message = choose(has_target, target_name, "No target")

            if (message == "Tank") {
                /echo ${message}
            }

            /p ${message}
            """);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["has_target"] = new BooleanExpressionValue(true),
                ["target_name"] = new StringExpressionValue("Tank"),
            },
        };
        var sink = new RecordingCommandSink();

        await new ScriptExecutor(resolver, new Random(0)).ExecuteAsync(program, sink, TestContext.Current.CancellationToken);

        Assert.Equal(["/echo Tank", "/p Tank"], sink.Commands);
    }

    [Fact]
    public async Task ScriptExecutor_LetBindings_AreScopedToTheirBlock()
    {
        var program = ScriptParser.Parse("""
            let label = "outer"

            if (has_target) {
                let label = "inner"
                /echo ${label}
            }

            /echo ${label}
            """);
        var resolver = new RecordingConditionResolver
        {
            Variables =
            {
                ["has_target"] = new BooleanExpressionValue(true),
            },
        };
        var sink = new RecordingCommandSink();

        await new ScriptExecutor(resolver, new Random(0)).ExecuteAsync(program, sink, TestContext.Current.CancellationToken);

        Assert.Equal(["/echo inner", "/echo outer"], sink.Commands);
    }

    [Fact]
    public void ScriptParser_LoopStatement_ParsesBlock()
    {
        var program = ScriptParser.Parse("""
            loop {
                /echo tick
                wait(1.2)
            }
            """);

        var loop = Assert.IsType<LoopStatement>(Assert.Single(program.Statements));
        Assert.Equal(2, loop.Block.Statements.Count);
    }

    [Fact]
    public void ScriptStructureInspector_ContainsLoop_FindsNestedLoopBlocks()
    {
        var program = ScriptParser.Parse("""
            if (true) {
                loop {
                    wait(1)
                }
            }
            """);

        Assert.True(ScriptStructureInspector.ContainsLoop(program));
    }

    [Fact]
    public void ScriptParser_WaitStatement_ParsesDecimalSeconds()
    {
        var program = ScriptParser.Parse("wait(1.2)");

        var wait = Assert.IsType<WaitStatement>(Assert.Single(program.Statements));
        Assert.InRange(wait.Seconds, 1.199, 1.201);
    }

    [Theory]
    [InlineData("wait()")]
    [InlineData("wait(nope)")]
    [InlineData("wait(-1)")]
    public void ScriptParser_WaitStatement_RejectsInvalidDurations(string source)
    {
        Assert.Throws<ScriptParseException>(() => ScriptParser.Parse(source));
    }

    [Fact]
    public async Task ScriptExecutor_WaitStatement_PausesBeforeNextStatement()
    {
        var program = ScriptParser.Parse("""
            /echo before
            wait(1.2)
            /echo after
            """);
        var sink = new RecordingCommandSink();
        var delays = new List<TimeSpan>();
        var executor = new ScriptExecutor(
            NullResolver.Instance,
            new Random(0),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await executor.ExecuteAsync(program, sink, TestContext.Current.CancellationToken);

        Assert.Equal(["/echo before", "/echo after"], sink.Commands);
        var delay = Assert.Single(delays);
        Assert.InRange(delay.TotalSeconds, 1.199, 1.201);
    }

    [Fact]
    public async Task ScriptExecutor_LoopStatement_RepeatsUntilCancelled()
    {
        var program = ScriptParser.Parse("""
            loop {
                /echo tick
                wait(1.2)
            }
            """);
        var sink = new RecordingCommandSink();
        var delays = new List<TimeSpan>();
        using var cancellationTokenSource = new CancellationTokenSource();
        var executor = new ScriptExecutor(
            NullResolver.Instance,
            new Random(0),
            (duration, _) =>
            {
                delays.Add(duration);
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(program, sink, cancellationTokenSource.Token));

        Assert.Equal(["/echo tick"], sink.Commands);
        var delay = Assert.Single(delays);
        Assert.InRange(delay.TotalSeconds, 1.199, 1.201);
    }

    [Fact]
    public void ScriptParser_RejectsDuplicateLetBindingsInTheSameBlock()
    {
        var exception = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            let message = "one"
            let message = "two"
            """));

        Assert.Contains("already declared in this block", exception.Message);
    }

    [Fact]
    public void ScriptParser_RejectsBuiltInVariableBindings()
    {
        var exception = Assert.Throws<ScriptParseException>(() => ScriptParser.Parse("""
            let world_name = "Balmung"
            /echo ${world_name}
            """));

        Assert.Contains("built-in variable name", exception.Message);
    }

    private sealed class NullResolver : IScriptRuntimeResolver
    {
        public static readonly NullResolver Instance = new();

        public bool TryResolveVariable(string name, out ExpressionValue value)
        {
            value = null!;
            return false;
        }

        public bool IsActionUnlocked(string actionName)
            => false;

        public bool IsActionAvailable(string actionName)
            => false;

        public bool IsActionReady(string actionName)
            => false;

        public bool IsCurrentClass(string abbreviation)
            => false;

        public bool HasStatus(string statusName)
            => false;

        public bool TargetHasStatus(string statusName)
            => false;

        public bool IsTargetEnemy()
            => false;

        public bool TryEvaluateFunction(string name, IReadOnlyList<ExpressionValue> arguments, out ExpressionValue value)
        {
            value = null!;
            return false;
        }
    }

    private sealed class RecordingConditionResolver : IScriptRuntimeResolver
    {
        public Dictionary<string, ExpressionValue> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? LastUnlockedActionName { get; private set; }

        public string? LastReadyActionName { get; private set; }

        public string? LastStatusName { get; private set; }

        public string? LastTargetStatusName { get; private set; }

        public bool ActionAvailableResult { get; set; }

        public bool ActionReadyResult { get; set; }

        public bool CurrentClassResult { get; set; }

        public bool HasStatusResult { get; set; }

        public bool TargetHasStatusResult { get; set; }

        public bool TargetIsEnemyResult { get; set; }

        public int DistanceToTargetResult { get; set; }

        public bool IsActionUnlocked(string actionName)
        {
            LastUnlockedActionName = actionName;
            return true;
        }

        public bool IsActionAvailable(string actionName)
            => ActionAvailableResult;

        public bool IsActionReady(string actionName)
        {
            LastReadyActionName = actionName;
            return ActionReadyResult;
        }

        public bool IsCurrentClass(string abbreviation)
            => CurrentClassResult;

        public bool HasStatus(string statusName)
        {
            LastStatusName = statusName;
            return HasStatusResult;
        }

        public bool TargetHasStatus(string statusName)
        {
            LastTargetStatusName = statusName;
            return TargetHasStatusResult;
        }

        public bool IsTargetEnemy()
            => TargetIsEnemyResult;

        public bool TryEvaluateFunction(string name, IReadOnlyList<ExpressionValue> arguments, out ExpressionValue value)
        {
            switch (name.ToLowerInvariant())
            {
                case "action_unlocked":
                    value = new BooleanExpressionValue(IsActionUnlocked(GetStringArgument(arguments)));
                    return true;
                case "action_available":
                    value = new BooleanExpressionValue(IsActionAvailable(GetStringArgument(arguments)));
                    return true;
                case "action_ready":
                    value = new BooleanExpressionValue(IsActionReady(GetStringArgument(arguments)));
                    return true;
                case "class":
                case "job":
                    value = new BooleanExpressionValue(IsCurrentClass(GetStringArgument(arguments)));
                    return true;
                case "has_status":
                case "have_status":
                    value = new BooleanExpressionValue(HasStatus(GetStringArgument(arguments)));
                    return true;
                case "target_has_status":
                    value = new BooleanExpressionValue(TargetHasStatus(GetStringArgument(arguments)));
                    return true;
                case "target_is_enemy":
                    EnsureNoArguments(name, arguments);
                    value = new BooleanExpressionValue(IsTargetEnemy());
                    return true;
                case "distance_to_target":
                    EnsureNoArguments(name, arguments);
                    value = new NumberExpressionValue(DistanceToTargetResult);
                    return true;
                default:
                    value = null!;
                    return false;
            }
        }

        public bool TryResolveVariable(string name, out ExpressionValue value)
            => Variables.TryGetValue(name, out value!);

        private static string GetStringArgument(IReadOnlyList<ExpressionValue> arguments)
        {
            if (arguments.Count != 1)
            {
                throw new InvalidOperationException("Expected a single string argument.");
            }

            return Assert.IsType<StringExpressionValue>(arguments[0]).Value;
        }

        private static void EnsureNoArguments(string functionName, IReadOnlyList<ExpressionValue> arguments)
        {
            if (arguments.Count != 0)
            {
                throw new InvalidOperationException($"{functionName}(...) should not receive arguments.");
            }
        }
    }

    private sealed class RecordingCommandSink : ICommandSink
    {
        public List<string> Commands { get; } = [];

        public ValueTask ExecuteAsync(string commandText, CancellationToken cancellationToken)
        {
            Commands.Add(commandText);
            return ValueTask.CompletedTask;
        }
    }
}
