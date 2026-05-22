# Review Queue And Gates

Use when launching WinMerge, handling overlay validation errors, or moving through a multi-file review queue.

## Rules

- WinMerge is a Host-owned review/save surface.
- One GUI diff at a time.
- Closing WinMerge is not a decision.
- `record_diff_decision` is called only after the Operator reports accepted or rejected.
- Accept means the full staged candidate was saved into watched source.
- Reject means watched source was left unchanged.
- After an accepted decision, check the `IndexRefresh` status returned by `record_diff_decision` before relying on solution-index queries.
- In a multi-file session, accepted decisions may defer index refresh until the review chain is complete; do not force a manual refresh unless `IndexRefresh` reports a failure or explicitly requires it.

## Overlay Gate

If overlay compile validation has errors, `launch_staged_diff` asks the WinForms Host before WinMerge opens.

- `Cancel Review`: no WinMerge launch; return diagnostics to the agent.
- `Force WinMerge Review`: explicit override; WinMerge opens.
- Host unavailable: machine block, not an Operator decision.

## Queue Stop

Any not-launched review result stops the current queue:

- overlay errors cancelled
- Host unavailable
- source/staged file missing
- WinMerge missing
- dirty-unexpected
- review-chain-blocked

Do not open later diffs until the blocked item is corrected, force-reviewed, or the session is abandoned.
