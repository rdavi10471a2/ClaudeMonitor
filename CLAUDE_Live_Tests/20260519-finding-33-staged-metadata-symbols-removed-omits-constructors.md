---
status: new
type: finding
created: 2026-05-19
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Title

`stage_candidate_for_review.serverDerivedMetadata.symbolsRemoved` omits constructors that were actually removed

## Severity

confusing

## File/tool

`monitor-base-claude` MCP — `stage_candidate_for_review`'s response payload, specifically `serverDerivedMetadata.symbolsRemoved`. The candidate file itself is correct; only the audit metadata is wrong.

## Observed

Pass 14 session `monitor-20260519140517-e8bc275066a040f0b` called `remove_symbol` six times against `McpAddApiFixture.cs`:

1. `remove_using(System.Linq)`
2. `remove_symbol(Method_Remove, method)`
3. `remove_symbol(McpAddApiFixture, constructor, parameterTypes: ["string"])`
4. `remove_symbol(_field_Remove, field)`
5. `remove_symbol(Property_Remove, property)`
6. `remove_symbol(Nested_Remove, class)`

Every individual call returned `status: candidate-updated` with overlay clean. `stage_candidate_for_review` then returned `serverDerivedMetadata.symbolsRemoved` listing only four entries:

```json
[
  {"name":"_field_Remove","kind":"field"},
  {"name":"Property_Remove","kind":"property"},
  {"name":"Method_Remove","kind":"method"},
  {"name":"Nested_Remove","kind":"class"}
]
```

The constructor removal is missing. `usingsRemoved: ["System.Linq"]` was correctly populated. The candidate file content was correct — the staged file at `Working\Staged\...\20260519_090626358_..._c50ee2f1.cs` clearly shows the parameterless ctor only; the `(string parm_Remove)` overload is gone. After `record_diff_decision(accepted)`, the watched file likewise carries only the parameterless ctor and the project compiles.

## Expected

`symbolsRemoved` should enumerate every removed symbol regardless of kind, mirroring the add-side behavior. Pass 13's `symbolsAdded` (same fixture, sibling pass `monitor-20260519140105-cdf28125ff0749d49`) correctly listed both constructor overloads with `kind: "constructor"`:

```json
{"name":"McpAddApiFixture","kind":"constructor","startLine":11,"endLine":11,...}
{"name":"McpAddApiFixture","kind":"constructor","startLine":12,"endLine":12,...}
```

The asymmetry (constructors enumerated on add, omitted on remove) means `symbolsRemoved` is an unreliable audit trail when a candidate touches constructors — exactly the case where audit clarity matters most.

## Minimal fix

In the server's `BuildServerDerivedMetadata` (or equivalent), the diff that emits `symbolsRemoved` is likely filtering by member kind and missing the constructor branch. Add `constructor` to the kinds enumerated, the same way the add-side already does. A single code-path change should resolve.

## Evidence

- Pass 14 step 3 response (op `remove_symbol` constructor): `status: candidate-updated`, `operationCount: 3`, `candidateHash: f3cc43f9...`. No error.
- Staged file at `Working\Staged\Schema Studio - DBV2_6c4e124c9922\SchemaStudio.SematicModel\Model\20260519_090626358_..._c50ee2f1.cs` — 11 lines, parameterless ctor only on line 8. The `(string parm_Remove)` overload is absent.
- `stage_candidate_for_review` response `serverDerivedMetadata.symbolsRemoved` array has 4 entries; the constructor entry is not present.
- `record_diff_decision(accepted)` returned `classification: accepted` (exact byte match), confirming the watched file now matches the staged candidate (which already had the ctor removed).
- Pass 13 comparison data point: `serverDerivedMetadata.symbolsAdded` for the sibling add-walk correctly listed both constructors. Same file, same fixture, opposite operation kind.
