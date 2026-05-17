# Oracle Formatting Fixture Corpus

This file is the backlog of known-answer fixtures for System Monitor insertion and replacement behavior. These are not model-reasoning tests. They are product-oracle tests: given a known edit intent, the monitor should stage clean, localized, diff-friendly C#.

Each fixture should record:

- starting shape
- requested edit
- correct insertion/replacement anchor
- final ordering assertion
- local trivia/spacing rule discovered
- what must not move

## Fixture 1: Related Property Insertion

Purpose: add a public property inside an existing property group without collapsing spacing.

Starting shape:

```csharp
public bool EnableAuditLogging { get; set; }

public bool IncludeArchivedCustomers { get; set; }

public int CommandTimeoutSeconds { get; set; }

public string DefaultSortColumn { get; set; }
```

Task:

```text
Add public bool IncludeInactiveCustomers { get; set; } near the related customer filtering properties.
```

Correct anchor:

```text
afterSymbol: IncludeArchivedCustomers
```

Expected ordering:

```text
EnableAuditLogging
IncludeArchivedCustomers
IncludeInactiveCustomers
CommandTimeoutSeconds
DefaultSortColumn
```

Oracle rules:

- preserve blank line before and after inserted property
- do not move existing properties
- do not move constructor or methods

Finding: Roslyn formatting alone fixes indentation but does not infer blank-line rhythm. The monitor must carry neighbor trivia into the inserted member.

## Fixture 2: Constructor Overload Insertion

Purpose: add a constructor overload near existing constructors, not near public methods.

Starting shape:

```csharp
private readonly string _connectionString;
private readonly int _commandTimeoutSeconds;

public CustomerRepository(string connectionString)
{
    _connectionString = connectionString;
    _commandTimeoutSeconds = 30;
}

public CustomerRepository(string connectionString, int commandTimeoutSeconds)
{
    _connectionString = connectionString;
    _commandTimeoutSeconds = commandTimeoutSeconds;
}

public List<CustomerRecord> GetAllCustomers()
{
    return [];
}
```

Task:

```text
Add a constructor overload CustomerRepository(string connectionString, TimeSpan timeout) that converts timeout.TotalSeconds to an int.
```

Correct anchor:

```text
afterSymbol: CustomerRepository(string,int)
```

Expected ordering:

```text
fields
CustomerRepository(string)
CustomerRepository(string,int)
CustomerRepository(string,TimeSpan)
GetAllCustomers
```

Oracle rules:

- constructor overload goes with constructor group
- existing constructor order is preserved
- public methods remain below constructors

Finding to verify: constructor selectors need parameter types for reliable overload anchoring.

## Fixture 3: Private Helper Insertion

Purpose: add a private helper near private helpers at the bottom.

Starting shape:

```csharp
public List<CustomerRecord> GetAllCustomers()
{
    string sql = BuildCustomerQuery();
    return Execute(sql);
}

private string BuildCustomerQuery()
{
    return "SELECT * FROM Customers";
}

private List<CustomerRecord> Execute(string sql)
{
    return [];
}
```

Task:

```text
Add a private static string NormalizeSortColumn(string sortColumn) helper and update nothing else.
```

Correct anchor:

```text
afterSymbol: BuildCustomerQuery
```

Expected ordering:

```text
GetAllCustomers
BuildCustomerQuery
NormalizeSortColumn
Execute
```

Oracle rules:

- helper stays in private helper region/cluster
- no public method movement
- no body edits unless explicitly requested

Finding to verify: when `afterSymbol` points at a private helper, inserted helper inherits helper-group spacing.

## Fixture 4: Method Body Replacement Only

Status: implemented as `--fixture-method-replacement-smoke`.

Purpose: replace one method while preserving its location and neighboring members.

Starting shape:

```csharp
public List<TableRecord> GetAllTables()
{
    const string sql = "SELECT * FROM Tables";
    return Query<TableRecord>(sql);
}

public List<TableRecord> GetActiveTables()
{
    const string sql = "SELECT * FROM Tables WHERE Active = 1";
    return Query<TableRecord>(sql);
}
```

Task:

```text
Replace GetAllTables so it uses SqlQueries["AllTables"]. Do not change GetActiveTables.
```

Correct selector:

```text
GetAllTables()
```

