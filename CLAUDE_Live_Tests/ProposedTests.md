# Proposed Tests — DEPRECATED

The previous content of this file has been removed at Operator direction during Pass 4 (2026-05-17).

**Framing error:** the prior catalog drifted toward testing DBV2's API surface and feature semantics, using DBV2 files as the *subject* of tests rather than as the *substrate* on which Monitor-workflow tests run. Operator: "I was asking for proposals to test the workflow, not the API interface of DBV2." The previous content also added test ideas surfaced by the Pass 4 deep dive that, on reflection, are DBV2-feature observations (dead `Binding/` classes, disposable misuse, complexity-40 method) rather than Monitor-workflow probes.

**Recover the prior content** via `git log -- CLAUDE_Live_Tests/ProposedTests.md` if any of it is salvageable as workflow-test scaffolding.

**Next session — correct frame:** propose tests **only** for the Monitor MCP workflow itself. Each test should answer one of:

- Does the staging surface preserve byte-level integrity outside the deliberate edit window?
- Does overlay validation correctly accept clean candidates and reject broken ones?
- Does vote-plus-hash classification correctly distinguish accepted / accepted-normalized / rejected / dirty-unexpected?
- Does the session bag correctly couple multi-file edits without a pre-declared WriteSet?
- Does `launch_staged_diff` correctly gate on overlay errors / WinMerge availability?
- Do the Roslyn discovery tools surface enough to identify a WriteSet, or do they produce false-negatives that cause undersized sessions (cf. Finding 11, Finding 15)?

DBV2 files appear in tests only as test *inputs* — the choice of input file should not turn the test into a DBV2-feature probe.

Open until Operator + Codex agree on the rewrite scope.
