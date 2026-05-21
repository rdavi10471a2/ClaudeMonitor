---
status: new
type: test-result
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

# Task: Verify Indexed Callers After `55f5108`

Pull latest `main` and verify commit `55f5108` or newer is present.

```powershell
git pull origin main
git rev-parse --short HEAD
dotnet build .\MonitorBaseClaude.slnx /p:UseAppHost=false
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --dbv2-index-callers
```

Expected smoke result:

```text
Passed: True
Target method/constructor count: 30
Fully matched target count: 30
Failure count: 0
```

This smoke rebuilds the DBV2 solution index, independently walks DBV2 source with Roslyn semantic binding, and compares expected call sites against SQLite-backed `FindCallers` results for:

- `Data/*Repository.cs` methods and constructors
- `Services/SchemaDiscovery.cs` methods and constructors

There is no parser library present in the current DBV2 checkout, so do not block this verification on parser-specific files.

## Live Tool Checks

After the smoke passes, run the live MCP/UI path against the same index. At minimum, verify these stable keys return callers:

```text
Data\BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::GetByDatabase(int)
Data\DataBaseRepository.cs::SchemaStudio.Data::DatabaseRepository::method::Insert(DatabaseDefinition)
Data\BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::Insert(BaseTableDefinition)
```

Expected callers include:

```text
EditorSurface\ExplorerControl.cs / InitializeLayout
UI\BaseTableEditorForm.cs / LoadData
Data\DataBaseRepository.cs / SaveAll
Data\BaseTableRepository.cs / SaveAll
UI\BaseTableEditorForm.cs / InitializeComponentCustom
```

Also verify slash-normalized input works by trying at least one equivalent key with `/` separators:

```text
Data/BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::GetByDatabase(int)
```

## Report Back

If the smoke passes but live MCP or the WinForms Solution Index tab fails, create a new dated finding with:

- exact stable key used
- whether the input used `\` or `/`
- actual caller rows returned
- selected symbol `SourceAnchor`
- index counts shown by status
- whether the UI selected row auto-loaded caller results or required pressing the button

If both smoke and live checks pass, create a compact dated `test-result` note confirming the command output and the live caller rows.
