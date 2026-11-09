using Reroute.Claims.Domain;

namespace Reroute.Claims.Application.Claims;

public sealed record ClaimSummaryDto(
    Guid ClaimId,
    string FlightNumber,
    DateOnly FlightDate,
    ClaimStatus Status,
    decimal CompensationAmount,
    string Currency,
    DateTimeOffset FiledAt);

public sealed record ClaimDto(
    Guid ClaimId,
    string TravellerId,
    string FlightNumber,
    DateOnly FlightDate,
    string Origin,
    string Destination,
    DisruptionType DisruptionType,
    ClaimStatus Status,
    decimal CompensationAmount,
    string Currency,
    string EligibilityReason,
    double DistanceKm,
    int Version,
    DateTimeOffset FiledAt,
    DateTimeOffset UpdatedAt);

/// <summary>One entry of the audit timeline, read straight from the event store.</summary>
public sealed record ClaimTimelineEntryDto(int Version, string EventType, DateTimeOffset OccurredAt, string Data);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
