using Reroute.SharedKernel;

namespace Reroute.Claims.Domain;

// Domain events are persisted forever in the event store: they are contracts.
// Rules: primitive/serialisable fields only, never rename, never remove fields — add new versions + upcasters instead.

public sealed record ClaimFiled(
    Guid ClaimId,
    string TravellerId,
    string FlightNumber,
    DateOnly FlightDate,
    string Origin,
    string Destination,
    DisruptionType DisruptionType,
    int ArrivalDelayMinutes,
    DateTimeOffset FiledAt) : IDomainEvent;

public sealed record EligibilityAssessed(
    Guid ClaimId,
    bool IsEligible,
    decimal CompensationAmount,
    string Currency,
    string Reason,
    double DistanceKm,
    bool ReducedForRerouting,
    DateTimeOffset AssessedAt) : IDomainEvent;

public sealed record ClaimSubmittedToAirline(Guid ClaimId, DateTimeOffset SubmittedAt) : IDomainEvent;

public sealed record AirlineAcceptedClaim(Guid ClaimId, string AirlineReference, DateTimeOffset DecidedAt) : IDomainEvent;

public sealed record AirlineRejectedClaim(Guid ClaimId, string Reason, DateTimeOffset DecidedAt) : IDomainEvent;

public sealed record ClaimEscalated(Guid ClaimId, string EnforcementBody, DateTimeOffset EscalatedAt) : IDomainEvent;

public sealed record CompensationPaid(Guid ClaimId, decimal Amount, string Currency, string PaymentReference, DateTimeOffset PaidAt) : IDomainEvent;
