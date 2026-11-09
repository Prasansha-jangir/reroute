using Dapper;
using Microsoft.Extensions.Caching.Hybrid;
using Reroute.BuildingBlocks.Persistence;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;
using Reroute.Claims.Infrastructure.Persistence;

namespace Reroute.Claims.Infrastructure.ReadModel;

/// <summary>Query side: flat SELECTs against the denormalised read model. No joins, covering indexes.</summary>
public sealed class SqlClaimReadStore(ISqlConnectionFactory connectionFactory) : IClaimReadStore
{
    public async Task<ClaimDto?> GetAsync(Guid claimId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ClaimRow>(new CommandDefinition(
            """
            SELECT ClaimId, TravellerId, FlightNumber, FlightDate, Origin, Destination, DisruptionType, Status,
                   CompensationAmount, Currency, EligibilityReason, DistanceKm, Version, FiledAt, UpdatedAt
            FROM claims.ClaimSummaries WHERE ClaimId = @claimId
            """,
            new { claimId },
            cancellationToken: cancellationToken));
        return row?.ToDto();
    }

    public async Task<PagedResult<ClaimSummaryDto>> ListByTravellerAsync(
        string travellerId, ClaimStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT ClaimId, FlightNumber, FlightDate, Status, CompensationAmount, Currency, FiledAt
            FROM claims.ClaimSummaries
            WHERE TravellerId = @travellerId AND (@status IS NULL OR Status = @status)
            ORDER BY FiledAt DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) FROM claims.ClaimSummaries
            WHERE TravellerId = @travellerId AND (@status IS NULL OR Status = @status);
            """,
            new { travellerId, status = status?.ToString(), offset = (page - 1) * pageSize, pageSize },
            cancellationToken: cancellationToken));

        var items = (await grid.ReadAsync<SummaryRow>()).Select(r => r.ToDto()).ToList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<ClaimSummaryDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<ClaimTimelineEntryDto>> GetTimelineAsync(Guid claimId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClaimTimelineEntryDto>(new CommandDefinition(
            """
            SELECT StreamVersion AS Version, EventType, OccurredAt, Data
            FROM claims.Events WHERE StreamId = @claimId ORDER BY StreamVersion
            """,
            new { claimId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    // Row types keep SQL types (DateTime, string) separate from the API's DTO types (DateOnly, enums).
    private sealed record ClaimRow(
        Guid ClaimId, string TravellerId, string FlightNumber, DateTime FlightDate, string Origin, string Destination,
        string DisruptionType, string Status, decimal CompensationAmount, string Currency, string EligibilityReason,
        double DistanceKm, int Version, DateTimeOffset FiledAt, DateTimeOffset UpdatedAt)
    {
        public ClaimDto ToDto() => new(
            ClaimId, TravellerId, FlightNumber, DateOnly.FromDateTime(FlightDate), Origin.Trim(), Destination.Trim(),
            Enum.Parse<DisruptionType>(DisruptionType), Enum.Parse<ClaimStatus>(Status), CompensationAmount, Currency.Trim(),
            EligibilityReason, DistanceKm, Version, FiledAt, UpdatedAt);
    }

    private sealed record SummaryRow(
        Guid ClaimId, string FlightNumber, DateTime FlightDate, string Status, decimal CompensationAmount, string Currency, DateTimeOffset FiledAt)
    {
        public ClaimSummaryDto ToDto() => new(
            ClaimId, FlightNumber, DateOnly.FromDateTime(FlightDate), Enum.Parse<ClaimStatus>(Status), CompensationAmount, Currency.Trim(), FiledAt);
    }
}

/// <summary>
/// Decorator adding two-level caching (L1 in-process + L2 Redis) via HybridCache.
/// Cache-aside with stampede protection; invalidated by tag from the repository after each commit.
/// </summary>
public sealed class CachedClaimReadStore(SqlClaimReadStore inner, HybridCache cache) : IClaimReadStore
{
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromSeconds(30), // bounds staleness on other replicas
    };

    public Task<ClaimDto?> GetAsync(Guid claimId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"reroute:claims:claim:{claimId}:v1",
            (inner, claimId),
            static (state, ct) => new ValueTask<ClaimDto?>(state.inner.GetAsync(state.claimId, ct)),
            Options,
            [CacheTags.Claim(claimId)],
            cancellationToken).AsTask();

    public Task<PagedResult<ClaimSummaryDto>> ListByTravellerAsync(
        string travellerId, ClaimStatus? status, int page, int pageSize, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"reroute:claims:list:{travellerId}:{status}:{page}:{pageSize}:v1",
            (inner, travellerId, status, page, pageSize),
            static (s, ct) => new ValueTask<PagedResult<ClaimSummaryDto>>(s.inner.ListByTravellerAsync(s.travellerId, s.status, s.page, s.pageSize, ct)),
            Options,
            [CacheTags.Traveller(travellerId)],
            cancellationToken).AsTask();

    public Task<IReadOnlyList<ClaimTimelineEntryDto>> GetTimelineAsync(Guid claimId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"reroute:claims:timeline:{claimId}:v1",
            (inner, claimId),
            static (state, ct) => new ValueTask<IReadOnlyList<ClaimTimelineEntryDto>>(state.inner.GetTimelineAsync(state.claimId, ct)),
            Options,
            [CacheTags.Claim(claimId)],
            cancellationToken).AsTask();
}
