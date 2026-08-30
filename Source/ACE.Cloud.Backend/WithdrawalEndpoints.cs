using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// Issue #33's Withdrawal Token/Reservation web HTTP surface (WDR-001, WDR-002, WDR-003, WDR-006,
/// WDR-008): opens a Withdrawal Reservation over a caller-selected mix of whole Cloud Items and
/// (whole or split-partial) Cloud Stack Lot quantities, mints its Withdrawal Token, and allows
/// explicit pre-redemption cancellation. Every route resolves "whose inventory" exclusively from the
/// authenticated session (matching <c>CloudInventoryEndpoints</c>), and every mutation checks
/// world-boundary health first (WDR-008: "if ACE is down, block token creation") -- opening/cancelling
/// a reservation itself touches only the ace_cloud schema (see
/// <see cref="ICloudWithdrawalReservationGateway"/>'s implementation doc comment) and would otherwise
/// succeed even while the ACE world process is offline, so this endpoint enforces WDR-008 as an
/// explicit precondition rather than relying on that to fail naturally.
/// </summary>
public static class WithdrawalEndpoints
{
    private static readonly TimeSpan TokenTimeToLive = TimeSpan.FromMinutes(15);

    public static IEndpointRouteBuilder MapWithdrawalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/withdrawal/reservations", HandleOpenReservationAsync);
        endpoints.MapPost("/withdrawal/reservations/{reservationId:guid}/cancel", HandleCancelReservationAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleOpenReservationAsync(
        HttpContext httpContext,
        OpenWithdrawalReservationRequest request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudWithdrawalReservationGateway reservationGateway,
        ICloudStackLotSplitGateway stackLotSplitGateway,
        ICloudWorldBoundaryHealthProbe worldBoundaryHealthProbe,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.Targets is null || request.Targets.Count == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var viewerResult = await TryResolveOwnMainAccountAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        var accountId = viewerResult.AccountId;
        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, accountId);

        // WDR-008: "if ACE is down, block token creation."
        var worldBoundaryHealth = await worldBoundaryHealthProbe.CheckAsync(cancellationToken);
        if (!worldBoundaryHealth.IsHealthy)
        {
            return Results.Json(new { error = "world_boundary_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var resolvedTargets = new List<CloudWithdrawalReservationRequestTarget>(request.Targets.Count);
        foreach (var requested in request.Targets)
        {
            var resolved = await TryResolveTargetAsync(requested, ownerId, stackLotSplitGateway, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            resolvedTargets.Add(resolved.Target!);
        }

        var token = CloudWithdrawalTokenHasher.Generate();
        var idempotencyKey = ReadIdempotencyKey(httpContext);

        var outcome = await reservationGateway.ReserveForWithdrawalAsync(
            resolvedTargets, options.ShardId, ownerId, token.Hash, TokenTimeToLive, idempotencyKey, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                reservationId = outcome.Value!.Id,
                // WDR-001's high-entropy secret: shown to the requester exactly once, in this
                // response body only -- never logged, never placed in a URL (security baseline).
                tokenSecret = token.Secret,
                version = outcome.Value.Version,
                expiresAtUtc = outcome.Value.ExpiresAtUtc,
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(
                new { error = "reservation_rejected", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<IResult> HandleCancelReservationAsync(
        HttpContext httpContext,
        Guid reservationId,
        CancelWithdrawalReservationRequest request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudWithdrawalReservationGateway reservationGateway,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var viewerResult = await TryResolveOwnMainAccountAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        // CancelWithdrawalReservationAsync takes no owner identity to check against (see its
        // implementation doc comment), so this endpoint authorizes the request itself first
        // (security baseline: "Authorization is server-side on every object query and command").
        var reservation = await reservationGateway.TryGetReservationAsync(reservationId, cancellationToken);
        if (reservation is null || reservation.OwnerId != CloudOwnerIdentity.ForAccount(options.ShardId, viewerResult.AccountId))
        {
            return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        }

        var outcome = await reservationGateway.CancelWithdrawalReservationAsync(reservationId, request.ExpectedVersion, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new { cancelled = true }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(
                new { error = "cancel_rejected", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<(CloudWithdrawalReservationRequestTarget? Target, IResult? Error)> TryResolveTargetAsync(
        WithdrawalReservationTargetRequest requested,
        Guid ownerId,
        ICloudStackLotSplitGateway stackLotSplitGateway,
        CancellationToken cancellationToken)
    {
        var invalid = Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);

        switch (requested.Kind)
        {
            case "Item":
                if (requested.ItemId is null || requested.ItemId.Value == 0)
                {
                    return (null, invalid);
                }

                return (CloudWithdrawalReservationRequestTarget.ForItem(requested.ItemId.Value), null);

            case "StackLot":
                if (requested.StackLotId is null || requested.StackLotId.Value == Guid.Empty)
                {
                    return (null, invalid);
                }

                if (requested.Quantity is null)
                {
                    // Full-quantity default (INV-002); ownership/existence is checked inside
                    // ReserveForWithdrawalAsync itself.
                    return (CloudWithdrawalReservationRequestTarget.ForStackLot(requested.StackLotId.Value), null);
                }

                if (requested.Quantity.Value <= 0 || requested.ExpectedVersion is null)
                {
                    return (null, invalid);
                }

                var snapshot = await stackLotSplitGateway.TryGetLotSnapshotAsync(requested.StackLotId.Value, cancellationToken);
                if (snapshot is null)
                {
                    return (null, Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound));
                }

                // Authorization is server-side (security baseline): only the lot's current owner may
                // split it, checked fresh here rather than trusted from the client's request.
                if (snapshot.OwnerId != ownerId)
                {
                    return (null, Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound));
                }

                if (requested.Quantity.Value >= snapshot.Quantity)
                {
                    // The whole lot was requested by quantity; reserve it directly instead of
                    // splitting off everything and leaving an empty remainder (CloudStackLotTransactionAuthority.SplitLotAsync
                    // requires a positive remainder by construction).
                    return (CloudWithdrawalReservationRequestTarget.ForStackLot(requested.StackLotId.Value), null);
                }

                var splitOutcome = await stackLotSplitGateway.SplitLotAsync(
                    requested.StackLotId.Value, requested.ExpectedVersion.Value, ownerId, requested.Quantity.Value, cancellationToken);

                if (splitOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
                {
                    return (null, Results.Json(
                        new { error = "split_rejected", reason = splitOutcome.Reason }, statusCode: StatusCodes.Status409Conflict));
                }

                return (CloudWithdrawalReservationRequestTarget.ForStackLot(splitOutcome.Value!.NewLot.Id), null);

            default:
                return (null, invalid);
        }
    }

    private static Guid ReadIdempotencyKey(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers[AccountEndpoints.IdempotencyKeyHeaderName].ToString();
        return Guid.TryParse(header, out var key) ? key : Guid.NewGuid();
    }

    private static async Task<(uint AccountId, IResult? Error)> TryResolveOwnMainAccountAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (string.IsNullOrEmpty(secret))
        {
            return (0, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var session = await sessionStore.TryGetActiveSessionAsync(CloudWebSessionSecretHasher.Hash(secret), DateTime.UtcNow, cancellationToken);
        if (session is null)
        {
            return (0, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        // Security baseline "CSRF protection", matching AuthSessionEndpoints' own logout check.
        var submittedCsrfToken = httpContext.Request.Headers[AuthSessionEndpoints.CsrfHeaderName].ToString();
        if (!CloudCsrfTokenValidator.Matches(submittedCsrfToken, session.CsrfToken))
        {
            return (0, Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden));
        }

        // AUTH-004: only Main Account credentials may manage the unified Cloud Inventory, including
        // creating/cancelling Withdrawal Tokens.
        var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            return (0, Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (session.AccountId, null);
    }
}
