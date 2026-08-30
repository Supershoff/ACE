using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudWithdrawalTargetRequestBody(string Kind, uint? ItemBiotaId, Guid? StackLotId);

public sealed record CloudCreateWithdrawalRequestBody(IReadOnlyList<CloudWithdrawalTargetRequestBody> Targets);

public sealed record CloudCancelWithdrawalRequestBody(int ExpectedVersion);

public sealed record CloudSplitStackLotRequestBody(int ExpectedVersion, int Quantity);

/// <summary>
/// Issue #33's Withdrawal Token web creation/status/cancellation surface (WDR-001, WDR-002, WDR-003,
/// WDR-006, WDR-008), plus the partial-quantity stack split it depends on (CONTEXT.md: "a caller who
/// wants a smaller amount must first split a new lot for exactly that quantity... and reserve that
/// new lot"). Every route resolves "whose reservation" exclusively from the authenticated session,
/// matching <c>CloudInventoryEndpoints</c>' and <c>AccountIdentityEndpoints</c>' own security
/// baseline. Only Main Account credentials may create, view, or cancel a Withdrawal Reservation
/// (AUTH-004) -- a Withdrawal Token itself may later be redeemed in-game by any character in the
/// Main/Linked group (WDR-002), but issuing and managing it from the web is Main-only asset
/// management, exactly like every other mutating inventory action.
/// </summary>
public static class CloudWithdrawalEndpoints
{
    private static readonly TimeSpan TokenTimeToLive = TimeSpan.FromMinutes(15);

    public static IEndpointRouteBuilder MapCloudWithdrawalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/withdrawal-locations", HandleGetWithdrawalLocationsAsync);
        endpoints.MapGet("/withdrawals/current", HandleGetCurrentWithdrawalAsync);
        endpoints.MapPost("/withdrawals", HandleCreateWithdrawalAsync);
        endpoints.MapPost("/withdrawals/{reservationId}/cancel", HandleCancelWithdrawalAsync);
        endpoints.MapPost("/inventory/stack-lots/{lotId}/split", HandleSplitStackLotAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetWithdrawalLocationsAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudWithdrawalLocationReader locationReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await CloudEndpointSessionHelpers.TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var configuration = await locationReader.GetCurrentAsync(options.ShardId, cancellationToken);

        return Results.Ok(new
        {
            withdrawAnywhereEnabled = configuration.WithdrawAnywhereEnabled,
            namedLandblocks = configuration.NamedLandblocks.Select(landblock => new
            {
                id = landblock.Id,
                landblock = "0x" + landblock.Landblock.ToString("X4"),
                name = landblock.Name,
            }),
        });
    }

    private static async Task<IResult> HandleGetCurrentWithdrawalAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudWithdrawalReservationService reservationService,
        ICloudWithdrawalReservationReader reservationReader,
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
        var reservation = await reservationReader.TryGetActiveByOwnerAsync(ownerId, cancellationToken);
        if (reservation is null)
        {
            return Results.Ok(new { active = false });
        }

        var targets = await reservationService.GetReservationTargetsAsync(reservation.Id, cancellationToken);

