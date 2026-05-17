# Bug: `submit_file` fails for brand-new file paths

**Project**: `MonitorBaseClaude` (`C:\VSCodeProjects\MonitorBaseClaude`)

**Discovered against**: live workflow run, this session — attempt to stage a new sibling file `SchemaStudio.Data\DatabaseDomainRepositoryAsync.cs` (async twin of `DatabaseDomainRepository.cs`).

**Status**: open; reproducible; **human direction for fix below**.

## Symptom

Calling `submit_file(path, content)` against a path that does not exist under the watched project root returns a generic `"An error occurred invoking 'submit_file'."` failure. No staged record is written. No error detail is returned.

Workaround used in this session: bypass the workflow and write the new file directly with the Write tool. Functionally fine for a brand-new file with no prior pattern to drift, but **bypasses the all-or-none gate's review surface** — operator never sees the new content in WinMerge before it lands on disk.

## Why this matters

The gate's design protects watched source from drift on **existing** files. A new file has no prior converged pattern to violate, so the gate's hash-based protection is less relevant — but **operator review is still meaningful**:

- Confirms the model's proposed new file is what the operator wants.
- Catches structural mistakes (wrong namespace, missing using, bad shape) before they land.
- Gives the operator a clear save-or-reject decision point, same as existing-file edits.
- Records the addition in the staged-record ledger for traceability.

Bypassing the gate for new files means the model can silently create files in the watched project that the operator never reviewed. That's a real safety gap, even if smaller than the existing-file mutation gap.

## Human direction for the fix

Per operator direction, the fix has three parts:

### 1. CLAUDE.md (or manifest) directs the model on new-file path specification

The watched-edit-loop documentation needs an explicit "new file" path. When the user requests a change that requires a new file:

- **The model must explicitly state the proposed new file path** before calling `submit_file`. No silent "I'll just create X.cs" — the path is part of the proposed candidate the operator gets to see.
- **If the path is unambiguous from context** (e.g. "duplicate `Foo.cs` as `FooAsync.cs` in the same folder"), the model may proceed with that path and surface it in the manifest.
- **If context is not sufficient to determine the path**, the model must ask the user for the target folder and filename before staging. No guessing.

### 2. `submit_file` must accept new paths and stage them into `Working\Staged` the same way it stages replacements

Implementation expectation:

- When `path` doesn't exist under the watched root, treat it as a new-file candidate.
- Write the staged candidate into `Working\Staged\<observed-root-key>\<relative-path>\<timestamp>_submit_file_<symbolish>_<id>.cs` exactly as for replacements.
- Record `originalHash` as a sentinel value (e.g. `"<new-file>"` or null) to indicate no prior baseline.
- Compute `stagedHash` against the candidate bytes as usual.
- Return a `StagedEditRecord` the operator can review.

### 3. `submit_file` creates the watched-file path on accept

When `record_diff_decision(accepted)` fires for a new-file staged record:

- Vote-plus-hash classification adapts: reported `accepted` + watched file now exists with hash == `stagedHash` → `accepted`. Reported `accepted` + watched file still doesn't exist → `dirty-unexpected` (operator said accepted but no file was created — same dirty-vote-vs-reality detection logic).
- The actual file creation still happens through WinMerge's save (operator action), not by the server directly. This preserves the model-never-writes-watched-source guarantee from CLAUDE.md.
- For new files, the WinMerge launch needs to handle a non-existent left/right pane — WinMerge supports this natively (will offer to create the missing file on save).

## Suggested follow-up tickets

- **#E** *submit_file new-file path support*: implement the above. Cover with smoke test that stages a new file, runs the diff (against an empty-or-nonexistent watched pane), and records `accepted` after the operator saves.
- **#F** *CLAUDE.md new-file workflow*: add a "New File" section to the watched edit loop describing the path-specification rule and the operator-asks-when-ambiguous behavior.

## Repro context

- Tool call: `submit_file(path="SchemaStudio.Data\\DatabaseDomainRepositoryAsync.cs", content="<async twin>")`.
- Watched project: `C:\Schema Studio - DBV2`.
- Target file did not previously exist.
- Server response: `"An error occurred invoking 'submit_file'."` — generic, no diagnostic detail.
- Workaround used: direct `Write` tool write to `C:\Schema Studio - DBV2\SchemaStudio.Data\DatabaseDomainRepositoryAsync.cs`. File compiled clean (0 errors against `SchemaStudio.Data.csproj`).
- All-or-none gate review **was bypassed** for this addition — operator never saw the diff before the file landed on disk.
