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

---

## Pass 2 Rerun + Pass 3 — 2026-05-17

### Finding 10

**Title:** Claude is inconsistent about applying Roslyn-first to symbol discovery

**Severity:** procedural / corrective rule needed

**File/tool:** Claude tester workflow; reinforced by `CLAUDE.md` "Always prefer Roslyn tools over text or grep search for C# symbol discovery"

**Observed:** In the Pass 2 rerun, Claude skipped Roslyn entirely for `DatabaseDomainRepository.cs` and went straight to Monitor tools (`find_file`, `get_source_map`, `get_file`). Internal justification was that the killed prior session had already established the symbol shape and caller count. For `SchemaObjectRepository.cs` in Pass 3, Claude deliberately led with Roslyn (`search_symbols`, `get_type_overview`, `find_callers` x4, `find_references` x2). The Operator's Roslyn telemetry proxy lit up for the second file but not the first, which made the telemetry an unreliable window into the workflow.

**Expected:** Every new file = full Roslyn discovery first, regardless of whether the model thinks it already knows the answer. Treat Roslyn as the entry point for *every* C# read cycle, not as a discretionary call.

**Minimal fix:** Saved as feedback memory `feedback_roslyn_first_saves_tokens.md`. No code change needed; this is a model-behavior correction the Operator caught live. Adding a one-line reminder near the top of `Docs/ClaudeMinimalReviewPack/Skills/RoslynFirstNavigation.md`: "Apply this rule per cycle, not per session — don't shortcut with 'I already discovered this earlier'."

**Evidence:** Operator: "i am still not getting any bloody feedback on rosln calls" after Pass 2 rerun completed; "why did the prior file not generate rosln chnges but htis one does. that mke no sensee" after Pass 3 discovery.

### Finding 11

**Title:** `find_references` returns empty for a type that `search_symbols` shows has a real field-typed usage

**Severity:** suspected Roslyn CodeLens inconsistency; needs verification

**File/tool:** `mcp__roslyn-codelens__find_references` vs `mcp__roslyn-codelens__search_symbols`

**Observed:** `search_symbols("SchemaObjectRepository")` returned three results: the class declaration plus `SchemaObjectRepository IntegrationsViewImportControl._schemaObjectRepository` (a field of that type in the UI project). `find_references("SchemaStudio.Data.SchemaObjectRepository")` returned `[]`. `find_references("SchemaObjectRepository.GetByDatabase")` also returned `[]`. The field-typed declaration is a legitimate type usage and should appear in `find_references` for the type. The overlay compile during `submit_file` confirmed there are no actual method invocations on the field (so `find_callers` was right), but the type *is* referenced as a field type and `find_references` missed that.

**Expected:** `find_references` on a type returns every place the type appears in source, including field/property/parameter/return-type declarations. If the tool intentionally limits to member-access references, that limitation should be documented.

**Minimal fix:** Verify against a small test type with known field-typed references. If reproducible, file as a bug in the Roslyn CodeLens adapter. If by design, document in the tool description ("returns method/field accesses, not type-position uses").

**Evidence:** Pass 3 discovery sequence in `Pass3_SchemaObjectRepository_Async.md`. Live session timestamp ~18:30 UTC, 2026-05-17.

### Finding 12

**Title:** Calling `get_source_map` plus `get_file` for whole-file `submit_file` rewrites doubles discovery cost

**Severity:** procedural / corrective rule needed

**File/tool:** Claude tester workflow; reinforced by `CLAUDE.md` "Read And Narrow Before Editing"

**Observed:** For a whole-file `submit_file` operation, the model called `get_source_map` (selector mode, ~2500 estimatedTokenProxy) *and* `get_file` (~1000 tokens). The source_map's purpose is to deliver stable symbol-selector keys for `get_symbol` / `submit_symbol` / `add_symbol` / `remove_symbol` — none of which are used by a whole-file submit. Both Pass 2 rerun and Pass 3 paid this double cost.

