using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudTransferOfferTargetRequestBody(string Kind, uint? ItemBiotaId, Guid? StackLotId);

public sealed record CloudCreateTransferOfferRequestBody(string RecipientCharacterName, IReadOnlyList<CloudTransferOfferTargetRequestBody> Targets);

public sealed record CloudResolveTransferOfferRequestBody(int ExpectedVersion);

/// <summary>
/// Issue #39's Transfer Offer web surface (XFER-001, XFER-002): sending, viewing, accepting,
/// declining, and cancelling. Mirrors <c>CloudWithdrawalEndpoints</c>'s established security shape
/// exactly -- origin check, Main-Account-only session, CSRF on every mutation -- since a Transfer
/// Offer is exactly the same class of asset-moving action WDR-001 already established the discipline
/// for. "Whose offer" for accept/decline/cancel is resolved from the URL's <c>offerId</c> plus the
/// caller's own session, never a client-supplied account field; the gateway itself independently
/// re-derives and enforces which side (sender vs. recipient) may perform which transition.
/// </summary>
public static class CloudTransferOfferEndpoints
{
    public static IEndpointRouteBuilder MapCloudTransferOfferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/transfer-offers", HandleListAsync);
        endpoints.MapPost("/transfer-offers", HandleCreateAsync);
        endpoints.MapPost("/transfer-offers/{offerId}/accept", HandleAcceptAsync);
        endpoints.MapPost("/transfer-offers/{offerId}/decline", HandleDeclineAsync);
        endpoints.MapPost("/transfer-offers/{offerId}/cancel", HandleCancelAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferReader offerReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var (session, sessionError) = await CloudEndpointSessionHelpers.TryRequireMainAccountSessionAsync(
            httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (sessionError is not null)
        {
            return sessionError;
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, session!.AccountId);
        var sent = await offerReader.GetSentAsync(options.ShardId, ownerId, cancellationToken);
        var received = await offerReader.GetReceivedAsync(options.ShardId, ownerId, cancellationToken);

        return Results.Ok(new
        {
            sent = sent.Select(ToWireResponse),
            received = received.Select(ToWireResponse),
        });
    }

    private static async Task<IResult> HandleCreateAsync(
        HttpContext httpContext,
        CloudCreateTransferOfferRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferService offerService,
        CloudBackendOptions options,
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

        if (string.IsNullOrWhiteSpace(request.RecipientCharacterName) || request.Targets is null || request.Targets.Count == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var requestTargets = new List<CloudTransferOfferRequestTarget>(request.Targets.Count);
        foreach (var target in request.Targets)
        {
            if (target.Kind == "Item" && target.ItemBiotaId is > 0)
            {
                requestTargets.Add(CloudTransferOfferRequestTarget.ForItem(target.ItemBiotaId.Value));
            }
            else if (target.Kind == "StackLot" && target.StackLotId is { } lotId && lotId != Guid.Empty)
            {
                requestTargets.Add(CloudTransferOfferRequestTarget.ForStackLot(lotId));
            }
            else
            {
                return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var outcome = await offerService.CreateAsync(
            options.ShardId, session!.AccountId, request.RecipientCharacterName, requestTargets, Guid.NewGuid(), cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(ToWireResponse(outcome.Value!, options.ShardId)),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static Task<IResult> HandleAcceptAsync(
        HttpContext httpContext, Guid offerId, CloudResolveTransferOfferRequestBody request,
        ICloudWebSessionStore sessionStore, ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferService offerService, CloudBackendOptions options, CancellationToken cancellationToken) =>
        HandleResolveAsync(httpContext, offerId, request, sessionStore, accountOwnershipResolver, offerService, options, offerService.AcceptAsync, cancellationToken);

    private static Task<IResult> HandleDeclineAsync(
        HttpContext httpContext, Guid offerId, CloudResolveTransferOfferRequestBody request,
        ICloudWebSessionStore sessionStore, ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferService offerService, CloudBackendOptions options, CancellationToken cancellationToken) =>
        HandleResolveAsync(httpContext, offerId, request, sessionStore, accountOwnershipResolver, offerService, options, offerService.DeclineAsync, cancellationToken);

    private static Task<IResult> HandleCancelAsync(
        HttpContext httpContext, Guid offerId, CloudResolveTransferOfferRequestBody request,
        ICloudWebSessionStore sessionStore, ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferService offerService, CloudBackendOptions options, CancellationToken cancellationToken) =>
        HandleResolveAsync(httpContext, offerId, request, sessionStore, accountOwnershipResolver, offerService, options, offerService.CancelAsync, cancellationToken);

    private static async Task<IResult> HandleResolveAsync(
        HttpContext httpContext,
        Guid offerId,
        CloudResolveTransferOfferRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudTransferOfferService offerService,
        CloudBackendOptions options,
        Func<Guid, uint, int, CancellationToken, Task<CloudBoundaryOutcome<CloudTransferOfferRecord>>> resolve,
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

        if (offerId == Guid.Empty)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await resolve(offerId, session!.AccountId, request.ExpectedVersion, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(ToWireResponse(outcome.Value!, options.ShardId)),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static object ToWireResponse(CloudTransferOfferRecord offer, string shardId) => new
    {
        id = offer.Id,
        senderOwnerId = offer.SenderAccountId,
        recipientOwnerId = offer.RecipientAccountId,
        status = offer.Status.ToString(),
        version = offer.Version,
        createdAtUtc = offer.CreatedAtUtc,
        expiresAtUtc = offer.ExpiresAtUtc,
    };

    private static object ToWireResponse(CloudTransferOfferSummary offer) => new
    {
        id = offer.Id,
        senderOwnerId = offer.SenderAccountId,
        recipientOwnerId = offer.RecipientAccountId,
        status = offer.Status.ToString(),
        version = offer.Version,
        createdAtUtc = offer.CreatedAtUtc,
        expiresAtUtc = offer.ExpiresAtUtc,
        targets = offer.Targets.Select(target => new
        {
            kind = target.Kind.ToString(),
            itemBiotaId = target.ItemBiotaId,
            stackLotId = target.StackLotId,
        }),
    };
}
