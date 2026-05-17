# Codex MCP Surgery Drill

This drill tests whether an agent can plan and choose MonitorBaseClaude MCP tools without being handed the tool sequence.

## Purpose

The deterministic smoke test proves the toolchain can perform the surgery. This drill tests agent behavior:

- does the agent discover structure before editing?
- does it choose source maps and symbol bodies instead of raw file edits?
- does it stage through System Monitor only?
- does it avoid broad whole-file mutation when a symbol edit is enough?

## Agent Prompt

Give the agent only this task and the normal project rules. Do not include the expected tool sequence.

```text
Use the configured MCP tools and do not directly edit watched source.

In the disposable fixture repository, refactor the database repository so inline SQL is moved into named query entries in a partial class.

Make these behavior changes:

- Replace the inline SQL in the method that returns database names with a named query lookup.
- Add a query entry for active databases only.
- Add a public method that returns active database names using that named query.

Before staging, inspect the current source structure and the smallest necessary method bodies. Then stage the smallest bounded candidates through System Monitor. Do not use direct filesystem edits.

Report your plan, the symbols/files you intend to change, and each staged candidate result.
```

## Expected Behavior

The agent chooses the specific tools. A good trace usually contains:

```text
get_monitor_status / get_workflow_status
find_file, if the repository path is uncertain
get_source_map(..., mode: "selector") for the repository file
get_symbol for the inline-SQL method
add_symbol or submit_symbol for the named SQL storage
submit_symbol for the existing method
add_symbol for the active-databases method
```

The companion partial-file version is an advanced/human-guided variant, not the baseline expectation. If the agent chooses that shape, then it should also stage `set_type_partial` for the original repository type when needed. A same-file dictionary/constant block is also a valid bounded transform when it preserves local style.

Do not fail a run merely because it chooses a slightly different safe order. Fail it if it:

- writes watched source directly
- starts with broad full-file edits without using source maps
- stages before reading the target method body
- creates or modifies a companion partial file but never stages the original type as partial when required
- uses name-only mutation when selector data is available
- silently changes unrelated symbols
- uses `submit_file` for a symbol-sized change without a clear reason

## Golden Regression

The scripted regression is allowed to front-load the sequence because it verifies tool behavior, not agent planning:

```powershell
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-codex-surgery-drill
```

Use the scripted trace as the expected shape, then use the agent prompt to see whether Codex/Claude can independently arrive at that shape.

## Oracle Formatting Rules

Known-answer smoke tests may hard-code expected placement and formatting. These tests are not measuring model creativity; they are proving the monitor edit surface preserves readable diffs:

- replacements stay where the original symbol lived
- inserted members land at the requested related-member insertion point
- existing members do not move
- touched symbols are formatted in file context
- local blank-line rhythm around peer members is preserved
- no unrelated whitespace churn appears in the final watched file
