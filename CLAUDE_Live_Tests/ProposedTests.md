# Proposed Tests — MonitorBaseClaude Against DBV2

A catalog of tests that would exercise the MonitorBaseClaude workflow end-to-end against the Schema Studio DBV2 codebase. Each test is structured so a human Operator or an automated smoke harness can run it deterministically and judge pass/fail without ambiguity.

**Scope:** the goal is to prove (or break) the architectural promises — drift defense via symbol-level staging, audit integrity via vote-plus-hash, overlay compile as a real syntactic gate, and the operator-as-final-eye review model. Region-marker additions are treated as legal first-class operations.

**Format for each test:**
- **Hypothesis** — what property this proves if it passes, what failure it catches if it fails.
- **Target** — concrete DBV2 file path.
- **Pre-conditions** — server state, session state, prior tests it depends on.
- **Steps** — concrete tool calls in order.
- **Pass** — observable, hash-or-diagnostic-based condition.
- **Fail** — opposite condition or unexpected error path.

Tests are grouped by what they probe. ID prefixes: `SS` = symbol staging; `RG` = regions; `DR` = drift detection; `MF` = multi-file; `OC` = overlay compile; `VH` = vote+hash; `WI` = workflow integrity (adversarial); `TS` = tool surface edge.

---

## SS — Symbol-Level Staging Happy Paths

### SS-001 — `submit_symbol` replaces one method body without touching neighbors

**Hypothesis:** Symbol-level staging is structurally bounded. After a `submit_symbol` against one method, every byte outside that method in the staged candidate is byte-for-byte identical to the original.

**Target:** `SchemaStudio.Data\DatabaseRepository.cs` (fresh, not yet touched by Pass 2/3).

**Pre-conditions:** server connected, host running, fresh monitor session.

**Steps:**
1. `start_monitor_session("SS-001 single-symbol body replacement")`.
2. Roslyn: `search_symbols("DatabaseRepository")`, `get_type_overview("SchemaStudio.Data.DatabaseRepository")` — record the public method list.
3. `get_file(sessionId, "SchemaStudio.Data\DatabaseRepository.cs")` — record baseline hash B.
4. Pick the smallest public method (likely `GetById` or equivalent). `submit_symbol(sessionId, path, symbolSelectorJson, newBody)` where `newBody` is the existing body with one trivial change (e.g. a renamed local variable). Do NOT modify other methods. Capture `stagedHash` S.
5. Diff the staged file at `Working\Staged\...\<record>.cs` against the source: compare every byte outside the targeted method's line range.

**Pass:** Outside the targeted method's line range, the staged file is byte-identical to the source. The method's body differs only in the intended renaming. Overlay compile clean.

**Fail:** Any byte outside the targeted method differs (whitespace, comment reflow, blank lines). That would falsify the structural-boundedness claim.

**Catches if broken:** Server splice logic accidentally reformats or re-emits surrounding code, defeating the architecture's primary drift defense.

---

### SS-002 — `add_method` inserts at default location, surrounding bytes unchanged

**Hypothesis:** `add_method` adds one new symbol without re-emitting any existing symbol's bytes.

**Target:** `SchemaStudio.Data\SchemaObjectColumnRepository.cs` (fresh, 6008 bytes).

**Steps:**
1. Fresh session, anchor with `get_file`.
2. `add_method(sessionId, path, methodSource)` where methodSource is a small async helper that returns `Task<int>` with `CancellationToken`.
3. Open staged candidate. Compute byte-for-byte equality between source and staged-minus-the-new-method's line range.

**Pass:** Every existing method, field, using directive, namespace, class signature, and brace is byte-identical. Only the new method's range is new content.

**Fail:** Anything else moved or was rewritten.

---

### SS-003 — `remove_symbol` deletes a private helper without touching its caller's body

**Hypothesis:** `remove_symbol` removes one symbol and only that symbol's lines; callers are NOT auto-edited by this call (caller fixes are a separate concern, see MF-002).

**Target:** A file with a small private helper used by one method. Operator selects via `find_callers` first.

**Steps:**
1. Session, anchor.
2. Roslyn `find_callers` on the helper to confirm it has at least one caller.
3. `remove_symbol(sessionId, path, symbolSelectorJson)` on the helper.
4. Observe overlay compile: it SHOULD report CS errors at every caller's reference site.

