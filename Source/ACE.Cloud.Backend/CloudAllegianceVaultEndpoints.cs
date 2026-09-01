using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudAllegianceVaultTargetRequestBody(uint ActingCharacterId, string Kind, uint? ItemBiotaId, Guid? StackLotId);

/// <summary>
/// Issue #39's Allegiance Vault web surface (VAULT-001..003): listing the caller's current Acting
/// Characters, viewing one character's live allegiance vault contents, and contributing/taking.
/// Mirrors <c>CloudWithdrawalEndpoints</c>'s established security shape. Vault contents reuse
/// <see cref="ICloudInventoryQueryReader"/> exactly like <c>/inventory/pages</c> does, only with the
/// selected character's Allegiance Vault owner ID as the sole authorized owner instead of the
/// caller's personal owner ID -- there is deliberately no new inventory read path, only a
/// differently scoped <see cref="CloudLiveStreamViewer"/> (matching <c>CloudActivityLedgerEndpoints</c>'s
/// own established "vault scope adds the Allegiance Vault owner ID" pattern). The acting-characters
/// list is read-only convenience for the selector; <see cref="ICloudAllegianceVaultTransactionService"/>
/// independently revalidates the submitted character live against ace_shard for every mutation
/// (VAULT-001), so a stale list entry can never itself authorize a contribute/take.
/// </summary>
public static class CloudAllegianceVaultEndpoints
{
    public static IEndpointRouteBuilder MapCloudAllegianceVaultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/allegiance-vault/acting-characters", HandleListActingCharactersAsync);
        endpoints.MapGet("/allegiance-vault", HandleGetVaultAsync);
        endpoints.MapPost("/allegiance-vault/contribute", HandleContributeAsync);
        endpoints.MapPost("/allegiance-vault/take", HandleTakeAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListActingCharactersAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudActingCharacterReader actingCharacterReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var (session, sessionError) = await CloudEndpointSessionHelpers.TryRequireMainAccountSessionAsync(
            httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (sessionError is not null)
        {
            return sessionError;
        }

        var characters = await actingCharacterReader.GetCurrentCharactersAsync(options.ShardId, session!.AccountId, cancellationToken);

        return Results.Ok(new
        {
            characters = characters.Select(character => new
            {
                characterId = character.CharacterId,
                characterName = character.CharacterName,
                monarchId = character.MonarchId,
                hasAllegiance = character.MonarchId is not null,
            }),
        });
    }

    private static async Task<IResult> HandleGetVaultAsync(
        HttpContext httpContext,
        uint characterId,
        int? page,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudActingCharacterReader actingCharacterReader,
        ICloudInventoryQueryReader inventoryQueryReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var (session, sessionError) = await CloudEndpointSessionHelpers.TryRequireMainAccountSessionAsync(
            httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (sessionError is not null)
        {
            return sessionError;
        }

        if (characterId == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // Never trust a client-supplied monarch/vault ID directly: only a character this session's own
        // account currently owns, per the same cache the acting-character list itself reads, may name a
        // vault to view (the mutation endpoints below separately, live, revalidate the same rule).
        var characters = await actingCharacterReader.GetCurrentCharactersAsync(options.ShardId, session!.AccountId, cancellationToken);
        var character = characters.FirstOrDefault(c => c.CharacterId == characterId);
        if (character is null || character.MonarchId is null)
        {
            return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        }

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(options.ShardId, character.MonarchId.Value);
        var viewer = CloudLiveStreamViewer.ForOwners([vaultOwnerId]);
        var request = new CloudInventoryQueryRequest
        {
            Category = null,
            Page = page is > 0 ? page.Value : 1,
            SortKey = CloudInventorySortKey.Name,
            SortDirection = CloudInventorySortDirection.Ascending,
        };

        var response = await inventoryQueryReader.QueryAsync(options.ShardId, viewer, request, cancellationToken);

        return Results.Ok(new
        {
            characterId = character.CharacterId,
            monarchId = character.MonarchId,
            page = new
            {
                pageNumber = response.Page.PageNumber,
                totalPages = response.Page.TotalPages,
                items = response.Page.Items.Select(item => new
                {
                    itemId = item.ItemId.Value,
                    stackLotId = item.StackLotId?.Value,
                    name = item.Name,
                    quantity = item.Quantity,
                    value = item.Value,
                    version = item.Version.Value,
                }),
            },
        });
    }

    private static Task<IResult> HandleContributeAsync(
        HttpContext httpContext, CloudAllegianceVaultTargetRequestBody request,
        ICloudWebSessionStore sessionStore, ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAllegianceVaultTransactionService vaultService, CloudBackendOptions options, CancellationToken cancellationToken) =>
        HandleTransferAsync(httpContext, request, sessionStore, accountOwnershipResolver, vaultService, options, vaultService.ContributeAsync, cancellationToken);

    private static Task<IResult> HandleTakeAsync(
        HttpContext httpContext, CloudAllegianceVaultTargetRequestBody request,
        ICloudWebSessionStore sessionStore, ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAllegianceVaultTransactionService vaultService, CloudBackendOptions options, CancellationToken cancellationToken) =>
        HandleTransferAsync(httpContext, request, sessionStore, accountOwnershipResolver, vaultService, options, vaultService.TakeAsync, cancellationToken);

    private static async Task<IResult> HandleTransferAsync(
        HttpContext httpContext,
        CloudAllegianceVaultTargetRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAllegianceVaultTransactionService vaultService,
        CloudBackendOptions options,
        Func<string, uint, uint, CloudReservationTarget, Guid, CancellationToken, Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>>> transfer,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var (session, sessionError) = await CloudEndpointSessionHelpers.TryRequireMainAccountSessionAsync(
            httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (sessionError is not null)
        {
            return sessionError;
        }

        if (!CloudEndpointSessionHelpers.CsrfMatches(httpContext, session!))
        {
            return Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.ActingCharacterId == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        CloudReservationTarget target;
        if (request.Kind == "Item" && request.ItemBiotaId is > 0)
        {
            target = CloudReservationTarget.ForItem(new CloudItemId(request.ItemBiotaId.Value));
        }
        else if (request.Kind == "StackLot" && request.StackLotId is { } lotId && lotId != Guid.Empty)
        {
            target = CloudReservationTarget.ForStackLot(new CloudStackLotId(lotId));
        }
        else
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await transfer(options.ShardId, session!.AccountId, request.ActingCharacterId, target, Guid.NewGuid(), cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                itemBiotaId = outcome.Value!.BiotaId,
                personalOwnerId = outcome.Value.PersonalOwnerId,
                vaultOwnerId = outcome.Value.VaultOwnerId,
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
