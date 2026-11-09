using Dapper;
using Microsoft.Data.SqlClient;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;

namespace Reroute.Claims.Infrastructure.Projections;

/// <summary>
/// Inline projection: updates the ClaimSummaries read model in the SAME transaction as the event append.
/// Trade-off (ADR-003): read-your-writes consistency for the UI at the cost of slightly slower writes.
/// Heavier/secondary read models are built asynchronously from claims.events.v1 instead.
/// </summary>
public static class ClaimSummaryProjection
{
    public static Task ApplyAsync(
        SqlConnection connection, SqlTransaction transaction, IDomainEvent domainEvent, int version, DateTimeOffset at)
    {
        Task SetStatus(Guid claimId, ClaimStatus status) => connection.ExecuteAsync(
            "UPDATE claims.ClaimSummaries SET Status = @Status, Version = @Version, UpdatedAt = @At WHERE ClaimId = @ClaimId",
            new { ClaimId = claimId, Status = status.ToString(), Version = version, At = at },
            transaction);

        return domainEvent switch
        {
            ClaimFiled e => connection.ExecuteAsync(
                """
                INSERT INTO claims.ClaimSummaries
                    (ClaimId, TravellerId, FlightNumber, FlightDate, Origin, Destination, DisruptionType, Status, Version, FiledAt, UpdatedAt)
                VALUES
                    (@ClaimId, @TravellerId, @FlightNumber, @FlightDate, @Origin, @Destination, @DisruptionType, 'Filed', @Version, @FiledAt, @FiledAt)
                """,
                new
                {
                    e.ClaimId,
                    e.TravellerId,
                    e.FlightNumber,
                    FlightDate = e.FlightDate.ToDateTime(TimeOnly.MinValue),
                    e.Origin,
                    e.Destination,
                    DisruptionType = e.DisruptionType.ToString(),
                    Version = version,
                    e.FiledAt,
                },
                transaction),

            EligibilityAssessed e => connection.ExecuteAsync(
                """
                UPDATE claims.ClaimSummaries
                SET Status = @Status, CompensationAmount = @CompensationAmount, Currency = @Currency,
                    EligibilityReason = @Reason, DistanceKm = @DistanceKm, Version = @Version, UpdatedAt = @At
                WHERE ClaimId = @ClaimId
                """,
                new
                {
                    e.ClaimId,
                    Status = (e.IsEligible ? ClaimStatus.Eligible : ClaimStatus.Ineligible).ToString(),
                    e.CompensationAmount,
                    e.Currency,
                    e.Reason,
                    e.DistanceKm,
                    Version = version,
                    At = at,
                },
                transaction),

            ClaimSubmittedToAirline e => SetStatus(e.ClaimId, ClaimStatus.SubmittedToAirline),
            AirlineAcceptedClaim e => SetStatus(e.ClaimId, ClaimStatus.AcceptedByAirline),
            AirlineRejectedClaim e => SetStatus(e.ClaimId, ClaimStatus.RejectedByAirline),
            ClaimEscalated e => SetStatus(e.ClaimId, ClaimStatus.Escalated),
            CompensationPaid e => SetStatus(e.ClaimId, ClaimStatus.Paid),
            _ => throw new InvalidOperationException($"No projection for {domainEvent.GetType().Name}."),
        };
    }
}
