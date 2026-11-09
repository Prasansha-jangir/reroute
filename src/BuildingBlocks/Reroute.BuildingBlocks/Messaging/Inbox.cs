using Dapper;
using Microsoft.Data.SqlClient;
using Reroute.BuildingBlocks.Persistence;

namespace Reroute.BuildingBlocks.Messaging;

/// <summary>Idempotent-consumer store: remembers which message IDs a consumer group has already processed.</summary>
public interface IInbox
{
    Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken);
}

public sealed class SqlInbox(ISqlConnectionFactory connectionFactory, string schema) : IInbox
{
    public async Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int?>(
            $"SELECT 1 FROM [{schema}].InboxMessages WHERE MessageId = @messageId AND Consumer = @consumer",
            new { messageId, consumer }) is not null;
    }

    public async Task MarkProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(
                $"INSERT INTO [{schema}].InboxMessages (MessageId, Consumer, ProcessedAt) VALUES (@messageId, @consumer, SYSUTCDATETIME())",
                new { messageId, consumer });
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            // Already recorded by a concurrent delivery — that's fine.
        }
    }
}
