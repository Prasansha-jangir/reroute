using Reroute.Claims.Domain;
using Shouldly;
using Xunit;
using static Reroute.Claims.UnitTests.TestData;

namespace Reroute.Claims.UnitTests;

/// <summary>Table-driven tests: every rule and every boundary of the compensation policy.</summary>
public sealed class Eu261CompensationPolicyTests
{
    [Theory]
    [InlineData("AMS", "CDG", 250)]   // ~400 km           → ≤ 1500 km band
    [InlineData("AMS", "ATH", 400)]   // ~2160 km intra-EU → €400
    [InlineData("AMS", "TLV", 400)]   // ~3300 km          → 1500–3500 band
    [InlineData("AMS", "JFK", 600)]   // ~5850 km          → > 3500 km
    [InlineData("CDG", "RUN", 400)]   // ~9350 km but intra-EU (outermost region) → capped at €400
    public void Distance_band_determines_amount(string from, string to, decimal expected)
    {
        var decision = Eu261CompensationPolicy.Assess(Disruption(Airports[from], Airports[to], arrivalDelay: 200));

        decision.IsEligible.ShouldBeTrue();
        decision.Compensation.ShouldBe(Money.Eur(expected));
    }

    [Theory]
    [InlineData(179, false)]
    [InlineData(180, true)]
    [InlineData(600, true)]
    public void Delay_must_be_at_least_three_hours_at_arrival(int delayMinutes, bool eligible) =>
        Eu261CompensationPolicy.Assess(Disruption(arrivalDelay: delayMinutes)).IsEligible.ShouldBe(eligible);

    [Fact]
    public void Extraordinary_circumstances_exempt_the_airline() =>
        Eu261CompensationPolicy.Assess(Disruption(extraordinary: true)).IsEligible.ShouldBeFalse();

    [Theory]
    [InlineData(13, true)]
    [InlineData(14, false)]
    [InlineData(30, false)]
    public void Cancellation_requires_less_than_fourteen_days_notice(int noticeDays, bool eligible) =>
        Eu261CompensationPolicy.Assess(Disruption(type: DisruptionType.Cancellation, arrivalDelay: 0, noticeDays: noticeDays))
            .IsEligible.ShouldBe(eligible);

    [Fact]
    public void Acceptable_reroute_removes_compensation() =>
        Eu261CompensationPolicy.Assess(Disruption(type: DisruptionType.Cancellation, noticeDays: 2, reroutedDelay: 90))
            .IsEligible.ShouldBeFalse();

    [Fact]
    public void Reroute_within_band_threshold_halves_compensation()
    {
        // AMS→JFK (> 3500 km): threshold 4 h. Re-routed 3.5 h late with 2 days' notice → 50 % of €600.
        var decision = Eu261CompensationPolicy.Assess(
            Disruption(Ams, Jfk, DisruptionType.Cancellation, noticeDays: 2, reroutedDelay: 210));

        decision.IsEligible.ShouldBeTrue();
        decision.ReducedForRerouting.ShouldBeTrue();
        decision.Compensation.ShouldBe(Money.Eur(300));
    }

    [Theory]
    [InlineData("JFK", "AMS", true, true)]    // inbound to EU on EU carrier → in scope
    [InlineData("JFK", "AMS", false, false)]  // inbound to EU on non-EU carrier → out of scope
    [InlineData("LHR", "JFK", true, false)]   // neither end in EU → out of scope
    [InlineData("AMS", "JFK", false, true)]   // departs EU → always in scope
    public void Scope_follows_article_3(string from, string to, bool euCarrier, bool eligible) =>
        Eu261CompensationPolicy.Assess(Disruption(Airports[from], Airports[to], euCarrier: euCarrier))
            .IsEligible.ShouldBe(eligible);

    [Fact]
    public void Denied_boarding_is_compensated_regardless_of_delay() =>
        Eu261CompensationPolicy.Assess(Disruption(type: DisruptionType.DeniedBoarding, arrivalDelay: 0))
            .IsEligible.ShouldBeTrue();

    [Fact]
    public void Great_circle_distance_is_accurate() =>
        GreatCircle.DistanceKm(Ams, Jfk).ShouldBe(5850, tolerance: 25);
}