**Pass:** Overlay compile fails with diagnostics pointing at caller sites; the staged file is the source minus exactly the helper's lines, no other edits.

**Fail:** Server silently fixes the callers (overreach), or the staged file modifies caller bodies (architecture leak), or no overlay errors are reported when callers exist (overlay compile is not actually checking the slice).

---

## RG — Region Introduction & Region-Targeted Insertion

### RG-001 — Introduce regions to a region-less file in a single deliberate cycle

**Hypothesis:** Adding `#region` markers to a class is a legitimate one-time structural pass. After this pass, member order is unchanged and every member ends up inside the semantically correct region.

**Target:** `SchemaStudio.Data\DatabaseRepository.cs` (currently region-less per CLAUDE_Live_Tests/Pass3 baseline).

**Steps:**
1. Session, anchor.
2. Author a candidate that adds `#region Fields`, `#region Constructors`, `#region Public Methods` around the existing members in their current order. NO body changes, NO renames, NO signature changes. Only `#region` and `#endregion` lines inserted between member boundaries.
3. `submit_file` (legitimately whole-file for this structural pass per CLAUDE.md "Reason In Cloud" — region introduction is structural, not member-level).
4. `launch_staged_diff`. Operator confirms in WinMerge that the diff is purely additive region markers.
5. `record_diff_decision(accepted)`.

**Pass:** WinMerge diff shows only `#region ...` / `#endregion` line additions, never any existing-member modification. Classification: `accepted` or `accepted-normalized`. After acceptance, every existing public/private member is enclosed in the semantically correct region.

**Fail:** Any line of an existing member is also changed in the candidate. Member order changes. Class signature, namespace, or usings move.

**Catches if broken:** I overreached on a "purely additive" pass and bundled in renames or reformatting.

---

### RG-002 — `add_field` into the `Fields` region after RG-001

**Hypothesis:** After regions exist, typed insertions target named regions correctly. The new field appears at the END of the named region, not at end-of-class or top-of-class.

**Target:** Same file as RG-001, after RG-001 is accepted.

