namespace SchemaStudio.Data;

public sealed partial class DatabaseDomainRepository
{
    #region SQL Statements

    private static readonly IReadOnlyDictionary<string, string> Sql = new Dictionary<string, string>
    {
        ["GetByDatabase"] = @"
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain
FROM DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain",

        ["Insert"] = @"
INSERT INTO DatabaseDomain
(
    DatabaseId,
    Domain
)
VALUES
(
    @DatabaseId,
    @Domain
);

SELECT CAST(SCOPE_IDENTITY() as int);",

        ["Update"] = @"
UPDATE DatabaseDomain
SET
    Domain = @Domain
WHERE DatabaseDomainId = @DatabaseDomainId",
    };

    #endregion
}
