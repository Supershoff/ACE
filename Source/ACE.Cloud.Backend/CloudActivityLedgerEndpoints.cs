using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// The scoped Activity Ledger HTTP surface (issue #34, EVT-001/EVT-002). Owner scope is the
/// authenticated viewer's own Main/Linked ownership group; Vault scope adds the Allegiance Vault
/// owner ID(s) for every allegiance the authenticated account's characters currently belong to
/// (VAULT-001's "allegiance members see their complete vault history"); an ADM-001-revalidated
/// administrator instead sees the complete global ledger, including the three admin-only categories.
/// There is no separate "Shared" query parameter yet: <see cref="CloudActivityLedgerQueryEngine"/>'s
/// doc comment explains why it is the exact same <see cref="CloudLiveStreamViewer.AuthorizedOwnerIds"/>
/// mechanism Owner scope already uses, waiting only on Sharing Grants (SHARE-001..004) to populate it
/// with a grantor's owner ID.
/// </summary>
public static class CloudActivityLedgerEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapCloudActivityLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/activity", HandleQueryAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleQueryAsync(
        HttpContext httpContext,
        int? page,
        int? pageSize,
        bool? vault,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAuthBridgeClient authBridgeClient,
        ICloudCharacterAllegianceVaultReader vaultReader,
        ICloudActivityLedgerQueryReader ledgerQueryReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await CloudEndpointSessionHelpers.TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var pageNumber = page ?? 1;
        if (pageNumber <= 0)
        {
            return Results.Json(new { error = "invalid_page" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var resolvedPageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        // ADM-001: "revalidate on every sensitive request" -- the exact same fresh Auth Bridge
        // access-level read /admin/whoami uses, never a client-supplied flag or cached session claim.
        var freshAccessLevel = await authBridgeClient.GetFreshAccessLevelAsync(session.AccountId, cancellationToken);
        var isAdmin = freshAccessLevel is not null && CloudAdminAccessRevalidationPolicy.Evaluate(freshAccessLevel.Value).IsAuthorized;

        CloudLiveStreamViewer viewer;
        if (isAdmin)
        {
            viewer = CloudLiveStreamViewer.ForAdmin();
        }
        else
        {
            var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
            if (effectiveMainAccountId != session.AccountId)
            {
                return Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var ownerIds = new List<Guid> { CloudOwnerIdentity.ForAccount(options.ShardId, effectiveMainAccountId) };

            // Vault scope: add this account's own current Allegiance Vault owner ID(s) (VAULT-001).
            // This does not yet enumerate the full Main/Linked group's characters -- only the
            // authenticated account's own -- a narrower, documented, honest scope until account
            // linking exposes the full character roster for a group.
            if (vault == true)
            {
                var vaultOwnerIds = await vaultReader.GetCurrentAllegianceVaultOwnerIdsAsync(options.ShardId, session.AccountId, cancellationToken);
                ownerIds.AddRange(vaultOwnerIds);
            }

            viewer = CloudLiveStreamViewer.ForOwners(ownerIds);
        }

        var pageResult = await ledgerQueryReader.QueryAsync(options.ShardId, viewer, pageNumber, resolvedPageSize, cancellationToken);
        return Results.Ok(ToWireResponse(pageResult));
    }

    private static object ToWireResponse(CloudActivityLedgerPage page) => new
    {
        entries = page.Entries.Select(entry => new
        {
            id = entry.Id,
            correlationId = entry.CorrelationId,
            category = entry.Category.ToString(),
            eventType = entry.EventType,
            ownerId = entry.OwnerId,
            itemBiotaId = entry.ItemBiotaId,
            outcome = entry.Outcome,
            reason = entry.Reason,
            occurredAtUtc = entry.OccurredAtUtc,
        }),
        pageNumber = page.PageNumber,
        pageSize = page.PageSize,
        totalCount = page.TotalCount,
        totalPages = page.TotalPages,
    };
}
