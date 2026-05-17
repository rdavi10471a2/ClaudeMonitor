# Findings

Compact bug reports and doc suggestions from Claude tester/config-helper passes. Codex owns merging accepted items into the active docs.

Format per finding: Title, Severity, File/tool, Observed, Expected, Minimal fix, Evidence. Max 5 findings per pass. Max 150 words per finding.

---

## Pass 1 — 2026-05-17

### Finding 1

**Title:** Stale link to retired skill doc in MCP_CLIENT_TESTING.md

**Severity:** stale

**File/tool:** `MCP_CLIENT_TESTING.md` line 7

**Observed:** "For a compact Claude workflow sheet, use `Docs\ClaudeRoslynSystemMonitorSkill.md`."

**Expected:** Link to the current skill-pack entry point, now at `Docs/ClaudeMinimalReviewPack/Skills/SkillRouter.md` or `Docs/Skills/SkillRouter.md`.

**Minimal fix:** Replace the line with: "For a compact Claude workflow sheet, start at `Docs/ClaudeMinimalReviewPack/Skills/SkillRouter.md`."

**Evidence:** The file `Docs/ClaudeRoslynSystemMonitorSkill.md` was moved to `Docs/Archive/Skills/ClaudeRoslynSystemMonitorSkill.md` in commit 9ac8d69. The reference still points at the old path.

### Finding 2

**Title:** Running MCP server does not expose newly-implemented tools, so there is currently no live way to serve skill cards

**Severity:** blocker for the tester pass

**File/tool:** `monitor-base-claude` MCP server, the running binary

**Observed:** The advertised tool list omits `get_staging_guide`, `get_smoke_test_catalog`, `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`, and `set_type_partial`. Because `get_staging_guide` is not callable, there is no server-side path to serve the skill cards on demand. Claude must Read the cards from `Docs/Skills/` directly until the binary catches up.

**Expected:** All of the above appear in `tools/list` per manifest lines 142 to 144, 397 to 406, and 533 to 541. `get_staging_guide` returns the staging plus session-overlay cards.

**Minimal fix:** Rebuild `MonitorBaseClaude.McpServer` then reconnect Claude Code's MCP session. The C# code at `Program.cs` lines 230 to 460 already implements every missing tool. This is a build and restart issue, not a code gap.

**Evidence:** `Program.cs` defines `[McpServerTool]` for each listed method. The deferred-tool list returned by the running stdio process includes none of them.

### Finding 3

**Title:** Two parallel copies of the skill cards risk drift

**Severity:** confusing

**File/tool:** `Docs/Skills/*.md` versus `Docs/ClaudeMinimalReviewPack/Skills/*.md`

**Observed:** The same ten skill cards live in both folders. `get_staging_guide` reads only from `Docs/Skills/` per `Program.cs` lines 439 to 440. The review pack README and the pack files point at `Docs/ClaudeMinimalReviewPack/Skills/`. No CI or script syncs the two.

**Expected:** One canonical source. Either `get_staging_guide` reads from the pack folder, or `Docs/Skills/` is the source of truth and the pack is generated from it before bundling.

**Minimal fix:** Add a one-line note at the top of `Docs/ClaudeMinimalReviewPack/README.md`: "Snapshot of `Docs/Skills/`; do not hand-edit; regenerate before bundling." Or point `get_staging_guide` at the pack folder.

**Evidence:** Manifest line 535 says `get_staging_guide` returns guidance from `Docs/Skills/SystemMonitorStaging.md` and `Docs/Skills/SessionOverlayValidation.md`. The pack has identical-content duplicates.

### Finding 4

**Title:** SkillRouter.md says to confirm task type from `get_workflow_status`, but that tool returns no task type

**Severity:** confusing

**File/tool:** `Docs/ClaudeMinimalReviewPack/Skills/SkillRouter.md` lines 7 to 8 and the mirror at `Docs/Skills/SkillRouter.md`

**Observed:** "Confirm task type from get_workflow_status output or the user's task description before selecting cards."

**Expected:** `get_workflow_status` returns watched solution path, working folder, and WinMerge resolution. It does not return any task type field. The instruction reads as if the tool tells the model what kind of work is needed, which it cannot.

**Minimal fix:** Drop the tool name. Replace with: "Read the user's task description before selecting cards. If unclear, load RoslynFirstNavigation.md only and route after the first tool results."

**Evidence:** `Program.cs` lines 66 to 69 and manifest lines 168 to 170 confirm `GetWorkflowStatus` returns paths and diff tool resolution only.

### Finding 5

**Title:** Duplicate `MONITOR_MCP_TOOL_MANIFEST.md` copies risk drift

**Severity:** suggestion

**File/tool:** `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md` versus `Docs/ClaudeMinimalReviewPack/MONITOR_MCP_TOOL_MANIFEST.md`

**Observed:** Two copies of the manifest exist in the repo. `get_tool_manifest` reads only the server-side copy per `Program.cs` line 426.

**Expected:** One canonical manifest, or an explicit snapshot note. The two could silently diverge over time without anyone noticing.

**Minimal fix:** Add a header line at the top of `Docs/ClaudeMinimalReviewPack/MONITOR_MCP_TOOL_MANIFEST.md`: "Snapshot of `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`; regenerate before bundling."

**Evidence:** Both files exist. The merge created the pack copy. `get_tool_manifest` serves only the server-root copy. No sync exists today.
