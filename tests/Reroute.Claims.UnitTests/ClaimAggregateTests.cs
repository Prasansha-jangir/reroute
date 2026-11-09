using Reroute.Claims.Domain;
using Reroute.SharedKernel;
using Shouldly;
using Xunit;
using static Reroute.Claims.UnitTests.TestData;

namespace Reroute.Claims.UnitTests;

/// <summary>Given (past events) → When (command) → Then (new events or error). The event-sourcing testing style.</summary>
public sealed class ClaimAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.CreateVersion7();

    private static Claim Given(params IDomainEvent[] history) => Claim.FromHistory(history);

    private static ClaimFiled Filed() => new(Id, "traveller-1", "KL1234", new DateOnly(2026, 10, 1), "AMS", "CDG", DisruptionType.Delay, 240, Now);

    private static EligibilityAssessed Assessed(bool eligible = true) => new(Id, eligible, eligible ? 250 : 0, "EUR", "reason", 398, false, Now);

    [Fact]
    public void Filing_raises_filed_and_assessed_events()
    {
        var claim = Claim.File(new ClaimId(Id), "traveller-1", Disruption(), Now);

        claim.UncommittedEvents.Select(e => e.GetType()).ShouldBe([typeof(ClaimFiled), typeof(EligibilityAssessed)]);
        claim.Status.ShouldBe(ClaimStatus.Eligible);
        claim.Compensation.ShouldBe(Money.Eur(250));
    }

    [Fact]
    public void Rehydration_restores_state_and_version()
    {
        var claim = Given(Filed(), Assessed(), new ClaimSubmittedToAirline(Id, Now));

        claim.Status.ShouldBe(ClaimStatus.SubmittedToAirline);
        claim.Version.ShouldBe(3);
        claim.UncommittedEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Eligible_claim_can_be_submitted()
    {
        var claim = Given(Filed(), Assessed());

        claim.SubmitToAirline(Now).IsSuccess.ShouldBeTrue();
        claim.UncommittedEvents.Single().ShouldBeOfType<ClaimSubmittedToAirline>();
    }

    [Fact]
    public void Ineligible_claim_cannot_be_submitted()
    {
        var claim = Given(Filed(), Assessed(eligible: false));

        var result = claim.SubmitToAirline(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("claim.invalid_transition");
        claim.UncommittedEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Accepting_requires_an_airline_reference()
    {
        var claim = Given(Filed(), Assessed(), new ClaimSubmittedToAirline(Id, Now));

        claim.RecordAirlineDecision(accepted: true, airlineReference: null, rejectionReason: null, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Full_happy_path_ends_in_paid()
    {
        var claim = Given(Filed(), Assessed(), new ClaimSubmittedToAirline(Id, Now), new AirlineAcceptedClaim(Id, "KL-REF-1", Now));

        claim.MarkPaid("PAY-1", Now).IsSuccess.ShouldBeTrue();

        claim.Status.ShouldBe(ClaimStatus.Paid);
        claim.UncommittedEvents.Single().ShouldBe(new CompensationPaid(Id, 250, "EUR", "PAY-1", Now));
    }

    [Fact]
    public void Rejected_claim_can_be_escalated()
    {
        var claim = Given(Filed(), Assessed(), new ClaimSubmittedToAirline(Id, Now), new AirlineRejectedClaim(Id, "weather", Now));

        claim.Escalate("ILT (NL enforcement body)", Now).IsSuccess.ShouldBeTrue();
        claim.Status.ShouldBe(ClaimStatus.Escalated);
    }
}
