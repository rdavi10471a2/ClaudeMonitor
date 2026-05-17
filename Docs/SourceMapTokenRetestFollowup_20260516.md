# Source-Map Token Retest Follow-up - 2026-05-16

## Summary

Claude pushed branch `origin/VVG_LIVE_SourceMapRetest_20260516` with retest commit `75cbffa` and a claimed DBV2 reproducibility snapshot at:

`Docs/SchemaStudioDBV2_Snapshot_20260516.zip`

The branch should not be merged raw into `main`. It is based on older branch state and includes stale `.mcp.json`, manifest, source, and docs changes that predate PR #13. The useful artifact is the retest writeup, but the bundled snapshot is not a faithful directory snapshot.

## Snapshot Finding

The zip expands into a flattened tree. It includes DBV2 source files, but also flattened Git internals and loose-object-like files at the snapshot root. Directory structure for paths like `Data\...` and `UI\...` is lost.

Observed after extraction:

| Metric | Value |
| --- | ---:|
| Root files | 2,751 |
| `.cs` files | 78 |
| Extensionless files | 2,626 |
| Git-like files | 21 |

Examples of extra files:

```text
COMMIT_EDITMSG
config
description
HEAD
index
exclude
applypatch-msg.sample
pre-commit.sample
000ce068a3b1e35b9d9ef8b643883e8c263dc3
00173b21a609f465ed370c0952297e9ffb1bb1
```

Because the snapshot loses folders, it cannot be used for apples-to-apples folder/file retests.

## Repeat Against Extracted Snapshot

Codex repeated the requested source-map calls after replacing `C:\Schema Studio - DBV2` with the extracted snapshot. Monitor was stopped first, and the previous DBV2 folder was backed up before overwrite.

| Case | Result |
| --- | --- |
| project / navigation | `estimatedTokenProxy: 55,797`, `budgetLimit: 20,000`, `wasTruncated: true` |
| project / detail | `estimatedTokenProxy: 165,065`, `budgetLimit: 20,000`, `wasTruncated: true` |
| `Data` folder / navigation | errored because `Data` folder was missing after flattening |
| `UI` folder / navigation | errored because `UI` folder was missing after flattening |
| `Data\BaseTableRepository.cs` / selector | errored because directory path was missing |
| `SchemaObjectRepository.cs` / selector substitute | `fileCount: 1`, `symbolCount: 8`, `estimatedTokenProxy: 2,474`, not truncated |

The substitute file result is close to the previous Codex `Data\BaseTableRepository.cs` selector reference (`2,477`), which suggests the compact per-file selector cost is stable for similar repository-shaped files.

## Restored Local State

The malformed extraction was preserved as:

`C:\Schema Studio - DBV2.flattened-claude-snapshot-20260516_201209`

The pre-overwrite watched project was restored to:

`C:\Schema Studio - DBV2`

The backup source used for restoration was:

`C:\Schema Studio - DBV2.before-claude-snapshot-20260516_201209`

## Clean Local Reference Numbers

Against the restored DBV2 tree, using the current Monitor code and a temp-built MCP server:

| Case | Result |
| --- | --- |
| project / navigation | `estimatedTokenProxy: 23,707`, `budgetLimit: 20,000`, `wasTruncated: true` |
| project / detail | `estimatedTokenProxy: 70,352`, `budgetLimit: 20,000`, `wasTruncated: true` |
| `Data` folder / navigation | `fileCount: 6`, `symbolCount: 54`, `estimatedTokenProxy: 3,369`, not truncated |
| `UI` folder / navigation | `fileCount: 8`, `symbolCount: 93`, `estimatedTokenProxy: 5,192`, not truncated |
| `Data\BaseTableRepository.cs` / selector | `fileCount: 1`, `symbolCount: 8`, `estimatedTokenProxy: 2,477`, not truncated |

## Conclusion

The retest discrepancy is mostly environment state, not proof that PR #11 failed. Claude's branch measured a different and partly malformed snapshot. The clean local tree still shows the smaller PR #11 source-map behavior: whole-project navigation is much lower than the older `78,080` report, and folder/file reads are comfortably under budget.

For future reproducible retests, create the DBV2 snapshot with preserved relative paths and exclude `.git`, `bin`, `obj`, `.vs`, packages, and backup/archive folders unless the test explicitly intends to include them.