Expected replacement:

```csharp
public List<TableRecord> GetAllTables()
{
    return Query<TableRecord>(SqlQueries["AllTables"]);
}
```

Oracle rules:

- leading and trailing trivia from original `GetAllTables` is preserved
- `GetActiveTables` remains unchanged
- no whole-file formatting

Finding: selector-based whole-member replacement is the right edit unit for method body surgery.

Additional finding: the fixture must include any support symbol used by the expected replacement. A replacement that formats correctly but references missing `SqlQueries` is still a failed oracle because overlay compile validation should matter.

## Fixture 5: Field Insertion In Field Group

Status: implemented as `--fixture-field-insertion-smoke`.

Purpose: add a private readonly field near existing fields.

Starting shape:

```csharp
private readonly string _connectionString;
private readonly ILogger _logger;

public CustomerRepository(string connectionString, ILogger logger)
{
    _connectionString = connectionString;
    _logger = logger;
}
```

Task:

```text
Add private readonly IClock _clock; and do not update constructor yet.
```

Correct anchor:

```text
afterSymbol: _logger
```

Expected ordering:

```text
_connectionString
_logger
_clock
constructor
```

Oracle rules:

- field remains in field group
- constructor does not move
- no constructor body change unless requested

Finding to verify: field symbol names and metadata should support variable-name anchoring.

Finding: variable-name anchoring works for single-variable field declarations, for example `afterSymbol: _logger`. Compact field groups should remain compact while preserving the blank line before the constructor.

## Fixture 5b: Field Removal With Dependents

Purpose: prove that removing a field while constructor assignments remain is unsafe and must surface diagnostics.

Starting shape:

```csharp
private readonly IClock _clock;

public CustomerRepository(string connectionString, ILogger logger, IClock clock)
{
    _clock = clock;
}
```

Task:

```text
Remove only the _clock field and verify the staged candidate reports the remaining dependent assignment.
```

Oracle rules:

- source map must resolve `_clock`
- `remove_symbol` stages the field-only candidate
- overlay validation reports a compile error such as CS0103 for `_clock`
- the fixture must not accept the broken candidate into watched source

Finding: removal needs a planning layer. The low-level `remove_symbol` can remove the exact field correctly, but a safe agent/tool flow must discover dependents and either stage coupled cleanup candidates in the same session or block before review.

## Fixture 6: Using Insertion

Purpose: add one using without disturbing project/style order.

Starting shape:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

using SchemaStudio.Models;
```

Task:

```text
Add using Microsoft.Data.SqlClient.
```

Expected result:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.SqlClient;
using SchemaStudio.Models;
```

Oracle rules:

- preserve the System/non-System grouping if present
- do not collapse blank line between using groups
- do not duplicate existing usings

Finding to verify: current `add_using` alphabetical sorting may be too crude when files use grouped usings.

## Fixture 7: Nested Type Insertion

Purpose: add a nested type at the bottom of the containing type, after helpers.

Starting shape:

```csharp
public List<CustomerRecord> GetAllCustomers()
{
    return [];
}

private static string NormalizeName(string value)
{
    return value.Trim();
}
```

Task:

```text
Add a private sealed class QueryOptions nested type with IncludeInactive and SortColumn properties.
```

Correct anchor:

```text
afterSymbol: NormalizeName
```

Expected ordering:

```text
GetAllCustomers
NormalizeName
QueryOptions
```

Oracle rules:

- nested type goes near bottom, not between public methods
- nested type body is formatted in file context
- existing helper spacing is preserved

Finding to verify: nested type insertion should use `symbolType: class` and still honor local member-group trivia.

## Fixture 8: Async Propagation Boundary

Purpose: validate planning guidance and staged edit set boundaries.

Starting shape:

```text
DatabaseRepository.GetDatabaseNames()
DatabaseService.LoadDatabaseNames()
DatabaseManagerForm.RefreshButton_Click(...)
```

Task:

```text
Make repository retrieval async and update callers. Stop async propagation at the UI event handler boundary.
```

Expected signatures:

```csharp
Task<List<string>> DatabaseRepository.GetDatabaseNamesAsync()
Task<List<string>> DatabaseService.LoadDatabaseNamesAsync()
async void DatabaseManagerForm.RefreshButton_Click(...)
```

