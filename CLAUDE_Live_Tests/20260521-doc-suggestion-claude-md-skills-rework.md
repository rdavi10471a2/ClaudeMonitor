---
status: new
type: doc-suggestion
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

CLAUDE.md teaches tool sequences but not the design principle that makes them make sense. The "reason in the cloud, edit locally" architecture is stated in `SystemMonitorStaging.md` but absent from CLAUDE.md. Several workflow gaps (warm-session span editing, solution index for dependency surfaces, refresh_file cold-session entry) are also missing. Additionally CLAUDE.md is verbose relative to what a model actually needs per turn — much of it duplicates skill card content. Recommend a rework pass: trim CLAUDE.md to principle + boundary rules, push mechanics into updated skills, and add the missing design intent.

---

## CLAUDE.md Gaps To Add

### 1. Design principle section (new, at top)

The core design is currently absent from CLAUDE.md but present in `SystemMonitorStaging.md`. Add something like:

> **Design: reason in the cloud, edit locally.**
> Claude holds compact context (summaries, signatures, in-context file knowledge). The local server executes targeted edits. Optimize both directions:
> - Inbound: use the solution index and source maps to fetch dependency surfaces in summary form. Load the file you are editing; summarize what it references via index tools rather than loading all relevant files.
> - Outbound: use the smallest edit primitive. `replace_span_in_file` for text spans with `expectedOldText` from context. Symbol-level tools for C#. `submit_file` only for new files or genuine whole-file rewrites.

This belongs above "Required Edit Loop" so it frames every sequence that follows.

### 2. Solution index as dependency surface (missing entirely)

The read workflow (steps 1–4 of Required Edit Loop) does not mention solution index tools. The intent is: after locating the working file, use `find_indexed_symbols`, `find_indexed_callers`, `find_indexed_references`, or `query_solution_index` to fetch referenced items (types, methods, namespaces the file uses) in summary form, rather than loading those dependency files in full.

Add to the Read-And-Narrow sequence:

> After loading the working file's structure, use `query_solution_index` or `find_indexed_symbols` to surface referenced types and members in summary form. Load dependency file bodies only when a specific implementation detail is required.

Note: this flow is design intent; end-to-end validation in a real multi-dependency edit session is still pending.

### 3. Warm-session guidance for `replace_span_in_file` (missing)

Line 93 says "when you can identify an exact span from the current file text" but does not distinguish warm vs cold sessions. Add:

> **Warm session** (file already loaded into context this session): call `replace_span_in_file` directly with `expectedOldText` from in-context knowledge. Do not re-read. `expectedOldText` validates in-context reasoning server-side — that is its purpose.
>
> **Cold session** (file not yet in context): call `refresh_file` to initialize the Working copy, then `Read(workingFilePath, offset, limit)` in chunks to load it. Then proceed as warm. Do not use `get_file` on files > ~40KB.

### 4. `refresh_file` as cold-session entry (missing, Finding 56)

`refresh_file` is not mentioned anywhere in CLAUDE.md. It is the correct pre-step before chunked Read on large files. Add to the Razor/text span editing guidance:

> For large Razor or text files (> ~40KB), cold-session entry is: `refresh_file(sourceFilePath)` → `Read(workingFilePath)` in chunks. `refresh_file` returns `candidateFilePath`. Never call `get_file` on large files — oversized tool results cause API-level failures.

### 5. Razor section is outdated (line 176)

Current text:
> "Razor files are not plain C# files. Do not apply C# Roslyn symbol surgery directly to `.razor` or `.cshtml` source. Use read/find/full-file staging and diff review until Razor-aware validation is available."

The "full-file staging" clause is stale. `replace_span_in_file` has been validated on Razor files (BaseViewCreator.razor 86KB, ManageViews.razor 36KB, 2026-05-21). Update to:

> Razor files are not plain C# files. Do not apply C# Roslyn symbol surgery directly to `.razor` or `.cshtml` source. For Razor edits, prefer `replace_span_in_file` (warm or cold session) over full-file `submit_file`. Full-file submission is still acceptable for new Razor files or broad structural rewrites. Overlay compile validates `@code` blocks via Razor SDK.

---

## CLAUDE.md Trim Suggestions

The file is ~177 lines and covers both *what* (tool sequences) and *why* (boundaries). The skill cards already cover mechanics in detail. Suggest:

1. **"Required Edit Loop" (steps 1–10)** and **"Expected Tool Sequences"** are largely redundant with `SystemMonitorStaging.md`. CLAUDE.md should state the boundary rule ("follow the Monitor MCP workflow") and point to the skill, not duplicate the step list.

2. **"Unsafe Or Ambiguous Requests" flow block** could be two sentences: refuse direct writes, stop on dirty-unexpected. The skill cards carry the detail.

3. **`get_source_map` description block** (lines 155–155, ~6 sentences) belongs in a skill or the tool manifest, not in CLAUDE.md.

4. **"Marker And Glyph Rules"** is detailed and correct but low-frequency. Move body to a skill or doc; leave a one-liner in CLAUDE.md.

Target: CLAUDE.md at ~80 lines. Principle + boundaries + lane rules + conflict resolution. Mechanics in skills.

---

## Skills Rework Suggestions

### Missing: index-for-dependencies pattern

No skill card addresses the inbound-channel optimization: use the solution index to fetch dependency surfaces in summary form. Candidate new card or addition to `RoslynFirstNavigation.md`:

> When the working file references types from other files (via `using` directives), fetch those types' surfaces via `find_indexed_symbols` / `query_solution_index` / `get_source_map(scope: namespace)` before loading any dependency file body. Load dependency bodies only when a specific implementation detail is required.

Note: validated end-to-end flow pending; add as design intent with that caveat.

### Missing: warm-session span editing

No skill card covers the warm/cold session distinction for `replace_span_in_file`. Add to `SystemMonitorStaging.md` or a new `RazorTextEditing.md` card.

### `SystemMonitorStaging.md` is close but incomplete

- Already has "Reason in the cloud; compose locally" (line 12) — good.
- Missing: warm-session guidance, `refresh_file` cold-session entry, solution index for dependency surfaces.
- `Choose The Staging Mode` table is good and should stay.

### Potential card consolidation

Current 10 cards + README. Some are narrow enough to merge:
- `AsyncPropagation.md` + `PartialClassRefactor.md` are task-specific patterns; could move into long docs if rarely triggered.
- `TroubleshootingDashboard.md` is operational, not editing; could live in `.claude-local/` or a Docs subfolder.
- `FormattingOracle.md` scope is narrow enough to inline into `SystemMonitorStaging.md`.

Consolidation reduces the cognitive cost of `SkillRouter.md` decisions.

---

## Priority Order

1. Add design principle section to CLAUDE.md — highest value, missing entirely.
2. Add warm-session / `refresh_file` / Razor section update — closes Findings 56 instruction gap.
3. Add index-for-dependencies pattern to a skill card — design intent, mark as pending validation.
4. Trim CLAUDE.md mechanics to pointers — reduces per-turn context load.
5. Skill consolidation — lowest urgency, weekend-sized task.
