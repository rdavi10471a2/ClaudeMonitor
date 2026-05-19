---
status: fixed
type: test-result
created: 2026-05-19
processed: true
processedBy: Codex
processedAt: 2026-05-19
resolution: Verified fixed in Pass 19 against fresh binaries (11:44). Force WinMerge Review button now drives validationGateDecision=force_review and launches WinMerge; Cancel Review still drives cancel_for_fix. See 20260519-pass19-test-result-finding-35-36-37-verified.md.
resolutionCommit: 0102077
---

## Summary

Finding 35 (`20260519-finding-35-host-force-review-button-may-have-mismatched-action.md`) is fully reproduced. The Host's `Force WinMerge Review` dialog button does not result in WinMerge opening, and the server records `validationGateDecision: cancel_for_fix` regardless of which dialog button the Operator clicks. The server-side tool argument `forceReviewOnOverlayErrors: true` correctly produces `validationGateDecision: force_review` and launches WinMerge — so the bug is squarely in the Host's overlay-error dialog wiring, not in the server.

## Repro

Session `monitor-20260519151325-d279e472530b41bc9`. Disposable broken fixture `McpOverlayGateFixture.cs` staged with a guaranteed CS0103 (`UndefinedSymbol`/`UndefinedSymbol2` reference).

Dialog exact labels (operator screenshot): title `Overlay validation failed: <stagedRecordId>`; body shows the CS0103 diagnostic followed by the phrase "Open WinMerge anyway? Choose Yes only when… Choose No to return the diagnostics to the agent for a fix." Buttons: `Cancel Review` (default focus, left) and `Force WinMerge Review` (right). Note the body text is in Yes/No phrasing while the buttons are labeled Cancel/Force — minor documentation drift, but unambiguous after a read.

## Results matrix

| Trial | Record | Operator clicked | Server `validationGateDecision` | WinMerge opened |
| --- | --- | --- | --- | --- |
| 1 | 303aeae5 (run 1, first launch) | Force WinMerge Review | `cancel_for_fix` | No |
| 2 | 303aeae5 (run 1, second launch) | Force WinMerge Review | `cancel_for_fix` | No |
| 3 | 303aeae5 with `forceReviewOnOverlayErrors: true` | (no dialog — server bypass) | `force_review` | **Yes** |
| 4 | 6462f21e (run 2, first launch) | Cancel Review | `cancel_for_fix` | No |
| 5 | 6462f21e (run 2, second launch) | Force WinMerge Review | `cancel_for_fix` | No |
| 6 | 6462f21e (run 2, third launch) | Force WinMerge Review | `cancel_for_fix` returned **before** click | No |

## Expected

- `Cancel Review` → `validationGateDecision: cancel_for_fix`, WinMerge not launched. **Met (trial 4).**
- `Force WinMerge Review` → `validationGateDecision: force_review`, WinMerge launched. **Not met (trials 1, 2, 5, 6).**

## Actual

`Force WinMerge Review` always produced `cancel_for_fix` and WinMerge did not open. The button is functionally equivalent to `Cancel Review` from the server's perspective.

## Secondary observation

On the third launch of a record already in `queueStatus: blocked-overlay-validation` (trial 6), the server returned `cancel_for_fix` **before** the Operator clicked anything in the dialog. The dialog still appeared on screen and the Operator clicked Force, but nothing happened afterwards. This suggests subsequent `launch_staged_diff` calls against an already-blocked record short-circuit on the server but the Host still displays a (now-meaningless) dialog. Worth investigating as part of the same Host-side fix.

## Root cause (read-only code inspection)

Located in [McpProxyHubService.cs](McpProxyHubService.cs):

- [McpProxyHubService.cs:287-293](McpProxyHubService.cs#L287-L293) — `cancelButton.DialogResult = DialogResult.Cancel`.
- [McpProxyHubService.cs:295-301](McpProxyHubService.cs#L295-L301) — `forceButton.DialogResult = DialogResult.OK`.
- [McpProxyHubService.cs:224](McpProxyHubService.cs#L224) — `bool forceReview = result == DialogResult.Yes;`

The check on line 224 compares against `DialogResult.Yes`, but neither button is wired to `Yes`. The Force button returns `DialogResult.OK` and the Cancel button returns `DialogResult.Cancel`. Neither matches `Yes`, so `forceReview` is always `false`, which always returns `decision: cancel_for_fix` on [line 228](McpProxyHubService.cs#L228). Cancel produces the intended outcome by coincidence; Force is silently inert.

The body text on [lines 217-218](McpProxyHubService.cs#L217-L218) is written in Yes/No phrasing ("Choose Yes only when…"), suggesting the original intent was `DialogResult.Yes`/`DialogResult.No` buttons. The button labels and DialogResult values drifted from that intent.

## Minimal fix

Either of these one-line changes resolves the headline bug:

- Option A — align buttons with the existing comparison: [line 298](McpProxyHubService.cs#L298) `DialogResult = DialogResult.OK` → `DialogResult.Yes`, and (for symmetry/intent) [line 290](McpProxyHubService.cs#L290) `DialogResult = DialogResult.Cancel` → `DialogResult.No`. Body text already matches.
- Option B — align comparison with the existing buttons: [line 224](McpProxyHubService.cs#L224) `result == DialogResult.Yes` → `result == DialogResult.OK`. Then update body text on [lines 217-218](McpProxyHubService.cs#L217-L218) to refer to "Cancel Review" / "Force WinMerge Review" instead of "Yes" / "No".

Option A is the smaller diff and preserves the existing body text wording.

Secondary fixes (separate, not blocking):
1. For records already in `queueStatus: blocked-overlay-validation`, either suppress the dialog entirely on subsequent `launch_staged_diff` calls or let the Operator's click drive a fresh server decision. Currently the server short-circuits while the Host still shows a dialog that has no effect (trial 6).
2. Optional cosmetic: keep body text and button labels in lock-step regardless of which option above is chosen.

## Evidence

- Staged records: `20260519_102111321_..._303aeae5` (run 1), `20260519_102458813_..._6462f21e` (run 2).
- Bypass-path response (trial 3): `status: winmerge-launched`, `processId: 29148`, `validationGateDecision: force_review`, `validationGateMessage: "Overlay compile errors were force-reviewed before WinMerge launch."`
- Dialog-path responses (trials 1, 2, 4, 5, 6): all identical — `status: overlay-errors-review-cancelled`, `validationGateDecision: cancel_for_fix`, `validationGateMessage: "Operator cancelled review so the agent can fix overlay compile errors first."`
- Operator screenshot of the dialog captured during trial 4 confirms the exact button labels above.
- Watched source after the run: `C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Model\McpOverlayGateFixture.cs` is **not** present (clean — the broken fixture never made it into watched source).