Oracle rules:

- repository and service method edits are separate staged candidates in one monitor session when they are coupled
- UI event handler remains event-compatible
- callers are updated using references/callers analysis, not grep-only discovery

Finding to verify: this is more of a workflow oracle than a formatting oracle. It should prove session-overlay discipline and async boundary handling.

## Fixture 9: Similar SQL Do Not Widen

Purpose: ensure the edit stays on one target method when nearby methods look tempting.

Starting shape:

```csharp
public void SaveAuditEvent(AuditEvent model)
{
    const string sql = "INSERT INTO AuditEvents ...";
    Execute(sql, model);
}

public void SaveAuditBatch(IEnumerable<AuditEvent> models)
{
    const string sql = "INSERT INTO AuditEvents ...";
    ExecuteMany(sql, models);
}

public void DeleteAuditEvent(int auditEventId)
{
    const string sql = "DELETE FROM AuditEvents WHERE AuditEventId = @auditEventId";
    Execute(sql, new { auditEventId });
}
```

Task:

```text
Only replace inline SQL in SaveAuditEvent with SqlQueries["SaveAuditEvent"]. Do not change batch or delete methods.
```

Correct selector:

```text
SaveAuditEvent(AuditEvent)
```

Expected ordering:

```text
SaveAuditEvent changed
SaveAuditBatch unchanged
DeleteAuditEvent unchanged
```

Oracle rules:

- no opportunistic refactor of similar methods
- no `submit_file` unless explicitly justified
- final source text for non-target methods remains unchanged

Finding to verify: known-answer tests should assert non-target member body hashes are unchanged.

## Fixture 10: Existing Region Preservation

Purpose: add a method into an existing region without introducing or moving regions.

Starting shape:

```csharp
#region Public Methods

public void Start()
{
}

public void Stop()
{
}

#endregion

#region Private Methods

private void ResetState()
{
}

#endregion
```

Task:

```text
Add public void Restart() after Stop().
```

Correct anchor:

```text
afterSymbol: Stop
```

Expected ordering:

```text
Start
Stop
Restart
#endregion Public Methods
Private Methods region unchanged
```

Oracle rules:

- preserve existing regions
- do not add new regions
- insert inside the correct region boundary

Finding to verify: trivia around region directives may require special handling beyond member leading trivia.

## Fixture 11: New Template Class With Regions

Purpose: generate a brand-new class file with front-loaded member order and standard regions.

Suggested generator parameters:

```text
namespace
visibility
className
isPartial
```

Starting shape:

```text
file does not exist
```

Task:

```text
Create CustomerImportService.cs with constructor-injected repository/logger dependencies, two public methods, one private helper, and one nested options type.
```

Expected file shape:

```csharp
namespace SchemaStudio.Services
{
    internal sealed class CustomerImportService
    {
        #region Fields

        private readonly ICustomerRepository _repository;
        private readonly ILogger _logger;

        #endregion

        #region Constructors

        public CustomerImportService(ICustomerRepository repository, ILogger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        #endregion

        #region Attributes

        #endregion

        #region Public Methods

        public void ImportCustomers(IEnumerable<CustomerRecord> customers)
        {
        }

        public bool CanImport(CustomerRecord customer)
        {
            return customer != null;
        }

        #endregion

        #region Private Methods

        private static ImportOptions CreateDefaultOptions()
        {
            return new ImportOptions();
        }

        #endregion

        #region Converters

        private static ImportOptions ToOptions(CustomerImportMode mode)
        {
            return new ImportOptions();
        }

        #endregion

        #region Nested Types

        private enum CustomerImportMode
        {
            Standard,
            Preview
        }

        private sealed class ImportOptions
        {
        }

        #endregion
    }
}
```

Oracle rules:

- regions are allowed because this is a brand-new generated file
- region order is stable
- fields before constructors, constructors before public methods, private helpers/converters before nested types
- enums live under nested types unless the project has a stronger enum placement convention
- attributes and converters have explicit regions when the generated class owns those helper concepts
- generator inputs should include namespace, visibility, className, and isPartial
- no regions should be inferred as a requirement for existing-file edits

Finding to verify: new-file generation can front-load formatting and layout correctness because there is no existing diff stability to preserve.
