using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudSetSharingGrantRequestBody(string GranteeCharacterName, string Level);

/// <summary>
/// Issue #39's personal Sharing Grant web surface (SHARE-001..004): viewing grants given/received and
/// setting (creating, changing, or explicitly revoking) a grant. Mirrors
/// <c>CloudWithdrawalEndpoints</c>'s established security shape -- origin check, Main-Account-only
/// session, CSRF on the mutating route. There is deliberately no separate "revoke" route: setting
/// <see cref="CloudSharingGrantLevel.None"/> through the same POST is SHARE-004's own "explicit None
/// is a real, auditable denial," not a distinct action.
/// </summary>
public static class CloudSharingGrantEndpoints
{
    public static IEndpointRouteBuilder MapCloudSharingGrantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sharing-grants", HandleListAsync);
        endpoints.MapPost("/sharing-grants", HandleSetAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudSharingGrantReader grantReader,
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
        var given = await grantReader.GetGivenAsync(options.ShardId, ownerId, cancellationToken);
        var received = await grantReader.GetReceivedAsync(options.ShardId, ownerId, cancellationToken);

        return Results.Ok(new
        {
            given = given.Select(ToWireResponse),
            received = received.Select(ToWireResponse),
        });
    }

    private static async Task<IResult> HandleSetAsync(
        HttpContext httpContext,
        CloudSetSharingGrantRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudSharingGrantService grantService,
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

        if (string.IsNullOrWhiteSpace(request.GranteeCharacterName) || !Enum.TryParse<CloudSharingGrantLevel>(request.Level, out var level))
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await grantService.SetAsync(options.ShardId, session!.AccountId, request.GranteeCharacterName, level, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(ToWireResponse(outcome.Value!)),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static object ToWireResponse(CloudSharingGrantRecord grant) => new
    {
        id = grant.Id,
        ownerId = grant.OwnerId,
        granteeId = grant.GranteeId,
        level = grant.Level.ToString(),
        version = grant.Version,
        updatedAtUtc = grant.UpdatedAtUtc,
    };
}
