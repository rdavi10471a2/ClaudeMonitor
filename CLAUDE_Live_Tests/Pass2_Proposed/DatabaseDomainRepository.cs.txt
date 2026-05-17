using Dapper;
using Microsoft.Data.SqlClient;

namespace SchemaStudio.Data;

public sealed partial class DatabaseDomainRepository
{
    #region Fields

    private readonly string _connectionString;

    #endregion

    #region Constructors

    public DatabaseDomainRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    #endregion

    #region Public Methods

    public async Task<List<DatabaseDomainDefinition>> GetByDatabaseAsync(int databaseId, CancellationToken cancellationToken = default)
    {
        using var conn = new SqlConnection(_connectionString);

        var rows = await conn.QueryAsync<DatabaseDomainDefinition>(
            new CommandDefinition(Sql["GetByDatabase"], new { databaseId }, cancellationToken: cancellationToken));

        return rows
            .Select(item =>
            {
                item.ClearDirty();
                return item;
            })
            .ToList();
    }

    public async Task<int> InsertAsync(DatabaseDomainDefinition model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var conn = new SqlConnection(_connectionString);

        var id = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(Sql["Insert"], model, cancellationToken: cancellationToken));

        model.DatabaseDomainId = id;
        model.ClearDirty();
        return id;
    }

    public async Task UpdateAsync(DatabaseDomainDefinition model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var conn = new SqlConnection(_connectionString);

        await conn.ExecuteAsync(
            new CommandDefinition(Sql["Update"], model, cancellationToken: cancellationToken));

        model.ClearDirty();
    }

    public async Task SaveAllAsync(IEnumerable<DatabaseDomainDefinition> models, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(models);

        foreach (var model in models.Where(item => item.IsDirty || item.DatabaseDomainId == 0))
        {
            if (model.DatabaseDomainId == 0)
            {
                await InsertAsync(model, cancellationToken);
            }
            else
            {
                await UpdateAsync(model, cancellationToken);
            }
        }
    }

    #endregion
}
