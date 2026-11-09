using Microsoft.Data.SqlClient;

namespace Reroute.BuildingBlocks.Persistence;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens pooled SQL connections. In Azure the connection string uses
/// "Authentication=Active Directory Default" so Workload Identity supplies the token — no passwords.
/// </summary>
public sealed class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
