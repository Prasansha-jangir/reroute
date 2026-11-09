using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;

namespace Reroute.Claims.Application.Abstractions;

// Ports (hexagonal architecture): the application defines what it needs; Infrastructure provides adapters.

/// <summary>One repository per aggregate — not a generic IRepository&lt;T&gt;.</summary>
public interface IClaimRepository
{
    Task<Claim?> GetAsync(ClaimId id, CancellationToken cancellationToken);

    /// <summary>Appends uncommitted events with optimistic concurrency. Throws <see cref="ConcurrencyException"/> on conflict.</summary>
    Task SaveAsync(Claim claim, CancellationToken cancellationToken);
}

/// <summary>Read side (CQRS): denormalised, query-optimised, cached.</summary>
public interface IClaimReadStore
{
    Task<ClaimDto?> GetAsync(Guid claimId, CancellationToken cancellationToken);

    Task<PagedResult<ClaimSummaryDto>> ListByTravellerAsync(
        string travellerId, ClaimStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimTimelineEntryDto>> GetTimelineAsync(Guid claimId, CancellationToken cancellationToken);
}

public interface IAirportDirectory
{
    Airport? Find(string iataCode);
}

public sealed class ConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
