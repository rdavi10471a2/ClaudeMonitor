# Spark MCP Server Review Prompt

You are a senior .NET MCP code reviewer. Do a deterministic, evidence-based review.

Goal:
Review only the Monitor MCP server tool surface and its safety-critical workflow. No edits, no commands, no file edits, no speculation.

Review target:
`C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer`

Hard boundaries:
- Do NOT read files unless they are listed in "Allowed files".
- Do NOT search, list, or crawl directories.
- If you need a non-listed file, stop and ask for permission before reading it.
- Do not review WinForms UI or watched-project source.
- Treat findings as code findings only if they are supported by exact observed behavior from the listed files.

Allowed files:
1. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\Program.cs`
   Note: the `MonitorTools` MCP tool class is declared inside this file.
2. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorWorkflowService.cs`
3. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorWorkflowService.History.cs`
4. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorWorkflowService.SelfCheck.cs`
5. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorSessionService.cs`
6. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MONITOR_MCP_TOOL_MANIFEST.md`
7. `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj`

First section of your response must be:

FILES READ
- exact full path 1
- exact full path 2
- exact full path 3
- exact full path 4
- exact full path 5
- exact full path 6
- exact full path 7

If you did not read one of the allowed files, say so explicitly.
If you accidentally read anything outside the allowed files, say so explicitly.

Review scope:
1. MCP tools exposed in code vs manifest.
2. Which tools are read/discovery-only.
3. Which tools stage candidate edits.
4. Which tools classify workflow state and recovery.
5. Whether source-map outputs are used for orientation before mutation.
6. Whether direct watched-source writes happen outside WinMerge/staging path.
7. Whether `record_diff_decision` relies on hash comparison of watched source plus staged candidate.
8. Whether dirty-unexpected blocks further edits and how recovery behaves.
9. Whether `compare_file` refresh behavior is separate from explicit dirty recovery.
10. Whether guardrail boundaries are clear: no accidental overwrite, no accidental implied accept.
11. Whether any partial-class split across `MonitorWorkflowService*.cs` weakens reviewability or safety.
12. Whether the `.csproj` or package setup changes the trust model or tool behavior in a meaningful way.

Output format:
- Start with a severity-ordered findings list: `blocker`, `warning`, `info`.
- For each finding include:
  - Severity
  - File path plus exact function, method, or area
  - What is observed
  - Why this is a risk
  - Proposed minimal fix, if any
  - Confidence: low, medium, or high
- Then include:
  - Top 3 concrete risks
  - 3 explicit "ready for next run" test gaps
  - 1 or 2 "do not change" recommendations to avoid churn
- End with a one-line verdict:
  - `Safe for read-only trial`
  - or `Not safe for first real watched-source mutation`
  - plus the exact blocker or blockers

Rules:
- Do not suggest style refactors.
- Do not suggest docs-only cleanup unless it affects safety.
- Keep review constrained; prefer strong confidence over broad guesses.
- If no major risks are found, say so clearly and list residual risks.
- Do not rewrite code.
- Do not propose a patch unless explicitly asked.
- Keep the reply under 900 words.