**Expected:** Discovery cost is bounded by the staging mode that follows it. Whole-file `submit_file` → Roslyn shape (`search_symbols` + `get_type_overview` + `find_callers`) plus Monitor `get_file`; **skip** `get_source_map`. Symbol-scoped staging → Roslyn shape plus Monitor `get_source_map` selector plus `get_symbol`; **skip** `get_file`.

**Minimal fix:** Saved as feedback memory `feedback_roslyn_first_saves_tokens.md`. Suggest tightening `CLAUDE.md` "Read And Narrow Before Editing" to spell this out per staging mode. Today the rule reads as if source_map is always required before editing, which encourages the over-call.

**Evidence:** Operator: "tjhe point is roslnn is supposed to save tokens." Pass 2 rerun and Pass 3 both ran the redundant pair.

### Finding 13

**Title:** `submit_file` was used where symbol-level staging tools were the right fit

**Severity:** procedural / corrective rule needed (architecture-aware)

**File/tool:** Claude tester workflow; rebuilt server now exposes `submit_symbol`, `add_method`, `add_field`, `add_property`, `add_constructor`, `add_nested_type`, `remove_symbol`, `add_using`, `remove_using`, `set_type_partial`

**Observed:** Pass 2 rerun (DatabaseDomainRepository) and Pass 3 (SchemaObjectRepository) both staged async conversions as whole-file `submit_file` payloads. Each was conceptually N independent method-body edits (the rest of the file unchanged). The rebuilt server exposes typed-insertion and symbol-replace tools that could express the same change as a sequence of small symbol-scoped stagings — sending only the changed method bodies over the wire. Operator framed the architectural rule: "you are reasoning in the cloud and editing locally" — meaning the model should reason about intent and let the local server + Roslyn compose the resulting file, not ship the whole file from the cloud.

**Expected:** `submit_file` is reserved for legitimately whole-file rewrites: new file creation, removing the entire class shape, generated-code regeneration. Async conversions, single-method edits, adding helpers, and similar should go through `submit_symbol` (replace), `add_method` / `remove_symbol` (insert/delete), and friends.

**Minimal fix:** Saved as feedback memory `feedback_reason_in_cloud_edit_locally.md`. Suggest adding a "Choose your staging mode" section to `Docs/Skills/SystemMonitorStaging.md` listing intent → tool mapping, and a corrective example showing the same async conversion done two ways (submit_file vs N submit_symbol calls) with a payload-size comparison.

**Evidence:** Operator: "submit file is more for a full new file edit" and "you need to use the ROSLNN to edit the local copy and never send a full file." Pass 2 rerun and Pass 3 are the violating cases. Both accepted-normalized — the *change* was correct, the *tool choice* was the issue.

## Pass 4 — 2026-05-17 — Session resume after VS Code restart

### Finding 14

**Title:** VS Code window reload does not respawn MCP server launchers; full window restart is required

**Severity:** confusing

**File/tool:** Claude Code MCP client behavior in the VS Code native extension; setup docs imply window reload should suffice

**Observed:** After killing the WinForms host and restarting it, executing a VS Code window reload (Ctrl+Shift+P → "Developer: Reload Window") did **not** cause Claude Code to respawn the `monitor-base-claude` or `roslyn-codelens` MCP server launcher scripts. Evidence: log files `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\logs\mcp-server-monitor-base-claude.log` and `...mcp-server-roslyn-codelens.log` had **zero new entries** after the reload — last events were `Server transport closed` from the prior session. The launchers were never invoked. `/mcp` slash command is not available in this environment for an explicit reconnect.

**Expected:** Either window reload triggers MCP client re-init (respawning the launchers), or the docs state that a full VS Code window restart is required after host restart.

