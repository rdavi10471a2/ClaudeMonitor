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

### Finding 9 — Pre-rebuild discovery

**Title:** `CLAUDE_Live_Tests\*.cs` files are picked up by `MonitorBaseClaude.csproj` compile glob

**Severity:** confusing (blocks build when notes folder contains C# samples)

**File/tool:** `MonitorBaseClaude.csproj`, repo build configuration

**Observed:** A clean `dotnet build .\MonitorBaseClaude.slnx` failed with six errors against `CLAUDE_Live_Tests/Pass2_Proposed/DatabaseDomainRepository.cs` (Dapper / Microsoft.Data / DatabaseDomainDefinition not found) because that file is a DBV2 candidate sample and is unrelated to the WinForms host project. The default `**/*.cs` glob in `MonitorBaseClaude.csproj` swept it in. Worked around locally by renaming the samples to `.cs.txt`. Build then succeeded in 4.1 s.

**Expected:** The notes folder is explicitly carved out as Claude's own working-notes lane in `CLAUDE_Live_Tests/README.md`. The repo build should ignore everything under that path. A model dropping a C# sample into its own notes folder should not break the project build.

**Minimal fix:** Add `<Compile Remove="CLAUDE_Live_Tests\**\*.cs" />` and matching `EmbeddedResource Remove` / `None Include` lines to `MonitorBaseClaude.csproj`, or a `Directory.Build.props` at the repo root that excludes the folder for all projects. Once that lands, `.cs.txt` rename in `Pass2_Proposed/` can revert to plain `.cs` so Codex sees the samples with proper syntax highlighting.

**Evidence:** Build log timestamps in this pass; six CS0246/CS0234 errors against `CLAUDE_Live_Tests/Pass2_Proposed/DatabaseDomainRepository.cs` before rename. Build clean after rename.

### Finding 8 — Pass 2 test-validity gate I should have caught up-front

**Title:** Pass 2 staging is not a valid test result without a rebuilt server and a running WinForms host

**Severity:** confusing (procedural; not a code bug)

**File/tool:** Claude tester workflow

**Observed:** I started staging real candidates against DBV2 while the running MCP server was still the stale binary (Finding 2) and without confirming the WinForms host was running. Any `submit_file` result is therefore from an older code path, and any `launch_staged_diff` call would return `host_unavailable`, not a real Operator decision. Findings 6 and 7 are still real signal, but the staged-record itself should not be treated as evidence of current server behavior.

**Expected:** Before staging anything against DBV2, confirm (a) the MCP server binary matches current source, (b) the WinForms host is running, and (c) `get_workflow_status` and a Host ping (if surfaced) report healthy. Treat any pre-condition failure as a hard stop.

**Minimal fix:** Add a one-line "test pre-conditions" rule at the top of `CLAUDE_Live_Tests/README.md` and at the top of `MCP_CLIENT_TESTING.md`'s Claude Code section. Pass 2 staging in session `monitor-20260517223523-706ea5677979487a9` is now annotated invalid in STATUS.md.

**Evidence:** Operator caught this mid-pass: "if you have not done a rebuild then this is not a valid test."

### Finding 7

**Title:** Setup docs do not say to start the WinForms host before MCP testing

**Severity:** stale / suggestion

**File/tool:** `MCP_CLIENT_TESTING.md`, `Docs/ClaudeMinimalReviewPack/README.md`, skill cards

**Observed:** `MCP_CLIENT_TESTING.md` walks through building and opening the MCP server but says nothing about the WinForms host. The skill cards reference the "WinForms Host" for the overlay-error gate and the actual WinMerge launch, but no doc tells a fresh Claude/Operator that the WinForms host must be running for the workflow to function end-to-end. Without it, `launch_staged_diff` returns `host_unavailable` and the queue cannot advance.

**Expected:** A short "Pre-flight" section near the top of `MCP_CLIENT_TESTING.md` listing the steps in order: (1) rebuild MonitorBaseClaude.McpServer, (2) start the MonitorBaseClaude WinForms host, (3) confirm MCP connection in Claude Code, (4) run `get_monitor_status` / `get_workflow_status`. Pass 1 also missed this — Finding 7 is filed against my own earlier pass.

**Minimal fix:** Insert a pre-flight checklist in `MCP_CLIENT_TESTING.md` and mirror a one-liner in `Docs/ClaudeMinimalReviewPack/README.md`.

**Evidence:** Operator: "the docs are supposed to say you should start the winforms app as well." No active doc in `Docs/ClaudeMinimalReviewPack/` or `MCP_CLIENT_TESTING.md` includes this instruction.

### Finding 6 — Pass 2 confirmation of a known bug

**Title:** `submit_file` against a brand-new watched path fails opaquely

**Severity:** blocker for the create-file workflow

**File/tool:** `monitor-base-claude` MCP tool `submit_file`

**Observed:** Submitting `SchemaStudio.Data\DatabaseDomainRepository.Sql.cs` (a new partial companion file that does not exist in watched source) returns only `An error occurred invoking 'submit_file'.` with no further detail. Retried once with the same result. The companion file is required by the modified main file (which staged fine); without it the overlay validation correctly reports `CS0103: The name 'Sql' does not exist`. This forced re-planning the test into a single-file shape, losing the partial-class-companion test goal.

**Expected:** `submit_file` against a new path either stages the candidate or returns a structured error explaining why (for example, "new-path staging not enabled in this server build"). Same surface as for an existing path. This was reportedly fixed around commit `b0618de`, so the live result suggests either a regression or that the running server binary predates the fix.

**Minimal fix:** Rebuild and restart `MonitorBaseClaude.McpServer` (Pass 1 Finding 2 root cause) and retry. If the failure persists after a confirmed fresh binary, the new-path code path itself has a regression worth tracing in `MonitorWorkflowService.SubmitFile`.

**Evidence:** Session `monitor-20260517223523-706ea5677979487a9`. First call at 22:48 UTC failed; retry seconds later also failed. The modify-existing-path call inside the same session succeeded normally and produced staged record `20260517_174813445_submit_file_DatabaseDomainRepository_4bc6cba3`. Operator decided to bail on the partial split and restage as a single file for this pass.

---

### Finding 5

**Title:** Duplicate `MONITOR_MCP_TOOL_MANIFEST.md` copies risk drift

**Severity:** suggestion

**File/tool:** `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md` versus `Docs/ClaudeMinimalReviewPack/MONITOR_MCP_TOOL_MANIFEST.md`

**Observed:** Two copies of the manifest exist in the repo. `get_tool_manifest` reads only the server-side copy per `Program.cs` line 426.

**Expected:** One canonical manifest, or an explicit snapshot note. The two could silently diverge over time without anyone noticing.

**Minimal fix:** Add a header line at the top of `Docs/ClaudeMinimalReviewPack/MONITOR_MCP_TOOL_MANIFEST.md`: "Snapshot of `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`; regenerate before bundling."

**Evidence:** Both files exist. The merge created the pack copy. `get_tool_manifest` serves only the server-root copy. No sync exists today.
