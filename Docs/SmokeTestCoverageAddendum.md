# Smoke Test Coverage Addendum

Generated: 2026-05-16

This is an addendum to `MonitorBaseClaude_SmokeTestCoverageReviewPackage.zip`.

## Correction: `serverDerivedMetadata`

The earlier claim that `serverDerivedMetadata` is empty in all symbol cases is not correct for the fresh 2026-05-16 Roslyn surgery smoke.

Confirmed behavior:

- `add_using` populates `usingsAdded`.
- `remove_using` populates `usingsRemoved`.
- `add_symbol` populates `symbolsAdded` with the added `HasName` method.
- `remove_symbol` populates `symbolsRemoved` with the removed `HasName` method.
- `submit_symbol` replacement leaves `symbolsAdded` and `symbolsRemoved` empty.

That `submit_symbol` behavior is reasonable for the current metadata model because replacing an existing method preserves the symbol identity. It is not an add/remove operation.

The real future question is whether `submit_symbol` should add a separate `symbolsChanged` or `symbolsReplaced` metadata field, possibly including:

- symbol name
- symbol kind
- old text hash
- new text hash
- selector used
- line-span movement

This is useful for future post-decision symbol verification and telemetry, but it is not a current vote-plus-hash safety blocker.

## Revised Coverage Gap

Replace this earlier gap:

```text
WEAK - submit_symbol serverDerivedMetadata is empty in all cases
```

with:

```text
WEAK - submit_symbol replacement has no changed-symbol metadata
```

Current add/remove metadata is proven. Replacement metadata is simply not represented as a first-class changed-symbol concept yet.

## Revised Next Smoke Priorities

Priority 1: Syntax-error rejection smoke

- Submit a C# candidate with deliberate syntax errors.
- Prove the server rejects it before staging, or explicitly document current behavior if staged syntax-error candidates remain intentional.
- If strict rejection is the desired contract, prove no staged record is written for invalid C#.

Priority 2: Dirty-unexpected recovery smoke

- Create a `dirty-unexpected` classification.
- Run the intended Host/operator recovery path.
- Prove a new staging operation can proceed only after explicit recovery/refresh/inspection.

Priority 3: Submit-symbol changed-metadata smoke

- Replace a method with `submit_symbol`.
- Verify whether future metadata reports a changed/replaced symbol distinctly from add/remove.
- This is telemetry/post-decision-verification coverage, not core accept/reject safety coverage.

## What Remains Unchanged

The deterministic coverage verdict remains strong:

- Vote-plus-hash truth table is covered for v1.
- Dirty mismatch blocking is covered.
- C# staged add/remove symbol and using metadata is covered.
- Razor safe-mode behavior is covered.
- Source-map navigation to selector to `get_symbol` hierarchy is covered.
- Real DBV2 source-map read-only navigation is covered.

The system remains ready for `MCP_CLIENT_TESTING.md` Pass 1 against a real Claude account.

Do not treat this addendum as a request for architecture churn. The next work should stay narrow.
