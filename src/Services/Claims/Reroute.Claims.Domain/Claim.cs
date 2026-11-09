using Reroute.SharedKernel;

namespace Reroute.Claims.Domain;

/// <summary>
/// Event-sourced aggregate for an EU261 compensation claim.
/// Command methods guard invariants (valid state transitions) and raise events; <see cref="When"/> applies them.
/// </summary>
public sealed class Claim : EventSourcedAggregate<ClaimId>
{
    private Claim()
    {
    }

    public string TravellerId { get; private set; } = string.Empty;

    public string FlightNumber { get; private set; } = string.Empty;

    public DateOnly FlightDate { get; private set; }

    public ClaimStatus Status { get; private set; }

    public Money Compensation { get; private set; } = Money.Zero();

    public string EligibilityReason { get; private set; } = string.Empty;

    public string? AirlineReference { get; private set; }

    /// <summary>Files a claim and immediately assesses eligibility (two events, one transaction).</summary>
    public static Claim File(ClaimId id, string travellerId, FlightDisruption disruption, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(travellerId);
        ArgumentNullException.ThrowIfNull(disruption);

        var claim = new Claim();
        claim.Raise(new ClaimFiled(
            id.Value,
            travellerId,
            disruption.Flight.ToString(),
            disruption.FlightDate,
            disruption.Origin.IataCode,
            disruption.Destination.IataCode,
            disruption.Type,
            disruption.ArrivalDelayMinutes,
            now));

        var decision = Eu261CompensationPolicy.Assess(disruption);
        claim.Raise(new EligibilityAssessed(
            id.Value,
            decision.IsEligible,
            decision.Compensation.Amount,
            decision.Compensation.Currency,
            decision.Reason,
            decision.DistanceKm,
            decision.ReducedForRerouting,
            now));

        return claim;
    }

    public static Claim FromHistory(IEnumerable<IDomainEvent> history)
    {
        var claim = new Claim();
        claim.LoadFromHistory(history);
        return claim;
    }

    public Result SubmitToAirline(DateTimeOffset now)
    {
        if (Status != ClaimStatus.Eligible)
        {
            return InvalidTransition("submit to airline");
        }

        Raise(new ClaimSubmittedToAirline(Id.Value, now));
        return Result.Success();
    }

    public Result RecordAirlineDecision(bool accepted, string? airlineReference, string? rejectionReason, DateTimeOffset now)
    {
        if (Status != ClaimStatus.SubmittedToAirline)
        {
            return InvalidTransition("record airline decision");
        }

        if (accepted)
        {
            if (string.IsNullOrWhiteSpace(airlineReference))
            {
                return Error.Conflict("claim.airline_reference_required", "An accepted claim needs the airline's reference.");
            }

            Raise(new AirlineAcceptedClaim(Id.Value, airlineReference, now));
        }
        else
        {
            Raise(new AirlineRejectedClaim(Id.Value, rejectionReason ?? "No reason given", now));
        }

        return Result.Success();
    }

    public Result Escalate(string enforcementBody, DateTimeOffset now)
    {
        if (Status != ClaimStatus.RejectedByAirline)
        {
            return InvalidTransition("escalate");
        }

        Raise(new ClaimEscalated(Id.Value, enforcementBody, now));
        return Result.Success();
    }

    public Result MarkPaid(string paymentReference, DateTimeOffset now)
    {
        if (Status != ClaimStatus.AcceptedByAirline)
        {
            return InvalidTransition("mark as paid");
        }

        Raise(new CompensationPaid(Id.Value, Compensation.Amount, Compensation.Currency, paymentReference, now));
        return Result.Success();
    }

    protected override void When(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case ClaimFiled e:
                Id = new ClaimId(e.ClaimId);
                TravellerId = e.TravellerId;
                FlightNumber = e.FlightNumber;
                FlightDate = e.FlightDate;
                Status = ClaimStatus.Filed;
                break;
            case EligibilityAssessed e:
                Status = e.IsEligible ? ClaimStatus.Eligible : ClaimStatus.Ineligible;
                Compensation = new Money(e.CompensationAmount, e.Currency);
                EligibilityReason = e.Reason;
                break;
            case ClaimSubmittedToAirline:
                Status = ClaimStatus.SubmittedToAirline;
                break;
            case AirlineAcceptedClaim e:
                Status = ClaimStatus.AcceptedByAirline;
                AirlineReference = e.AirlineReference;
                break;
            case AirlineRejectedClaim:
                Status = ClaimStatus.RejectedByAirline;
                break;
            case ClaimEscalated:
                Status = ClaimStatus.Escalated;
                break;
            case CompensationPaid:
                Status = ClaimStatus.Paid;
                break;
            default:
                throw new InvalidOperationException($"Unknown event {domainEvent.GetType().Name} for Claim.");
        }
    }

    private Error InvalidTransition(string action) =>
        Error.Conflict("claim.invalid_transition", $"Cannot {action} a claim in status {Status}.");
}
