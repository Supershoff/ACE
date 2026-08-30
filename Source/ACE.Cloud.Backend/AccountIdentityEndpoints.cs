using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudAccountLinkRequestBody(string SourceAccountName, string SourcePassword);

public sealed record CloudAccountUnlinkRequestBody(uint LinkedAccountId);

/// <summary>
/// Issue #33's account identity, Main/Linked display, and destructive account-linking HTTP surface
/// (AUTH-003, AUTH-005..009). Every route resolves "which account" exclusively from the
/// authenticated session cookie, never from a client-supplied field, matching
/// <c>CloudInventoryEndpoints</c>' own security baseline. Linking/unlinking are Main Account-only
/// mutations (AUTH-004) enforced here server-side, not only by the web client's own
/// <c>RequireMainAccount</c> route guard.
/// </summary>
public static class AccountIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAccountIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/identity", HandleGetIdentityAsync);
        endpoints.MapPost("/account/link", HandleLinkAsync);
        endpoints.MapPost("/account/unlink", HandleUnlinkAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetIdentityAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAccountLinkAdministration linkAdministration,
        ICloudDisplayCharacterReader displayCharacterReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await CloudEndpointSessionHelpers.TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var mainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        var isMain = mainAccountId == session.AccountId;

        // AUTH-004: a Linked Account's own credentials can never see who else shares its Main
        // Account's group -- only the Main Account's own session may list linked accounts.
        var linkedAccounts = isMain
            ? await linkAdministration.GetActiveLinksAsync(options.ShardId, mainAccountId, cancellationToken)
            : [];

        var ownershipGroupId = await linkAdministration.TryGetOwnershipGroupIdAsync(options.ShardId, mainAccountId, cancellationToken);
        var displayCharacter = ownershipGroupId is null
            ? null
            : await displayCharacterReader.GetCurrentSelectionAsync(ownershipGroupId.Value, cancellationToken);

        return Results.Ok(new
        {
            accountId = session.AccountId,
            accountKind = isMain ? "Main" : "Linked",
            mainAccountId,
            linkedAccounts = linkedAccounts.Select(link => new
            {
                accountId = link.LinkedAccountId,
                linkedAtUtc = link.LinkedAtUtc,
            }),
            displayCharacter = displayCharacter?.CharacterId is null
                ? null
                : new { characterId = displayCharacter.CharacterId, characterName = displayCharacter.CharacterName },
        });
    }

    private static async Task<IResult> HandleLinkAsync(
        HttpContext httpContext,
        CloudAccountLinkRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAccountLinkAdministration linkAdministration,
        ICloudAuthBridgeClient authBridgeClient,
        CloudPrivateServiceKeyRing keyRing,
        CloudBackendOptions options,
        ILogger<Program> logger,
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

        if (string.IsNullOrWhiteSpace(request.SourceAccountName) || string.IsNullOrWhiteSpace(request.SourcePassword))
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // AUTH-005/AUTH-006's "delayed confirmation" and destructive-warning UX belong entirely to
        // the web client; this endpoint's own irreversible-mutation safeguard is requiring the
        // *source* account's own password (never the Main Account's), proved the same way login
        // proves a password: through the private ACE Auth Bridge, which never gives Cloud Mule
        // direct password-hash verification (AUTH-002).
        var grantResult = await authBridgeClient.IssueGrantAsync(request.SourceAccountName, request.SourcePassword, CloudAuthAudiences.CloudBackend, cancellationToken);
        if (grantResult.Kind == CloudAuthBridgeGrantOutcomeKind.Unavailable)
        {
            return Results.Json(new { error = "authentication_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (grantResult.Kind != CloudAuthBridgeGrantOutcomeKind.Issued)
        {
            // Never distinguish "unknown account" / "wrong password" -- matches the login endpoint's
            // own enumeration-safety rule.
            return Results.Json(new { error = "invalid_source_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var validation = CloudAuthGrantValidator.Validate(grantResult.Grant, CloudAuthAudiences.CloudBackend, DateTime.UtcNow, keyRing);
        if (!validation.IsValid)
        {
            logger.LogWarning("Auth Bridge issued an account-link grant that failed independent backend validation: {Kind}.", validation.Kind);
            return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var sourceAccountId = validation.Grant!.AccountId;

        var outcome = await linkAdministration.LinkAsync(
            options.ShardId, session!.AccountId, sourceAccountId, Guid.NewGuid(), cancellationToken: cancellationToken);

        return Results.Ok(new { approved = outcome.IsApproved, rejectionCode = outcome.RejectionCode.ToString() });
    }

    private static async Task<IResult> HandleUnlinkAsync(
        HttpContext httpContext,
        CloudAccountUnlinkRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudAccountLinkAdministration linkAdministration,
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

        if (request.LinkedAccountId == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await linkAdministration.UnlinkAsync(
            options.ShardId, session!.AccountId, request.LinkedAccountId, Guid.NewGuid(), cancellationToken);

        return Results.Ok(new { approved = outcome.IsApproved, rejectionCode = outcome.RejectionCode.ToString() });
    }
}