        // WDR-001's single-reveal rule: the Withdrawal Token secret is returned exactly once, from
        // HandleCreateWithdrawalAsync's own response, and never again -- this reconciliation view
        // (used on page reload and by every other open tab/device, EVT-007) exposes only what a
        // reservation status view needs, never the secret itself.
        return Results.Ok(new
        {
            active = true,
            reservationId = reservation.Id,
            version = reservation.Version,
            expiresAtUtc = reservation.ExpiresAtUtc,
            targets = targets.Select(target => new
            {
                kind = target.Kind.ToString(),
                itemBiotaId = target.ItemBiotaId,
                stackLotId = target.StackLotId,
                quantity = target.Quantity,
            }),
        });
    }

    private static async Task<IResult> HandleCreateWithdrawalAsync(
        HttpContext httpContext,
        CloudCreateWithdrawalRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudWithdrawalReservationService reservationService,
        ICloudServiceAvailabilityReader serviceAvailabilityReader,
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

        if (request.Targets is null || request.Targets.Count == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var requestTargets = new List<CloudWithdrawalReservationRequestTarget>(request.Targets.Count);
        foreach (var target in request.Targets)
        {
            if (target.Kind == "Item" && target.ItemBiotaId is > 0)
            {
                requestTargets.Add(CloudWithdrawalReservationRequestTarget.ForItem(target.ItemBiotaId.Value));
            }
            else if (target.Kind == "StackLot" && target.StackLotId is { } lotId && lotId != Guid.Empty)
            {
                requestTargets.Add(CloudWithdrawalReservationRequestTarget.ForStackLot(lotId));
            }
            else
            {
                return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // ARCH-008/WDR-008: unlike every other Cloud mutation, opening a Withdrawal Reservation
        // requires the ACE world process itself to eventually redeem it, so creation specifically
        // (not the rest of the Cloud Mule web app) must refuse while the world boundary is
        // unavailable, even though the database and every off-world operation stay healthy.
        var mode = await serviceAvailabilityReader.GetCurrentModeAsync(cancellationToken);
        if (mode != CloudServiceAvailabilityMode.Operational)
        {
            return Results.Json(new { error = "world_boundary_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, session!.AccountId);
        var token = CloudWithdrawalTokenHasher.Generate();

        var outcome = await reservationService.ReserveForWithdrawalAsync(
            requestTargets, options.ShardId, ownerId, token.Hash, TokenTimeToLive, Guid.NewGuid(), cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                // The one and only response that ever carries the raw secret (security baseline:
                // never in a URL, log, or subsequent status read).
                secret = token.Secret,
                reservationId = outcome.Value!.Id,
                version = outcome.Value.Version,
                expiresAtUtc = outcome.Value.ExpiresAtUtc,
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<IResult> HandleCancelWithdrawalAsync(
        HttpContext httpContext,
        Guid reservationId,
        CloudCancelWithdrawalRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudWithdrawalReservationService reservationService,
        ICloudWithdrawalReservationReader reservationReader,
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

        if (reservationId == Guid.Empty)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // Authorization must be server-side on every command, not just every query: a reservation ID
        // is an opaque handle the client only ever learns for its own reservation, but the caller's
        // session alone does not prove it names that caller's own reservation. Look the reservation up
        // by the ID actually named in the URL -- an owner can hold several simultaneously active
        // reservations over disjoint targets, so "the caller's most recently created active
        // reservation" is not a valid proxy for "the reservation named in this request." Reuse
        // HandleGetAppraisalAsync's "one generic not_found for missing-or-foreign" discipline instead
        // of leaking whether a foreign reservation ID exists.
        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, session!.AccountId);
        var namedReservation = await reservationReader.TryGetByIdAsync(reservationId, cancellationToken);
        if (namedReservation is null || namedReservation.OwnerId != ownerId)
        {
            return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        }

        var outcome = await reservationService.CancelWithdrawalReservationAsync(reservationId, request.ExpectedVersion, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                reservationId = outcome.Value!.Id,
                version = outcome.Value.Version,
                status = outcome.Value.Status.ToString(),
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<IResult> HandleSplitStackLotAsync(
        HttpContext httpContext,
        Guid lotId,
        CloudSplitStackLotRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudStackLotSplitService splitService,
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

        if (lotId == Guid.Empty || request.Quantity <= 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, session!.AccountId);
        var outcome = await splitService.SplitOwnLotAsync(lotId, request.ExpectedVersion, ownerId, request.Quantity, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                remainingLot = new { id = outcome.Value!.RemainingLot.Id, quantity = outcome.Value.RemainingLot.Quantity, version = outcome.Value.RemainingLot.Version },
                newLot = new { id = outcome.Value.NewLot.Id, quantity = outcome.Value.NewLot.Quantity, version = outcome.Value.NewLot.Version },
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
