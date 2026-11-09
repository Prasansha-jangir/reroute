using Reroute.BuildingBlocks.Cqrs;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;

namespace Reroute.Claims.Application.Claims;

public sealed record GetClaimQuery(Guid ClaimId) : IQuery<Result<ClaimDto>>;

public sealed record ListMyClaimsQuery(ClaimStatus? Status, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ClaimSummaryDto>>;

public sealed record GetClaimTimelineQuery(Guid ClaimId) : IQuery<Result<IReadOnlyList<ClaimTimelineEntryDto>>>;

internal sealed class GetClaimHandler(IClaimReadStore readStore, ICurrentUser currentUser)
    : IQueryHandler<GetClaimQuery, Result<ClaimDto>>
{
    public async Task<Result<ClaimDto>> Handle(GetClaimQuery query, CancellationToken cancellationToken)
    {
        var claim = await readStore.GetAsync(query.ClaimId, cancellationToken);
        return claim is not null && (claim.TravellerId == currentUser.Id || currentUser.IsInRole("ops"))
            ? claim
            : ClaimErrors.NotFound(query.ClaimId);
    }
}

internal sealed class ListMyClaimsHandler(IClaimReadStore readStore, ICurrentUser currentUser)
    : IQueryHandler<ListMyClaimsQuery, PagedResult<ClaimSummaryDto>>
{
    public Task<PagedResult<ClaimSummaryDto>> Handle(ListMyClaimsQuery query, CancellationToken cancellationToken) =>
        readStore.ListByTravellerAsync(
            currentUser.Id,
            query.Status,
            Math.Max(1, query.Page),
            Math.Clamp(query.PageSize, 1, 100),
            cancellationToken);
}

internal sealed class GetClaimTimelineHandler(IClaimReadStore readStore, ICurrentUser currentUser)
    : IQueryHandler<GetClaimTimelineQuery, Result<IReadOnlyList<ClaimTimelineEntryDto>>>
{
    public async Task<Result<IReadOnlyList<ClaimTimelineEntryDto>>> Handle(GetClaimTimelineQuery query, CancellationToken cancellationToken)
    {
        var claim = await readStore.GetAsync(query.ClaimId, cancellationToken);
        if (claim is null || (claim.TravellerId != currentUser.Id && !currentUser.IsInRole("ops")))
        {
            return ClaimErrors.NotFound(query.ClaimId);
        }

        return Result<IReadOnlyList<ClaimTimelineEntryDto>>.Success(await readStore.GetTimelineAsync(query.ClaimId, cancellationToken));
    }
}