**Pre-condition:** RG-001 must be accepted. (Requires region-aware `add_field` implementation per Codex's roadmap.)

**Steps:**
1. Fresh session, anchor.
2. `add_field(sessionId, path, region: "Fields", source: "private readonly ILogger _logger;")`.
3. Inspect staged candidate.

**Pass:** New field appears as the LAST line inside `#region Fields` (before `#endregion`). Every other line of the file is byte-identical to RG-001 result.

**Fail:** Field placed outside the region (top of class, bottom of class, after another member type). Or the call succeeds silently when the region doesn't exist.

---

### RG-003 — `add_field` into a non-existent region returns structured error

**Hypothesis:** Region-targeted insertion refuses cleanly when the named region doesn't exist; it does NOT fall back to a default placement.

**Target:** Any file. Use a region name like `"DoesNotExistRegion"`.

**Steps:**
1. Session, anchor.
2. `add_field(sessionId, path, region: "DoesNotExistRegion", source: "private int _x;")`.

**Pass:** Response is a structured error naming the missing region. No staged record created.

**Fail:** Field placed at default location (silent fallback) or an opaque "An error occurred invoking add_field" (Finding 6 anti-pattern recurrence).

---

### RG-004 — Region surface in `get_source_map` selector mode

**Hypothesis:** `get_source_map` lists region names so the AI can target them without parsing the file.

**Target:** A file post-RG-001 (has regions).

**Steps:**
1. `get_source_map(path, scope: file, mode: selector)`.
2. Inspect response for a `regions` field listing names + line ranges.

**Pass:** Response includes region metadata with at least name + start line + end line. Selector keys for region-bounded symbols include their containing region.

**Fail:** No region metadata anywhere in the response.

---

## DR — Drift Detection (Original Problem)

### DR-001 — Watched file changes between submit and decision → dirty-unexpected

**Hypothesis:** Vote-plus-hash detects out-of-band edits to the watched file made after staging but before `record_diff_decision`.

**Target:** Any small repo file.

**Steps:**
1. Session, anchor, `submit_symbol` something small. Capture `originalHash`, `stagedHash`.
2. `launch_staged_diff` — WinMerge opens.
3. **Outside the workflow**, manually edit the watched file in another editor (e.g. add a blank line at the end). Save. Close manual editor.
4. Close WinMerge WITHOUT saving (so the operator vote is `rejected`).
5. `record_diff_decision(rejected)`.

**Pass:** Classification = `dirty-unexpected`. `currentHash` ≠ `originalHash` AND `currentHash` ≠ `stagedHash`.

**Fail:** Classification = `rejected` (would mean the integrity check ignored the manual edit).

---

### DR-002 — Surrounding code preservation under `submit_symbol`

**Hypothesis:** Same as SS-001 but framed as drift detection. After a chain of N `submit_symbol` calls in one session, the bytes outside every targeted symbol's line range are byte-identical to baseline.

**Target:** `SchemaStudio.Data\DatabaseRepository.cs`.

**Steps:**
1. Session, anchor. Record full file content C0.
2. `submit_symbol` on method A — capture staged content C1.
3. `submit_symbol` on method B — capture staged content C2.
4. `submit_symbol` on method C — capture staged content C3.
5. For each pair (C0 vs C3), diff byte-for-byte excluding the line ranges of methods A, B, C.

**Pass:** All bytes outside the three targeted ranges are identical between C0 and C3.

**Fail:** Anything outside the targeted ranges has drifted.

---

### DR-003 — Adversarial probe: try to drift by misusing `submit_file`

**Hypothesis:** When `submit_file` is used for what should have been a member-level edit, drift becomes structurally possible — and the operator's WinMerge eye is the only defense.

**Target:** `SchemaStudio.Data\DatabaseRepository.cs`.

**Steps:**
1. Session, anchor.
2. Author a `submit_file` candidate where the intended change is to one method, but also "while in there" reformat a comment in another method (single character whitespace change).
3. `submit_file`, `launch_staged_diff`. Operator looks at WinMerge.

**Pass:** Operator notices the unrelated change and rejects. Then re-test the same change via `submit_symbol`: the unrelated whitespace difference is structurally impossible because the surrounding code never enters my payload.

**Fail:** Operator misses the unrelated change and accepts. (Caught only by post-hoc audit. Demonstrates why "prefer symbol-level" is a real rule.)

---

## MF — Multi-File Coupled Edits

### MF-001 — Rename a public method across data + UI in one session

**Hypothesis:** Coupled multi-file edits stage cleanly when all targets are anchored before the first review launch. Overlay compile catches any partial update.

**Target:** A `SchemaStudio.Data\*Repository.cs` method that has a real UI-side caller. (Identify via `find_references` first.)

**Steps:**
1. Roslyn discovery: `search_symbols`, `find_references` on the method to identify the WriteSet.
2. `start_monitor_session("MF-001 cross-project rename")`.
3. `get_file(sessionId)` on the data-side file AND each UI-side caller file.
4. Stage data-side change via `submit_symbol` (or `submit_file` if necessary). DO NOT launch diff yet.
5. Stage UI-side caller changes via `submit_symbol` for each caller.
6. Only after every file is staged: `launch_staged_diff` for file 1.
7. Operator reviews + saves. `record_diff_decision`. Repeat serially for each file.

**Pass:** All N files accepted serially. Overlay compile reports zero errors at every stage. Vote-plus-hash classifies each as `accepted` or `accepted-normalized`.

**Fail:** Overlay compile errors before the first launch (one file's staging conflicts with another). Or rename is partial after the run (some callers untouched), meaning the WriteSet wasn't fully discovered.

---

### MF-002 — `remove_symbol` on a shared private field cascades correctly

**Hypothesis:** Removing a member used by N methods either (a) refuses cleanly with a "blocked by references" list, or (b) requires AI to stage corrective method-body edits in the same session first.

**Target:** A field in a `*Repository.cs` referenced by 2+ methods in the same class.

**Steps:**
1. Roslyn `find_references` to enumerate use sites.
2. Session, anchor.
3. Stage method-body changes via `submit_symbol` that no longer reference the field (e.g. inline the value or remove the use).
4. `remove_symbol` on the field.
5. `launch_staged_diff` serially. Accept all.

**Pass:** Final accepted state: field gone, every consumer method modified, file compiles. Operator reviews each diff in sequence.

**Fail:** `remove_symbol` succeeds while consumers still reference the field, overlay compile NOT catching the dangling references. OR `remove_symbol` silently rewrites consumer methods (architectural overreach — the server should not be modifying methods I didn't stage).

---

## OC — Overlay Compile Gating

### OC-001 — Stage a candidate with intentional CS error → `launch_staged_diff` requires force-review

**Hypothesis:** When `overlayValidation.hasErrors == true`, `launch_staged_diff` must consult the WinForms host for an explicit force-review decision before launching WinMerge.

**Target:** Any repo file. Inject a deliberate `int x = "string";` line.

**Steps:**
1. Session, anchor.
2. `submit_file` with the broken content. Confirm `overlayValidation.hasErrors == true` and the specific CS error is reported.
3. `launch_staged_diff(stagedRecordId)` with default `forceReviewOnOverlayErrors: false`.

**Pass:** Response indicates blocked-by-overlay-errors and that the host was asked. WinMerge does NOT open without explicit operator force-review. Either the response itself contains the gate result, or a host log entry confirms the prompt.

**Fail:** WinMerge opens unprompted despite overlay errors.

---

### OC-002 — Overlay validates across multi-file slice

**Hypothesis:** When a staged change in file A affects file B (consumer in same or different project), overlay compile reports the diagnostic against file B's line, not A's.

**Target:** Repository method rename + consumer in UI (use whatever pair `find_references` surfaces).

**Steps:**
1. Roslyn identify consumer file.
2. Session, anchor BOTH files.
3. `submit_symbol` rename on the data-side method ONLY. Do NOT stage the consumer fix.
4. Inspect `overlayValidation.diagnostics`.

**Pass:** Diagnostics include a CS-error pointing at the consumer file's call site (e.g. `CS0117: Type does not contain definition for 'NewName'`).

**Fail:** Overlay reports clean compile despite the consumer being broken. (Means the overlay slice is too narrow to be a real safety net.)

---

## VH — Vote-Plus-Hash Classification Edges

### VH-001 — Whitespace-only no-op → server refuses or marks no-op

**Hypothesis:** A candidate that differs from the source only in trailing whitespace or final newline (i.e. staged hash equals normalized original hash) is not enqueued as a normal diff per CLAUDE.md "no-op" rule.

**Target:** Any small file.

**Steps:**
1. Session, anchor. Record original content and hash.
2. `submit_file` with content that is the source plus one trailing space at end of file. Compute and confirm `stagedHash != originalHash` but `normalizedStagedHash == normalizedOriginalHash`.

**Pass:** Server either returns a `no-op` / `no-change` status and refuses to enqueue, or stages with a flag indicating no-meaningful-change.

**Fail:** Server stages a normal diff. Operator opens WinMerge to a "blank" diff. Accept/reject collapse to the same hash state, violating CLAUDE.md's "Accept and Reject cannot collapse" rule.

---

### VH-002 — Accept where staged is byte-equal to source after BOM/EOL normalization → `accepted-normalized`

**Hypothesis:** Already validated in Pass 2 rerun + Pass 3 this session. Repeat as a regression smoke test on a fresh file with mixed line endings.

**Target:** A file with mixed `\n` and `\r\n` (Pass 3 baseline confirmed `DatabaseRepository.cs` family has this).

**Steps:** Standard staging + accept flow.

**Pass:** `classification: "accepted-normalized"`, `decisionMatchesClassification: true`, `stagedNormalizedHash == currentNormalizedHash`.

**Fail:** Classification = `dirty-unexpected` (would indicate a regression on the BOM/EOL normalizer added in commit `05752b6`).

---

### VH-003 — `record_diff_decision` twice on the same record → idempotent or error

**Hypothesis:** The decision ledger is append-only or idempotent; a second call cannot change a recorded classification.

**Steps:**
1. Run a normal accept cycle. `record_diff_decision(accepted)`.
2. Call `record_diff_decision(rejected)` on the same `stagedRecordId`.

**Pass:** Second call either errors structurally ("decision already recorded") OR is silently idempotent (no state change). Either is acceptable; what's not is "second call overwrites first."

**Fail:** Second call overwrites the first decision. Audit trail integrity compromised.

---

## WI — Workflow Integrity (Adversarial)

### WI-001 — Skip `launch_staged_diff`, call `record_diff_decision` directly

**Hypothesis:** The server allows recording a decision without a prior `launch_staged_diff` because the operator might use an external diff tool. But it should at least flag the absence of a launch in the audit record.

**Steps:**
1. Stage something via `submit_file`.
2. `record_diff_decision(accepted)` WITHOUT calling `launch_staged_diff` first.

**Pass:** Either: (a) server refuses with a structured error requiring a launch first, OR (b) server records the decision but the decision record includes a flag like `reviewLaunchedExplicitly: false` so audits can spot it.

**Fail:** Decision is recorded with no indication that the diff was never launched. Skip-WinMerge becomes invisible.

---

### WI-002 — Direct write to watched source by some external tool, then `record_diff_decision`

**Hypothesis:** Vote-plus-hash catches when the watched file's `currentHash` matches `stagedHash` because something other than WinMerge wrote it. The classification will show `accepted` if hashes line up — meaning **the architecture cannot tell WinMerge-saved from any-other-write**. This test demonstrates the gap rather than catching it.

**Steps:**
1. Stage a candidate, capture `stagedHash`.
2. Do NOT launch the diff.
3. Outside the workflow, copy the staged file content over the watched file (simulating Claude writing directly via the `Write` tool).
4. `record_diff_decision(accepted)` without launching.

**Pass (demonstrates gap):** Classification = `accepted` and `decisionMatchesClassification: true`. This proves the bypass surface exists exactly as analyzed earlier in this session's evaluation, and that the only structural defense at this layer is WI-001's "did the launch happen" check.

**Fail (gap is closed):** Server refuses, indicating a deeper integrity check (e.g. requiring proof of a WinMerge process having run). Would be a hardening win.

---

### WI-003 — Stage two competing records on the same file in one session

**Hypothesis:** Concurrent or sequential stagings on the same file are tracked individually; only the last to be accepted writes to the watched file.

**Steps:**
1. Session, anchor.
2. `submit_file` candidate A. `submit_file` candidate B. Both should stage successfully.
3. `launch_staged_diff(A)`, operator accepts.
4. `launch_staged_diff(B)`, operator accepts.

**Pass:** Both decisions recorded. The final watched-file state matches B. Both records traceable in the ledger. Classification of B uses its own original-vs-staged hashes (where "original" is the post-A state).

**Fail:** Second staging overwrites the first without record. OR second `launch_staged_diff` fails because the file changed during A's accept.

---

## TS — Tool Surface Edge Cases

### TS-001 — `.razor` file is refused for symbol surgery

**Hypothesis:** Per CLAUDE.md "Razor Files" rule, Roslyn symbol-level operations don't apply to `.razor` or `.cshtml`. Test which tools enforce this.

**Target:** Any `.razor` file in the DBV2 UI tree. Glob for `*.razor` first.

**Steps:**
1. `get_source_map` on a `.razor` file.
2. `get_symbol` on a `.razor` symbol.
3. `submit_symbol` against a `.razor` symbol.

**Pass:** At least the staging call refuses with a structured "Razor unsupported" error. Read tools may succeed in degraded mode but should flag the file type.

**Fail:** `submit_symbol` accepts a Razor edit and stages it (would let Roslyn produce non-Razor-aware output against Razor source).

---

### TS-002 — `find_references` on a type used as a field type only

**Hypothesis:** Closes Finding 11. `find_references` should surface field/property/parameter/return-type declarations as type usages, not just member-access references.

**Target:** Any class with a usage like `private MyClass _instance;` somewhere.

**Steps:**
1. Identify a class via `search_symbols`.
2. `find_references` on the class.
3. Manually verify against `search_symbols` output that all field-typed usages appear.

**Pass:** `find_references` lists every field declaration whose type is this class. If the tool is intentionally limited to method accesses, the description must say so.

**Fail:** `find_references` returns empty while `search_symbols` shows real field usages of the type. Repro of Finding 11.

---

### TS-003 — `get_staging_guide` returns documented payload

**Hypothesis:** Closes the second condition for removing the "Skill Card Loading (Interim)" section in CLAUDE.md — that the rebuilt server's `get_staging_guide` returns the documented `SystemMonitorStaging.md` + `SessionOverlayValidation.md` payload.

**Steps:**
1. `get_staging_guide`.
2. Compare response to `Docs/Skills/SystemMonitorStaging.md` + `Docs/Skills/SessionOverlayValidation.md`.

**Pass:** Response includes the content of both cards, structured per the tool description. After pass, the interim section in CLAUDE.md can be removed.

**Fail:** Empty response, opaque error, or content mismatched against the on-disk skill cards.

---

### TS-004 — `submit_file` against new-path stages cleanly

**Hypothesis:** Closes Finding 6. After rebuild, `submit_file` for a path that doesn't exist yet (new file creation) returns a structured success with stagedRecord.

**Target:** A new path like `SchemaStudio.Data\TestArtifact_DELETE_ME.cs`.

**Steps:**
1. Session.
2. `submit_file(sessionId, "SchemaStudio.Data\TestArtifact_DELETE_ME.cs", "namespace SchemaStudio.Data; public class TestArtifact {}")`.
3. `launch_staged_diff`, operator rejects (don't actually create the artifact).

**Pass:** Staging returns success. Overlay compiles. `launch_staged_diff` opens WinMerge with a one-sided diff (new file). Reject completes cleanly.

**Fail:** Opaque error matching Finding 6's signature (`An error occurred invoking 'submit_file'`).

---

### TS-005 — Empty file submission

**Hypothesis:** Submitting empty content for an existing file is either refused (since it deletes the type) or stages as a legitimate whole-file blank with clear overlay errors at every consumer.

**Target:** `SchemaStudio.Data\DatabaseDomainRepositoryAsync.cs` (now redundant after Pass 2 rerun; safe to test on without affecting anything).

**Steps:**
1. Session, anchor.
2. `submit_file(sessionId, path, "")`.

**Pass:** Either (a) server refuses with structured error, or (b) stages successfully with overlay compile reporting "namespace not found" / "type not declared" at consumers. Either is acceptable behavior; what matters is non-opaque response.

**Fail:** Opaque error or silent acceptance.

---

## Suggested Test Order

For an initial smoke run, sequence:

1. **TS-003, TS-004** — confirm rebuilt-server tool surface is sound (closes Pass 1 Finding 2 final lap).
2. **SS-001, SS-002, SS-003** — symbol staging basics (the architecture's primary claim).
3. **DR-002** — chain of `submit_symbol` calls preserves surrounding bytes (drift defense empirical).
4. **RG-001, RG-002, RG-003, RG-004** — region introduction + region-targeted insertion (the new feature plane).
5. **MF-001, MF-002** — multi-file coupled edits including shared-member removal (real-world workflow).
6. **OC-001, OC-002** — overlay compile gating teeth.
7. **VH-001, VH-002, VH-003** — classification edges, no-op, idempotency.
8. **WI-001, WI-002, WI-003** — adversarial / trust-gap demonstrations. WI-002 in particular is expected to confirm a known gap; the result feeds Codex's hardening backlog.
9. **TS-001, TS-002, TS-005** — tool surface refusals and edge cases.

Each pass should adhere to the role doc: pre-flight verified, max 5 findings, max 150 words per finding, all results recorded in `CLAUDE_Live_Tests/STATUS.md` with a per-pass detail file under `CLAUDE_Live_Tests/Pass<N>_<Title>.md`.

## What This Test Catalog Is NOT

- **Not a unit-test suite for DBV2.** These tests probe the MonitorBaseClaude workflow against DBV2 source as the test substrate. The expected outcomes are workflow-level behaviors (classifications, overlay diagnostics, byte-level diffs), not DBV2 business logic.
- **Not exhaustive.** I've prioritized tests that prove architectural promises (SS, DR), exercise new features (RG), or surface trust gaps (WI). Other useful tests not in this catalog: scale tests (10+ files in one session), large-file tests (>50KB), generated-code interaction, source-generator edits.
- **Not adversarial in the security sense.** WI tests are about workflow-integrity gaps that the Operator should be aware of, not about a malicious Claude trying to hide actions. The trust model is documented; these tests measure it honestly.

## Open Questions for Codex Before Running

- Should WI-001 / WI-002 be run at all, given they explicitly probe trust gaps? My take: yes, once, to confirm the documented gap shape matches reality and to capture the result for the hardening backlog. Then never again (no value in re-confirming a known gap).
- Should RG tests run before or after region-aware typed insertion lands? My take: RG-001 and RG-004 can run today; RG-002 and RG-003 are blocked on the `region` parameter implementation per the Codex handoff.
- Are there DBV2 files that should be off-limits to these tests (production-critical, in-flight by another agent, etc.)? Recommend the Operator confirms a safe-to-mutate target list before the first symbol-staging pass.
