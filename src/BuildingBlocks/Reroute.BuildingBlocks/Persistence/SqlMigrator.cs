using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Reroute.BuildingBlocks.Persistence;

/// <summary>
/// Tiny forward-only migrator: applies embedded *.sql scripts in name order, once each, tracked in dbo.SchemaVersions.
/// In Kubernetes it runs as a pre-deploy Job (not on app start) so replicas never race each other.
/// </summary>
public sealed partial class SqlMigrator(string connectionString, Assembly scriptsAssembly, ILogger logger)
{
    public async Task MigrateAsync(bool createDatabaseIfMissing, CancellationToken cancellationToken = default)
    {
        if (createDatabaseIfMissing)
        {
            await EnsureDatabaseAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync("""
            IF OBJECT_ID('dbo.SchemaVersions') IS NULL
                CREATE TABLE dbo.SchemaVersions (
                    ScriptName NVARCHAR(255) NOT NULL PRIMARY KEY,
                    AppliedAt  DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME());
            """);

        var applied = (await connection.QueryAsync<string>("SELECT ScriptName FROM dbo.SchemaVersions"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scripts = scriptsAssembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var script in scripts.Where(s => !applied.Contains(s)))
        {
            await using var stream = scriptsAssembly.GetManifestResourceStream(script)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            foreach (var batch in GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await connection.ExecuteAsync(batch, transaction: tx);
            }

            await connection.ExecuteAsync(
                "INSERT INTO dbo.SchemaVersions (ScriptName) VALUES (@script)", new { script }, tx);
            await tx.CommitAsync(cancellationToken);
            logger.LogInformation("Applied migration {Script}", script);
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<int?>("SELECT 1 FROM sys.databases WHERE name = @database", new { database });
        if (exists is null)
        {
            var quoted = "[" + database.Replace("]", "]]", StringComparison.Ordinal) + "]";
            await connection.ExecuteAsync($"CREATE DATABASE {quoted}");
            // Readers never block writers (row versioning).
            await connection.ExecuteAsync($"ALTER DATABASE {quoted} SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
            logger.LogInformation("Created database {Database}", database);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
