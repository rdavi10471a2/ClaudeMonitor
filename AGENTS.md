# MonitorBaseClaude Implementation Agent Rules

This file is for Codex or another implementation assistant working on MonitorBaseClaude itself.

`CLAUDE.md` is the runtime rule file for the future Claude MCP client. Do not treat Codex as the final runtime MCP agent. Codex is helping build and harden the tooling so Claude can later use it safely.

## Role Boundary

Codex role:

- Implement MonitorBaseClaude code, tests, docs, and smoke harnesses.
- Preserve the intended Claude-facing runtime workflow.
- Keep changes narrow and aligned with existing architecture.
- Verify with builds and focused smoke tests when code changes.

Claude runtime role:

- Later, consume the Monitor MCP Tool Server through MCP.
- Follow `get_source_map -> get_symbol -> stage candidate -> WinMerge review/save -> record_diff_decision`.
- Prove the workflow through `MCP_CLIENT_TESTING.md` before real watched-source staging.

## Do Not Reinterpret Runtime Semantics

When editing this repo, do not weaken these invariants:

- Monitor Tool Server stages candidates but does not directly overwrite watched source.
- WinMerge is the Host-owned review/save surface.
- Operator accepts all or rejects all.
- Operator does not hand-edit or partially merge hunks.
- `record_diff_decision` classifies by vote-plus-hash agreement.
- `dirty-unexpected` blocks further AI edits until Host/Operator refreshes, inspects, resets, or explicitly recovers.
- `get_source_map` is read-only discovery, not an accept/reject gate.
- Source-map narrowing should happen before full-file reads for C# edits.
- Structural evolution is allowed only through explicit, bounded, all-or-none staged candidates.

## Implementation Rules

- Do not refactor unrelated files.
- Do not extract helper methods merely to remove small local repetition.
- Inline repetition is acceptable when it keeps workflow state, source-map mode behavior, or vote-plus-hash logic easier to review.
- Do not perform DRY cleanup as a side effect of a narrow requested change. Extract only when it creates a real semantic boundary, reduces meaningful risk, or is explicitly requested as a bounded structural candidate.
- Do not change public tool contracts unless the task explicitly requires it.
- Preserve existing `AIFileContext`, `FileVersion`, and meaningful `AIChange` attributes.
- Do not add routine process comments to watched source.
- Do not add glyphs, emoji, decorative Unicode, or non-ASCII process text as anchors, workflow markers, session purposes, or generated source strings.
- Keep generated monitor state under `Working`, not inside the watched project.
- Use disposable fixtures for mutation-path tests when possible.
- Use real DBV2 source-map smokes for read/discovery artifacts, not mutation.

## Verification Defaults

For code changes, prefer:

```powershell
dotnet build .\MonitorBaseClaude.slnx
```

Use focused smokes based on the touched surface:

```powershell
Legacy smoke tests are local-only under `LocalSmokeTests\LegacyToolSmokeTests` and are not part of normal source pushes.

For new smoke work, prefer one class per test behind a small runner. Use separate executables only for process/bridge/proxy lifecycle probes.

Do not add more flags to the old monolithic smoke harness unless explicitly requested.

Keep local smoke output under `Working\History\ToolSmokeTests` or the ignored `LocalSmokeTests` workspace.
```

Use Ollama smokes as harness/model-behavior probes only. Do not treat small local Ollama failures as Monitor correctness failures.
