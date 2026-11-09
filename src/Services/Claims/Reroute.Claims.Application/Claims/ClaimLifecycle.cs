using FluentValidation;
using Reroute.BuildingBlocks.Cqrs;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;

namespace Reroute.Claims.Application.Claims;

public sealed record SubmitClaimToAirlineCommand(Guid ClaimId, int? ExpectedVersion) : ICommand<Result>;

public sealed record RecordAirlineDecisionCommand(Guid ClaimId, bool Accepted, string? AirlineReference, string? Reason) : ICommand<Result>;

public sealed class RecordAirlineDecisionValidator : AbstractValidator<RecordAirlineDecisionCommand>
{
    public RecordAirlineDecisionValidator()
    {
        RuleFor(c => c.AirlineReference).NotEmpty().When(c => c.Accepted);
        RuleFor(c => c.Reason).NotEmpty().When(c => !c.Accepted);
    }
}

internal sealed class SubmitClaimToAirlineHandler(
    IClaimRepository repository, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<SubmitClaimToAirlineCommand, Result>
{
    public async Task<Result> Handle(SubmitClaimToAirlineCommand command, CancellationToken cancellationToken)
    {
        var claim = await repository.GetAsync(new ClaimId(command.ClaimId), cancellationToken);

        // Resource-based authorisation. Return NotFound (not Forbidden) so IDs can't be probed (IDOR).
        if (claim is null || claim.TravellerId != currentUser.Id)
        {
            return ClaimErrors.NotFound(command.ClaimId);
        }

        // HTTP If-Match → optimistic concurrency at the API level.
        if (command.ExpectedVersion is { } expected && expected != claim.Version)
        {
            return ClaimErrors.StaleVersion(expected, claim.Version);
        }

        var result = claim.SubmitToAirline(timeProvider.GetUtcNow());
        if (result.IsSuccess)
        {
            await repository.SaveAsync(claim, cancellationToken);
        }

        return result;
    }
}

/// <summary>Ops-only: records the airline's answer (in production this would come from an airline integration).</summary>
internal sealed class RecordAirlineDecisionHandler(IClaimRepository repository, TimeProvider timeProvider)
    : ICommandHandler<RecordAirlineDecisionCommand, Result>
{
    public async Task<Result> Handle(RecordAirlineDecisionCommand command, CancellationToken cancellationToken)
    {
        var claim = await repository.GetAsync(new ClaimId(command.ClaimId), cancellationToken);
        if (claim is null)
        {
            return ClaimErrors.NotFound(command.ClaimId);
        }

        var result = claim.RecordAirlineDecision(command.Accepted, command.AirlineReference, command.Reason, timeProvider.GetUtcNow());
        if (result.IsSuccess)
        {
            await repository.SaveAsync(claim, cancellationToken);
        }

        return result;
    }
}

public static class ClaimErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("claim.not_found", $"Claim {id} was not found.");

    public static Error StaleVersion(int expected, int actual) =>
        Error.Conflict("claim.stale_version", $"Claim was modified (expected version {expected}, current {actual}). Reload and retry.");
}
