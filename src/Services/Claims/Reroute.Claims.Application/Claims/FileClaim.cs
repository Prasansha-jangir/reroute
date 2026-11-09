using FluentValidation;
using Reroute.BuildingBlocks.Cqrs;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;

namespace Reroute.Claims.Application.Claims;

public sealed record FileClaimCommand(
    string FlightNumber,
    DateOnly FlightDate,
    string Origin,
    string Destination,
    DisruptionType DisruptionType,
    int ArrivalDelayMinutes,
    int? CancellationNoticeDays = null,
    bool ExtraordinaryCircumstances = false,
    int? ReroutedArrivalDelayMinutes = null,
    bool OperatedByEuCarrier = true) : ICommand<Result<FileClaimResponse>>;

public sealed record FileClaimResponse(Guid ClaimId, bool IsEligible, decimal CompensationAmount, string Currency, string Reason);

public sealed class FileClaimValidator : AbstractValidator<FileClaimCommand>
{
    public FileClaimValidator(TimeProvider timeProvider)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(c => c.FlightNumber).NotEmpty().MaximumLength(8);
        RuleFor(c => c.Origin).NotEmpty().Length(3);
        RuleFor(c => c.Destination).NotEmpty().Length(3)
            .NotEqual(c => c.Origin).WithMessage("Destination must differ from origin.");
        RuleFor(c => c.FlightDate)
            .LessThanOrEqualTo(today).WithMessage("You can only claim for flights that already happened.")
            .GreaterThan(today.AddYears(-6)).WithMessage("Claims older than 6 years are time-barred in every EU member state.");
        RuleFor(c => c.ArrivalDelayMinutes).InclusiveBetween(0, 7 * 24 * 60);
        RuleFor(c => c.CancellationNoticeDays).GreaterThanOrEqualTo(0).When(c => c.CancellationNoticeDays.HasValue);
        RuleFor(c => c.CancellationNoticeDays).NotNull()
            .When(c => c.DisruptionType == DisruptionType.Cancellation)
            .WithMessage("Tell us how many days before departure the cancellation was announced.");
    }
}

internal sealed class FileClaimHandler(
    IClaimRepository repository,
    IAirportDirectory airports,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<FileClaimCommand, Result<FileClaimResponse>>
{
    public async Task<Result<FileClaimResponse>> Handle(FileClaimCommand command, CancellationToken cancellationToken)
    {
        var flight = FlightNumber.Create(command.FlightNumber);
        if (flight.IsFailure)
        {
            return flight.Error;
        }

        var origin = airports.Find(command.Origin);
        var destination = airports.Find(command.Destination);
        if (origin is null || destination is null)
        {
            return Error.Validation(new Dictionary<string, string[]>
            {
                [origin is null ? "origin" : "destination"] = ["Unknown airport code."],
            });
        }

        var disruption = new FlightDisruption(
            flight.Value,
            command.FlightDate,
            origin,
            destination,
            command.DisruptionType,
            command.ArrivalDelayMinutes,
            command.CancellationNoticeDays,
            command.ExtraordinaryCircumstances,
            command.ReroutedArrivalDelayMinutes,
            command.OperatedByEuCarrier);

        var claim = Claim.File(ClaimId.New(), currentUser.Id, disruption, timeProvider.GetUtcNow());
        await repository.SaveAsync(claim, cancellationToken);

        return new FileClaimResponse(
            claim.Id.Value,
            claim.Status == ClaimStatus.Eligible,
            claim.Compensation.Amount,
            claim.Compensation.Currency,
            claim.EligibilityReason);
    }
}
