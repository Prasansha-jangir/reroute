using NSubstitute;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;
using Shouldly;
using Xunit;

namespace Reroute.Claims.UnitTests;

public sealed class FileClaimHandlerTests
{
    private readonly InMemoryClaimRepository _repository = new();
    private readonly IAirportDirectory _airports = Substitute.For<IAirportDirectory>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    public FileClaimHandlerTests()
    {
        _airports.Find(Arg.Any<string>()).Returns(ci => TestData.Airports.GetValueOrDefault((string)ci[0]));
        _user.Id.Returns("traveller-42");
    }

    [Fact]
    public async Task Files_and_persists_an_eligible_claim()
    {
        var handler = new FileClaimHandler(_repository, _airports, _user, TimeProvider.System);

        var result = await handler.Handle(
            new FileClaimCommand("KL 1234", new DateOnly(2026, 10, 1), "AMS", "ATH", DisruptionType.Delay, 200),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CompensationAmount.ShouldBe(400);
        var saved = _repository.Saved.ShouldHaveSingleItem();
        saved.TravellerId.ShouldBe("traveller-42");
        saved.FlightNumber.ShouldBe("KL1234");
    }

    [Fact]
    public async Task Unknown_airport_is_a_validation_error()
    {
        var handler = new FileClaimHandler(_repository, _airports, _user, TimeProvider.System);

        var result = await handler.Handle(
            new FileClaimCommand("KL1234", new DateOnly(2026, 10, 1), "AMS", "XXX", DisruptionType.Delay, 200),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(Reroute.SharedKernel.ErrorType.Validation);
        _repository.Saved.ShouldBeEmpty();
    }

    /// <summary>A fake beats a mock here: simpler, and it exercises MarkCommitted like the real repository.</summary>
    private sealed class InMemoryClaimRepository : IClaimRepository
    {
        public List<Claim> Saved { get; } = [];

        public Task<Claim?> GetAsync(ClaimId id, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.FirstOrDefault(c => c.Id == id));

        public Task SaveAsync(Claim claim, CancellationToken cancellationToken)
        {
            claim.MarkCommitted();
            if (!Saved.Contains(claim))
            {
                Saved.Add(claim);
            }

            return Task.CompletedTask;
        }
    }
}
