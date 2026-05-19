---
status: new
type: finding
created: 2026-05-19
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
needsOperatorReview: true
---

## Title

Host force-review dialog may have a mismatch between the button the Operator clicked and the action the server recorded — Operator believes they clicked "go to review" but the server saw `cancel_for_fix`

## Severity

needs-investigation (potential blocker if confirmed; not yet reproduced)

## File/tool

WinForms Host's force-review dialog (the modal that appears when `launch_staged_diff` detects `overlayValidation.hasErrors: true`). Specifically the button row that lets the Operator pick `force_review` vs `cancel_for_fix`. The MCP-side server contract is unaffected by this finding.

## Observed

Pass 16 step D fired `launch_staged_diff` against a staged record with `overlayValidation.status: compiled-with-errors` (a dangling `_value` reference after a deliberate field removal). The expected flow was: Host modal pops up → Operator picks one of two buttons → server records the choice as `force_review` or `cancel_for_fix`.

Operator reported: "*I swear I clicked go to review*" — i.e., the intent was to bypass the overlay error and proceed to WinMerge.

Server response was unambiguously the opposite: `status: overlay-errors-review-cancelled`, `validationGateDecision: cancel_for_fix`, `validationGateMessage: "Operator cancelled review so the agent can fix overlay compile errors first."`, `nextStep: "Review was not launched..."`. WinMerge did not open.

Three non-exclusive possibilities:

1. **Button label/position mismatch** — e.g., labels reversed, default focus on the wrong button, the visually-prominent button maps to the safer (cancel) action while the "review anyway" button is less prominent, or two buttons look similar enough to misclick under attention pressure.
2. **Confirmation step inverted** — e.g., the dialog asks "Cancel review for fix?" Yes/No, and "Yes" was read as "yes review it" but mapped to `cancel_for_fix`. (Mirror-mode UI bug.)
3. **Genuine misclick** — Operator clicked a different button than they intended, no UI bug.

Operator said they will "*review* that later" — i.e., reproduce and inspect the actual button labels and default focus. This finding records the moment so the investigation isn't lost.

## Expected

The button the Operator clicks should produce the server decision the Operator expected. Specifically:

- A "Review anyway" (or "Force review") button → server sees `force_review`, WinMerge launches.
- A "Cancel for fix" (or "Stop and let me fix") button → server sees `cancel_for_fix`, WinMerge does not launch.

If the dialog uses different labels, those labels should unambiguously map to those server-side semantics, and the more-destructive option (force-review of broken code) should not be the default.

## Minimal fix (if reproduced)

Audit the WinForms force-review modal's button bindings: confirm the button captioned for "proceed to review" wires to the `force_review` decision and the button captioned for "let the agent fix first" wires to `cancel_for_fix`. If they are swapped, swap the bindings. If the labels are ambiguous, rename to the explicit form above. Default focus should be on the cancel-for-fix button (safer outcome on accidental Enter).

## Evidence

- Pass 16 step D request payload: `launch_staged_diff(stagedRecordId: 20260519_092612389_..._38adf30c)` at 2026-05-19 ~14:26:17Z.
- Pass 16 step D response payload: see `STATUS.md` Pass 16 section, the table row "D" carrying `status: overlay-errors-review-cancelled`, `validationGateDecision: cancel_for_fix`. Server response timing relative to Operator-reported click is consistent with a single round-trip — Operator's perception and server record do not agree.
- Decision record persisted at: this dialog interaction is reflected in the staged record's `queueStatus: blocked-overlay-validation` post-launch, recorded by `list_session_staged_records` in the same Pass 16 session.

## Operator follow-up

Operator reproducing locally: bring up the force-review dialog again (any broken candidate; the Pass 16 fixture path works), screenshot the button row, and either confirm the button labels match the produced decision (closes this finding as a one-off misclick) or document the mismatch (escalates to a real UI bug with this finding as the breadcrumb).
