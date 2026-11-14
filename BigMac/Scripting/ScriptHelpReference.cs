using System.Collections.Generic;

namespace BigMac.Scripting;

public sealed record ScriptHelpSection(string Title, string Summary, IReadOnlyList<ScriptHelpEntry> Entries);

public sealed record ScriptHelpEntry(string Name, string Description, string? Syntax = null, string? Example = null);

public static class ScriptHelpReference
{
    public static IReadOnlyList<ScriptHelpSection> Sections { get; } =
    [
        new ScriptHelpSection(
            "Basics",
            "Welcome to BigMac!\n\nBigMac aims to extend your macro scripting capaibilities beyond what the game ordinarily allows.\n\n" +
            "BigMac will run from top to bottom. Each executable line must be a conditional, statement, or command, with comments using double slashes.\n\n" +
            "Refer to this guide when you're stuck!",
            [
                new ScriptHelpEntry(
                    "Slash commands",
                    "Any line that starts with '/' is treated as a command. BigMac renders any expressions first, then executes the finished command text.\n\n" +
                    "As a note; actions that are forcibly upgraded by class unlocks will remain upgraded. For example, a White Mage who has Medica III unlocked will always cast" +
                    " Medica III if /ac Medica II is called, unless level sync takes them below the unlock point for it.  ",
                    "/command arguments",
                    """
                    /ac Cure
                    /say Stand still! I'm trying to heal you, not bloody tickle you!
                    """),
                new ScriptHelpEntry(
                    "Comments",
                    "Use '//' for comments. Comments run to the end of the current line and are ignored by the parser.",
                    "// this is a comment",
                    """
                    chance (10) { // 10 percent chance
                        /echo Lucky you! 
                    }
                    """),
                new ScriptHelpEntry(
                    "Wait statements",
                    "Use wait(...) to pause script execution for a number of seconds. Decimal durations are supported.",
                    "wait(1.5)",
                    """
                    /ac "Heavy Swing"
                    /echo Waiting for cooldown.
                    wait(1.5)
                    /ac "Maim"
                    """),
                new ScriptHelpEntry(
                    "Loop blocks",
                    "Use loop { ... } to repeat a block until it is cancelled, or repeat(count) { ... } to run a block a fixed number of times. Running the same macro again cancels its active execution, and moving also stops looping macros, including bounded repeats.",
                    "loop { ... } or repeat(3) { ... }",
                    """
                    loop {
                        /ac "Sprint"
                        wait(1)
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Conditionals",
            "If/else blocks run only when a state check succeeds. Conditions can use built-in predicates like class(...), state functions like have_status(...), named variables, comparison expressions, or boolean logic with &&, ||, and !. The outer condition parentheses are optional. Else blocks are optional, and run when the initial if check fails.",
            [
                new ScriptHelpEntry(
                    "Action unlock checks",
                    "Use action_unlocked(\"...\") to check whether a named action is learned or otherwise unlocked. BigMac resolves the name in the client language first, then falls back to English. This does not account for temporary loss from level sync, so a level 100 WHM synced to 50 still counts Medica III as unlocked. Use action_available(...) if you also need current job and effective level checks.\n\n" +
                    "If this check is used and the player is underlevelled to use the checked action, the chatbox will display an error about an invalid action.",
                    "if (action_unlocked(\"Rescue\")) { ... }",
                    """
                    if (action_unlocked("Rescue")) {
                        /ac Rescue <2>
                        /p Bad tank! Sit!
                    }
                    """),
                new ScriptHelpEntry(
                    "Action availability checks",
                    "Use action_available(...) to check whether a named action is currently available on your active job at your effective level. BigMac resolves the name in the client language first, then falls back to English. This accounts for level sync and current class or job restrictions, but it does not check cooldowns, charges, target validity, range, or other moment-to-moment casting restrictions.\n\n" +
                    "As noted previously, forcibly upgraded actions will automatically cast if the lesser version is called. It may be useful to check the inferior version in these cases.",
                    "if (action_available(\"Medica II\")) { ... }",
                    """
                    if (action_available("Medica II")) {
                        /ac "Medica III" <me>
                    } else {
                        /echo Medica III is not currently available.
                    }
                    """),
                new ScriptHelpEntry(
                    "Action readiness checks",
                    "Use action_ready(...) to check whether a named action is currently available, off cooldown, and has the required resources. This does not check whether your current target is valid, in range, or in line of sight, so combine it with target checks when needed.",
                    "if (action_ready(\"Rescue\")) { ... }",
                    """
                    if (action_ready("Rescue")) {
                        /ac Rescue <2>
                    } else {
                        /echo Rescue is not ready yet.
                    }
                    """),
                new ScriptHelpEntry(
                    "Class checks",
                    "Use class(\"...\") with a class or job abbreviation such as WHM or PLD. BigMac resolves the abbreviation in the client language first, then falls back to English.",
                    "if (class(\"WHM\")) { ... } else { ... }",
                    """
                    if (class("WHM")) {
                        /ac Cure <t>
                    } else {
                        /echo Switch to WHM first.
                    }
                    """),
                new ScriptHelpEntry(
                    "Status checks",
                    "Use have_status(\"...\") to check whether your current player character has a named status effect. BigMac resolves the name in the client language first, then falls back to English. has_status(...) is also accepted.",
                    "if have_status(\"Poison\") { ... }",
                    """
                    if have_status("Poison") {
                        /ac "Esuna"
                    } else {
                        /echo No poison detected.
                    }
                    """),
                new ScriptHelpEntry(
                    "Target status checks",
                    "Use target_has_status(\"...\") to check whether your current target has a named status effect. This returns false when you have no valid target. BigMac resolves the name in the client language first, then falls back to English.",
                    "if target_has_status(\"Vulnerability Up\") { ... }",
                    """
                    if target_has_status("Vulnerability Up") {
                        /echo Target has vuln.
                    }
                    """),
                new ScriptHelpEntry(
                    "Variable conditions",
                    "Any named variable can be used directly inside if(...). Booleans use their actual value, numbers are true when non-zero, strings are true when non-empty, and arrays are true when they contain at least one item.",
                    "if (in_combat) { ... }",
                    """
                    if (target_name) {
                        /echo Current target: ${target_name}
                    } else {
                        /echo You do not have a target.
                    }
                    """),
                new ScriptHelpEntry(
                    "Comparison operators",
                    "Use <, >, ==, !=, <=, and >= inside conditions or other expressions. Numeric comparisons use numeric order. String comparisons are case-insensitive, so world_name == \"balmung\" works when your current world is Balmung.",
                    "if (party_size >= 4) { ... }",
                    """
                    if (world_name == "balmung") {
                        /echo Balmung detected. I know what you are. 
                    }

                    if (party_size >= 4) {
                        /p Full party check passed.
                    }
                    """),
                new ScriptHelpEntry(
                    "Boolean logic",
                    "Use && for and, || for or, and ! for not. && is evaluated before ||, and parentheses can be used to group checks.",
                    """
                    condition && condition
                    condition || condition
                    !condition
                    """,
                    """
                    if (has_target && target_is_enemy() && distance_to_target() <= 15) {
                        /echo Enemy target is in range.
                    }

                    if (!mounted && !in_combat) {
                        /mount "Company Chocobo"
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Chance Blocks",
            "Chance blocks choose whether a nested block runs. Blocks may be nested, and else blocks are optional.",
            [
                new ScriptHelpEntry(
                    "Percent chance",
                    "Use a whole-number percentage from 0 to 100.",
                    "chance (50) { ... }",
                    """
                    /p gg, tyfp!
                    chance (50) {
                        /bow
                    }
                    """),
                new ScriptHelpEntry(
                    "Ratio chance",
                    "Use \"left in right\" for odds like 1 in 10000. Both numbers must be positive, and the first number cannot exceed the second.",
                    "chance (1 in 10000) { ... }",
                    """
                    /p It's okay, I'm sure I'll get it the next time it drops...
                    chance (1 in 25) {
                        /sulk
                    }
                    """),
                new ScriptHelpEntry(
                    "Else branch and nesting",
                    "An else block runs when the chance roll fails. Blocks may contain more commands or more chance blocks.",
                    "chance (...) { ... } else { ... }",
                    """
                    chance (25) {
                        /echo Lucky you!
                        chance (50) {
                            /echo Even luckier you, you got a bonus!
                        }
                    } else {
                        /echo Aww. 
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Loops",
            "Use loop for indefinite repetition or repeat(count) for a bounded number of iterations. Re-running the same looping macro toggles it off, and movement cancels both kinds. These cancellation rules apply to any macro containing a loop or repeat block, including commands before or after that block.",
            [
                new ScriptHelpEntry(
                    "Bounded repetition",
                    "repeat(count) runs its block exactly count times, then continues with the next statement. The count is evaluated once when the block is reached and must be a non-negative whole number. Zero will skip the block for if you're doing randomisation. Numeric expressions and local bindings are supported, but text such as arg(1) is not automatically converted to a number. Each iteration has a fresh local scope and can read outer bindings and macro arguments. Repeats can be nested and contain if/else, chance, waits, or other loops. Execution yields between iterations, but this does not add a timed pause; include wait(...) to space out commands.",
                    "repeat(count) { ... }",
                    """
                    let count = choose(arg(1) == "short", 2, 5)
                    repeat(count) {
                        /echo Tick.
                        wait(1)
                    }
                    /echo Finished.
                    """),
                new ScriptHelpEntry(
                    "Basic loop",
                    "Use loop { ... } to repeat commands. In practice you should include wait(...) inside the loop body so it does not hammer commands continuously.",
                    "loop { ... }",
                    """
                    loop {
                        /echo Tick.
                        wait(2)
                    }
                    """),
                new ScriptHelpEntry(
                    "Loop with conditions",
                    "Loop bodies can contain the same statements as any other block, including if/else and chance blocks.",
                    """
                    loop {
                        ...
                    }
                    """,
                    """
                    loop {
                        if (target_is_enemy()) {
                            /ac Provoke <t>
                        } else {
                            /echo Waiting for an enemy target.
                        }

                        wait(1)
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Expressions",
            "Expressions can be embedded inside a command with ${...}, or used as the entire argument part of a command. Comparison expressions evaluate to true or false and can also be used in conditions.",
            [
                new ScriptHelpEntry(
                    "Interpolation",
                    "Use ${...} inside a command line to insert a computed value into the final command text. See the Variables tab for a list.",
                    "/echo ${expression}",
                    """
                    /echo ${player_name}
                    /echo ${random(["a", "b", "c"])}
                    """),
                new ScriptHelpEntry(
                    "Bare expression arguments",
                    "If the full argument text is a valid expression, BigMac evaluates it and uses the result as the command arguments.",
                    "/p expression",
                    """
                    /ac "Divine Benison"
                    /p random([
                        "You're not the main tank, you're the main *concern*",
                        "Floor tanking wasn't in the tutorial",
                        "On your feet, lass!"
                    ])
                    """),
                new ScriptHelpEntry(
                    "Literal values",
                    "Expressions support quoted strings, whole numbers, and arrays. Strings may use single or double quotes and support standard escapes like \\n, \\t, \\\\, \\\", and \\\'.",
                    """
                    "text"
                    'text'
                    true
                    false
                    42
                    ["a", "b", "c"]
                    """),
                new ScriptHelpEntry(
                    "Local bindings",
                    "Use let to store a computed value for the rest of the current block. Bindings are immutable, available to later statements in the same block and nested blocks, reset each time the macro runs, and cannot reuse built-in variable names.",
                    """
                    let message = random("Pulling", "Going in", "Starting")
                    let low_resources = min(hp_pct, mp_pct) < 25
                    """,
                    """
                    let target_label = choose(has_target, target_name, "No target")
                    /echo ${target_label}

                    if (has_target) {
                        let callout = upper(target_label)
                        /p ${callout}
                    }
                    """),
                new ScriptHelpEntry(
                    "Comparison expressions",
                    "Comparison expressions return true or false. You can use them in a command, interpolation, or directly inside if(...).",
                    """
                    world_name == "balmung"
                    party_size >= 4
                    level < 100
                    """,
                    """
                    /echo ${world_name == "balmung"}
                    /echo party_size >= 4
                    """),
            ]),
        new ScriptHelpSection(
            "Variables",
            "Named variables are available inside ${...} interpolation and other expressions such as random(...). Variables can also be used directly in if(...), or compared with <, >, ==, !=, <=, and >=.",
            [
                new ScriptHelpEntry(
                    "Macro arguments",
                    "args contains the original argument text passed after the macro's slash command. arg_count is the number of arguments, and arg(1) returns the first argument as text (indexes start at 1). Missing arguments return an empty string; use default(arg(1), \"fallback\") to provide a default. Arguments are private to each execution and remain available in nested blocks and after waits. Calling another macro passes only the arguments explicitly written in that command.",
                    """
                    ${args}
                    ${arg_count}
                    ${arg(1)}
                    """,
                    """
                    // If saved as /greet, run: /greet "Momodi"
                    let name = default(arg(1), "friend")
                    /echo Hello, ${name}!
                    """),
                new ScriptHelpEntry(
                    "Current job and level",
                    "Available variables: job_abbr, job_id, job_name, level, and role. level uses your effective level, so it reflects level sync. role is a simple string such as tank, healer, dps, or unknown.",
                    """
                    ${job_abbr}
                    ${job_id}
                    ${job_name}
                    ${level}
                    ${role}
                    """,
                    """
                    /echo ${job_name} (${job_abbr}) level ${level} on ${role}
                    """),
                new ScriptHelpEntry(
                    "Health and mana",
                    "Available variables: hp_pct, mp_pct, and target_hp_pct. These are whole-number percentages from 0 to 100.",
                    """
                    ${hp_pct}
                    ${mp_pct}
                    ${target_hp_pct}
                    """,
                    """
                    /echo HP ${hp_pct}% / MP ${mp_pct}%
                    /echo Target HP ${target_hp_pct}%
                    """),
                new ScriptHelpEntry(
                    "Names and locations",
                    "Available variables: player_name, target_name, zone_name, and world_name.",
                    """
                    ${player_name}
                    ${target_name}
                    ${zone_name}
                    ${world_name}
                    """,
                    """
                    /echo ${player_name} is targeting ${target_name}
                    /p ${player_name} is in ${zone_name} on ${world_name}
                    """),
                new ScriptHelpEntry(
                    "Party and state",
                    "Available variables: party_size, has_target, in_combat, mounted, and gposing.",
                    """
                    ${party_size}
                    ${has_target}
                    ${in_combat}
                    ${mounted}
                    ${gposing}
                    """,
                    """
                    /echo Party size: ${party_size}
                    /echo Targeted: ${has_target}, in combat: ${in_combat}, mounted: ${mounted}, gposing: ${gposing}
                    """),
                new ScriptHelpEntry(
                    "Variables in conditions",
                    "Variables do not need a wrapper function. Use them directly when you want a truthy check, or compare them when you want a specific value.",
                    """
                    if (in_combat) { ... }
                    if (has_target) { ... }
                    if (party_size >= 4) { ... }
                    if (world_name == "balmung") { ... }
                    if (role == "healer") { ... }
                    """,
                    """
                    if (mounted) {
                        /echo Travel mode enabled.
                    }

                    if (world_name == "balmung") {
                        /echo World check passed.
                    }

                    if (role == "healer") {
                        /echo Healer role detected.
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Functions",
            "BigMac supports built-in expression functions and state query functions.",
            [
                new ScriptHelpEntry(
                    "arg(index)",
                    "Returns one argument as text using a positive whole-number index, starting at 1. Empty double quotes count as an empty argument. Inside double quotes, backslash can escape a double quote or another backslash. Single quotes are literal characters. indexes past arg_count return an empty string.",
                    "arg(1)",
                    """
                    // Run this macro with: hello "two words"
                    if (arg_count >= 2 && arg(1) == "hello") {
                        /echo ${arg(2)}
                    }
                    """),
                new ScriptHelpEntry(
                    "random(...)",
                    "Chooses one random option. You can pass an array as a single argument or pass multiple arguments directly.",
                    """
                    random(["a", "b", "c"])
                    random("a", "b", "c")
                    """,
                    """
                    /echo ${random(["hello", "hi", "hey"])}
                    /p random(player_name, "Pulling", "Going in")
                    """),
                new ScriptHelpEntry(
                    "choose(condition, when_true, when_false)",
                    "Evaluates the condition using normal truthiness rules, then returns either the true branch or the false branch. Only the selected branch is evaluated.",
                    """
                    choose(has_target, target_name, "No target")
                    choose(hp_pct < 30, "Emergency", "Stable")
                    """,
                    """
                    /echo ${choose(has_target, target_name, "No target selected")}
                    /p ${choose(in_combat, "Engaged", "Standing by")}
                    """),
                new ScriptHelpEntry(
                    "min(...), max(...), clamp(value, min, max)",
                    "min and max return the smallest or largest numeric argument. They also accept a single array argument. clamp keeps a number within the inclusive range you provide.",
                    """
                    min(hp_pct, mp_pct)
                    max([hp_pct, mp_pct, target_hp_pct])
                    clamp(distance_to_target(), 0, 25)
                    """,
                    """
                    /echo Lowest resource: ${min(hp_pct, mp_pct)}%
                    /echo Safe distance: ${clamp(distance_to_target(), 0, 15)}
                    """),
                new ScriptHelpEntry(
                    "contains(text, value)",
                    "Returns true when the first value contains the second value as a case-insensitive substring.",
                    """
                    contains(world_name, "balm")
                    contains(zone_name, "limsa")
                    """,
                    """
                    if (contains(lower(zone_name), "limsa")) {
                        /echo Limsa detected.
                    }
                    """),
                new ScriptHelpEntry(
                    "starts_with(text, value), ends_with(text, value)",
                    "Returns true when the first value starts or ends with the second value. Comparisons are case-insensitive.",
                    """
                    starts_with(zone_name, "limsa")
                    ends_with(world_name, "mung")
                    """,
                    """
                    if (starts_with(lower(zone_name), "limsa")) {
                        /echo Limsa area detected.
                    }
                    """),
                new ScriptHelpEntry(
                    "lower(...), upper(...)",
                    "Converts a value to text, then returns a lowercase or uppercase version. This is useful for comparisons and formatted output.",
                    """
                    lower(world_name)
                    upper(job_abbr)
                    """,
                    """
                    /echo ${upper(world_name)}
                    if (lower(world_name) == "balmung") {
                        /echo Balmung check passed.
                    }
                    """),
                new ScriptHelpEntry(
                    "trim(...), default(value, fallback)",
                    "trim(...) removes leading and trailing spaces. default(...) returns the first value when it is truthy, otherwise it returns the fallback.",
                    """
                    trim("  text  ")
                    default(target_name, "No target")
                    """,
                    """
                    /echo ${trim("  ready  ")}
                    /echo Target: ${default(target_name, "No target")}
                    """),
                new ScriptHelpEntry(
                    "target_is_enemy()",
                    "Returns true when your current target is considered hostile. This is useful for splitting friendly-target and enemy-target behaviour.",
                    "target_is_enemy()",
                    """
                    if (target_is_enemy()) {
                        /echo Enemy target detected.
                    }
                    """),
                new ScriptHelpEntry(
                    "distance_to_target()",
                    "Returns the whole-yalm distance to your current target after hitbox adjustment. If you have no valid target, it returns a large fallback distance, so pair it with has_target when you need exact intent.",
                    "distance_to_target()",
                    """
                    if (has_target) {
                        /echo ${distance_to_target()} yalms away
                    }
                    """),
            ]),
        new ScriptHelpSection(
            "Full Examples",
            "These examples match the syntax BigMac supports today.",
            [
                new ScriptHelpEntry(
                    "Simple chance script",
                    "A 50/50 branch that either bows or echoes a random choice.",
                    Example:
                    """
                    chance (50) {
                        /bow
                    } else {
                        /echo ${random(["a", "b", "c"])}
                    }
                    """),
                new ScriptHelpEntry(
                    "Combat and party message script",
                    "A command that checks your class and whether an action is currently available, then uses variables in output.",
                    Example:
                    """
                    if (class("WHM")) {
                        if (action_available("Rescue")) {
                            /ac Rescue <2>
                        }

                        chance (10) {
                            /p ${player_name} is here on ${job_abbr}
                            /p random(target_name, zone_name, world_name)
                        }
                    } else {
                        /echo ${player_name} is not on WHM.
                    }
                    """),
                new ScriptHelpEntry(
                    "World and party gate",
                    "Nested conditionals that combine a case-insensitive string comparison with a numeric comparison.",
                    Example:
                    """
                    if (world_name == "balmung") {
                        if (party_size >= 4) {
                            /p Balmung party ready: ${party_size} players.
                        } else {
                            /echo Need more party members first.
                        }
                    } else {
                        /echo This macro is only intended for Balmung.
                    }
                    """),
                new ScriptHelpEntry(
                    "Target-aware utility script",
                    "A macro that uses a direct variable condition to check whether you currently have a target.",
                    Example:
                    """
                    if (target_name) {
                        /echo Using utility on ${target_name}
                        /ac "Divine Benison" <t>
                    } else {
                        /echo Pick a target first.
                    }
                    """),
                new ScriptHelpEntry(
                    "Travel and combat state script",
                    "A macro that checks state variables directly without a predicate wrapper.",
                    Example:
                    """
                    if (mounted) {
                        /echo Already mounted.
                    } else {
                        /mount "Company Chocobo"
                    }

                    if (in_combat) {
                        /echo Combat detected.
                    } else {
                        /echo Safe to travel.
                    }
                    """),
                new ScriptHelpEntry(
                    "Emergency support script",
                    "A support macro that checks action readiness, a player status, and current resource percentages.",
                    Example:
                    """
                    if (role == "healer") {
                        if (action_ready("Rescue")) {
                            /ac Rescue <2>
                        } else {
                            /echo Rescue not ready.
                        }

                        if (have_status("Swiftcast")) {
                            /echo Swiftcast is active at ${mp_pct}% MP.
                        } else {
                            /echo No Swiftcast active.
                        }
                    } else {
                        /echo This script expects a healer job.
                    }
                    """),
                new ScriptHelpEntry(
                    "Enemy range gate",
                    "A macro that checks whether you have an enemy target close enough before using a utility action.",
                    Example:
                    """
                    if (has_target) {
                        if (target_is_enemy()) {
                            if (distance_to_target() <= 15) {
                                /echo Enemy in range at ${distance_to_target()} yalms.
                            } else {
                                /echo Target is too far away.
                            }
                        } else {
                            /echo Current target is not an enemy.
                        }
                    } else {
                        /echo No target selected.
                    }
                    """),
                new ScriptHelpEntry(
                    "Cancelable loop script",
                    "A simple looping macro that repeats until you run it again or start moving.",
                    Example:
                    """
                    loop {
                        if (has_target && target_is_enemy()) {
                            /echo Watching ${target_name} at ${distance_to_target()} yalms.
                        } else {
                            /echo No enemy target.
                        }

                        wait(1)
                    }
                    """),
            ]),
    ];
}