**Minimal fix:** Document the requirement in `MCP_CLIENT_TESTING.md` and `SESSION_RESUME.md` template: "After restarting the WinForms host, do a full VS Code window restart (close the window entirely, reopen the workspace). Window reload alone is not sufficient." Longer-term consideration: have the WinForms host advertise its MCP endpoint via a known IPC mechanism so the bridge can poll liveness and auto-reconnect on next tool call.

**Evidence:** Two diagnosis sessions of `SESSION_RESUME.md` (commits `4f0de26` and `44db1d8`) captured the reload-then-restart sequence with concrete log gaps. After the full restart, pre-flight passed on first try.

### Finding 15

**Title:** Empty `find_references` led to undersized single-file WriteSet for what was a coupled multi-file rename — Pass 2/3 left consumers un-updated and watched build broke post-accept

**Severity:** blocker (workflow-level — produced a non-compiling watched-source state that overlay validation could not catch because the consumer files were never staged into the session)

**File/tool:** Pass 2 rerun (`DatabaseDomainRepository` async rename) and Pass 3 (`SchemaObjectRepository` async rename); Roslyn `find_references` discovery; `CLAUDE.md` "Reason In Cloud, Compose Locally" section as written before this pass

**Observed:** Both passes staged the repository file alone via `submit_file(sessionId)`, accepted the rename, then ended the session. Roslyn `get_diagnostics` next session surfaced 8 compile errors: 7 CS1061 ("method does not exist") at consumer call sites in `UI\DatabaseDomainManagerForm.cs`, `UI\MergedEditorSurface\IntegrationsViewImportControl.{Loading,Persistence}.cs`, plus 1 CS0411 in `SchemaStudio.Data\DatabaseDomainTypeConverter.cs` (consumer partially adapted to `GetByDatabaseAsync` but treats the `Task<T>` return as `IEnumerable<T>`). Operator framing: "you should have been using the new protocol for multi file edits — write set is dead." The session bag is populated by staging calls themselves; consumer fixes had to be staged under the **same sessionId** before the first `launch_staged_diff`. Two interacting root causes:

1. **Discovery false-negative**: `find_references` returned empty for `_schemaObjectRepository` field in Pass 3 (Finding 11). I read empty as "no consumers exist" instead of "discovery is incomplete; cross-check." That undersized the WriteSet.
2. **Stale protocol in CLAUDE.md**: the "anchor every target file via `get_file(sessionId)` before staging" rule treated session membership as a separate read-time concern. The canonical protocol in `get_staging_guide` has no anchor step — the staging call itself populates the session.

**Expected:** Coupled multi-file rename should:
- Use Roslyn discovery to identify every consumer file. Treat empty `find_references` as "verify another way," not as "no consumers."
- Stage repository AND every consumer fix via `submit_symbol(sessionId)` under one session before the first `launch_staged_diff`.
- Overlay validation then sees the union and reports diagnostics across the whole change.

**Minimal fix:** (a) Updated `CLAUDE.md` "Reason In Cloud, Compose Locally" to drop the dead WriteSet/anchor-step language and reference `get_staging_guide` as canonical (this pass). (b) Add a "Discovery Discipline" note that empty Roslyn discovery results are not absence-of-consumers; cross-check before sizing the WriteSet. (c) Consumer updates for Pass 2/3 left to Codex per Operator direction — Operator's preferred shape is sync bridge methods on the repositories (re-add `GetByDatabase`, `GetBySource`, `Insert`, `Update`, `SaveAll` as thin sync-over-async wrappers), preserving consumer call sites and keeping async as the primary path.

**Evidence:** `get_diagnostics(severity=error)` results captured in this pass's STATUS entry. Watched repo state: `M SchemaStudio.Data/DatabaseDomainRepository.cs`, `M SchemaStudio.Data/DatabaseDomainTypeConverter.cs`, `M SchemaStudio.Data/SchemaObjectRepository.cs` — all uncommitted in `C:\Schema Studio - DBV2` after Pass 2/3 accepts.
