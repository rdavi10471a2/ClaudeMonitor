---
title: CLAUDE_Live_Tests/Fixtures — canonical fixture templates
status: active
created: 2026-05-21
---

# Fixture templates

This directory holds canonical fixture-source templates for the watched smoke/test fixtures in the DBV2 solution. Templates are stored as `.cs.txt` (not `.cs`) so the C# compiler ignores them and they cannot accidentally enter the watched solution as a compilation unit.

## Why templates exist

Per the operator feedback on 2026-05-21:

> you shoudl be keeping yhoru fixtures in a safe place with a txt extnsion and then copying themm in as you need them when uyou start a test overt

Prior to this directory, test passes accumulated drift on the watched fixtures — each pass would mutate the fixture, sometimes leave it mutated, and the next pass would inherit a slightly different baseline. Pass-21's evidence note recorded that `McpAddApiFixture.cs` was at "post-Reg #1 state" before pass-21 even started, because earlier passes had not rolled back their changes.

The fix is **template-then-copy, not edit-then-revert**:

1. Each fixture has a canonical template here: `<FixtureName>.cs.txt`.
2. Test passes start with a `submit_file → stage → diff → accept` that copies the template content into the watched `.cs`. This restores the fixture to a known-good state before the pass begins, regardless of what previous passes left behind.
3. During the pass, mutate the watched fixture freely.
4. At pass close, do **not** revert. The next pass's template-copy step handles restoration.

The template-copy step is mediated by Monitor staging like any other watched-source change. Direct filesystem overwrites of watched source remain forbidden.

## Template hash registry

When a template is added or intentionally updated, record the canonical hash here so a pass can verify the template-copy succeeded by comparing watched hash to expected.

| Template | Watched path | Canonical hash | Notes |
|---|---|---|---|
| `McpAddApiFixture.cs.txt` | `SchemaStudio.SematicModel\Model\McpAddApiFixture.cs` | `6592eb30f00323fa5b9d4231bccd64ca7920ac0828137921c5a6e0e25ee76198` | 2-ctor + Nested-class shape. Captured 2026-05-21 after Pass 21 cleanup revert. Used for index-first chain rehearsals and `submit_symbol` end-to-end tests. |

## Adding a new template

1. Confirm the watched file is in its intended canonical state (read it, sanity-check the shape, verify the hash via `check_file_hash` or `find_indexed_symbols`).
2. Copy the content into `<FixtureName>.cs.txt` here.
3. Add a row to the registry above with watched path and canonical hash.
4. Use the template in the next pass's pre-flight.

## Updating an existing template

Templates are intentionally updated when the canonical fixture shape changes (e.g. adding a new symbol that all future passes should see). Update the `.cs.txt`, update the registry hash, and reference the pass evidence note that motivated the change.

Drift is not an update. If a pass leaves the watched fixture in an unintended state, the next pass should template-copy back to canonical, not update the template to match drift.
