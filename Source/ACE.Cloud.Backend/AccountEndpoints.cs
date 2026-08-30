using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// Issue #33's account/display-settings and destructive account-linking HTTP surface (AUTH-003..009):
/// read-only account overview, plus link/unlink against <see cref="ICloudAccountLinkGateway"/>'s
/// already-transactional, idempotent, commit-time-revalidated policy. Every route resolves "which
/// account" exclusively from the authenticated session, matching <c>CloudInventoryEndpoints</c>'s
/// existing security-baseline discipline.
/// </summary>
public static class AccountEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/overview", HandleGetOverviewAsync);
        endpoints.MapPost("/account/link", HandleLinkAsync);
        endpoints.MapPost("/account/unlink", HandleUnlinkAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetOverviewAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountLinkGateway accountLinkGateway,
        ICloudDisplayCharacterGateway displayCharacterGateway,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var effectiveMainAccountId = await accountLinkGateway.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            // AUTH-004: a linked credential's web login "shows only that they are linked" -- never the
            // Main Account's identity, linked-account roster, or Display Character.
            return Results.Ok(new { isLinkedAccount = true });
        }

        var linkedAccountIds = (await accountLinkGateway.GetOwnershipGroupAccountIdsAsync(options.ShardId, session.AccountId, cancellationToken))
            .Where(id => id != session.AccountId)
            .ToArray();

        object? displayCharacter = null;
        var groupId = await accountLinkGateway.TryGetOwnershipGroupIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (groupId is not null)
        {
            var selection = await displayCharacterGateway.GetCurrentSelectionAsync(groupId.Value, cancellationToken);
            if (selection is { CharacterId: not null })
            {
                displayCharacter = new { characterId = selection.CharacterId, characterName = selection.CharacterName, totalLogins = selection.TotalLogins };
            }
        }

        return Results.Ok(new
        {
            isLinkedAccount = false,
            mainAccountId = session.AccountId,
            linkedAccountIds,
            displayCharacter,
        });
    }

    private static async Task<IResult> HandleLinkAsync(
        HttpContext httpContext,
        AccountLinkRequest request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountLinkGateway accountLinkGateway,
        ICloudDisplayCharacterGateway displayCharacterGateway,
        ICloudCharacterIdentityReader characterIdentityReader,
        ICloudAuthBridgeClient authBridgeClient,
        CloudAccountLinkAttemptRateLimiter rateLimiter,
        CloudPrivateServiceKeyRing keyRing,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.SourceAccountName) || string.IsNullOrWhiteSpace(request.SourcePassword))
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!HasValidCsrfToken(httpContext, session))
        {
            return Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var nowUtc = DateTime.UtcNow;

        // Keyed by the authenticated Main Account, not source IP: unlike login (where the caller has
        // no identity yet), linking always has one, and a shared IP (NAT, proxy) must not let one
        // account's link attempts exhaust another account's budget.
        var rateLimitResult = rateLimiter.RegisterAttempt(session.AccountId.ToString(), nowUtc);
        if (!rateLimitResult.IsAllowed)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(rateLimitResult.RetryAfter!.Value.TotalSeconds)).ToString();
            return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var effectiveMainAccountId = await accountLinkGateway.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            // AUTH-004: only Main Account credentials may manage the unified Cloud Inventory, so only
            // a Main Account session may initiate a link at all.
            return Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden);
        }

        // AUTH-007: "source password re-entry" -- proof the requester actually controls the source
        // account, verified the same way login proves the requester controls their own account:
        // through the private ACE Auth Bridge, never a Cloud-side password check.
        var sourceAccountId = await TryVerifySourceCredentialsAsync(
            request.SourceAccountName, request.SourcePassword, sessionStore, authBridgeClient, keyRing, options, nowUtc, cancellationToken);
        if (sourceAccountId is null)
        {
            return Results.Json(new { error = "invalid_source_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var idempotencyKey = ReadIdempotencyKey(httpContext);
        var outcome = await accountLinkGateway.LinkAsync(
            options.ShardId, session.AccountId, sourceAccountId.Value, idempotencyKey, wouldCreateActiveAuctionConflict: false, cancellationToken);

        if (!outcome.IsApproved)
        {
            return Results.Json(new { error = "link_rejected", reason = outcome.RejectionCode.ToString() }, statusCode: StatusCodes.Status409Conflict);
        }

        await ReselectDisplayCharacterAsync(
            options.ShardId, session.AccountId, CloudDisplayCharacterSelectionReason.RosterChanged,
            accountLinkGateway, characterIdentityReader, displayCharacterGateway, cancellationToken);

        return Results.Ok(new { approved = true });
    }

    private static async Task<IResult> HandleUnlinkAsync(
        HttpContext httpContext,
        AccountUnlinkRequest request,
        ICloudWebSessionStore sessionStore,
        ICloudAccountLinkGateway accountLinkGateway,
        ICloudDisplayCharacterGateway displayCharacterGateway,
        ICloudCharacterIdentityReader characterIdentityReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.LinkedAccountId == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!HasValidCsrfToken(httpContext, session))
        {
            return Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var effectiveMainAccountId = await accountLinkGateway.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            return Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var idempotencyKey = ReadIdempotencyKey(httpContext);
        var outcome = await accountLinkGateway.UnlinkAsync(
            options.ShardId, session.AccountId, request.LinkedAccountId, idempotencyKey, cancellationToken);

        if (!outcome.IsApproved)
        {
            return Results.Json(new { error = "unlink_rejected", reason = outcome.RejectionCode.ToString() }, statusCode: StatusCodes.Status409Conflict);
        }

        await ReselectDisplayCharacterAsync(
            options.ShardId, session.AccountId, CloudDisplayCharacterSelectionReason.RosterChanged,
            accountLinkGateway, characterIdentityReader, displayCharacterGateway, cancellationToken);

        return Results.Ok(new { approved = true });
    }

    /// <summary>
    /// AUTH-003: re-runs Display Character selection after the group's roster changed. Runs as its
    /// own transaction after the link/unlink itself already committed -- <c>CloudDisplayCharacterGateway.ReselectAsync</c>
    /// opens its own transaction and cannot be nested inside the link/unlink gateway's already-open
    /// one, and this pointer is a derived convenience projection, not a custody-authoritative fact,
    /// so a brief window where the link is committed but not yet reselected is acceptable (the same
    /// window the identity-outbox projection consumer already has for a bare rename/deletion).
    /// Failure here is deliberately swallowed rather than failing the whole link/unlink response: the
    /// link/unlink itself already committed and must not be reported as failed over a display-only
    /// side effect.
    /// </summary>
    private static async Task ReselectDisplayCharacterAsync(
        string shardId,
        uint mainAccountId,
        CloudDisplayCharacterSelectionReason reason,
        ICloudAccountLinkGateway accountLinkGateway,
        ICloudCharacterIdentityReader characterIdentityReader,
        ICloudDisplayCharacterGateway displayCharacterGateway,
        CancellationToken cancellationToken)
    {
        try
        {
            var groupId = await accountLinkGateway.TryGetOwnershipGroupIdAsync(shardId, mainAccountId, cancellationToken);
            if (groupId is null)
            {
                return;
            }

            var accountIds = await accountLinkGateway.GetOwnershipGroupAccountIdsAsync(shardId, mainAccountId, cancellationToken);
            var candidates = await characterIdentityReader.GetCandidatesAsync(shardId, accountIds, cancellationToken);
            await displayCharacterGateway.ReselectAsync(shardId, groupId.Value, candidates, reason, Guid.NewGuid(), cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately non-fatal: see this method's doc comment. A future issue that adds
            // structured background-job/error telemetry should log this instead of swallowing it
            // silently; no such sink exists yet in this endpoint's dependencies.
        }
    }

    private static async Task<uint?> TryVerifySourceCredentialsAsync(
        string sourceAccountName,
        string sourcePassword,
        ICloudWebSessionStore sessionStore,
        ICloudAuthBridgeClient authBridgeClient,
        CloudPrivateServiceKeyRing keyRing,
        CloudBackendOptions options,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var grantResult = await authBridgeClient.IssueGrantAsync(sourceAccountName, sourcePassword, CloudAuthAudiences.CloudBackend, cancellationToken);
        if (grantResult.Kind != CloudAuthBridgeGrantOutcomeKind.Issued)
        {
            return null;
        }

        var validation = CloudAuthGrantValidator.Validate(grantResult.Grant, CloudAuthAudiences.CloudBackend, nowUtc, keyRing);
        if (!validation.IsValid)
        {
            return null;
        }

        // Consumes the grant's nonce (the same replay defense login uses) without opening a session
        // cookie for it -- this call proves password control, it does not authenticate a browser as
        // the source account. The resulting orphaned session row is harmless and expires normally.
        var exchangeResult = await sessionStore.ExchangeGrantForSessionAsync(
            options.ShardId, validation.Grant!.AccountId, validation.Grant.Nonce,
            CloudWebSessionSecretHasher.Generate().Hash, CloudCsrfTokenGenerator.Generate(), nowUtc, TimeSpan.FromMinutes(1), cancellationToken);

        return exchangeResult.IsCreated ? validation.Grant.AccountId : null;
    }

    /// <summary>Security baseline "CSRF protection", matching <see cref="AuthSessionEndpoints"/>'s own logout check.</summary>
    private static bool HasValidCsrfToken(HttpContext httpContext, CloudWebSession session)
    {
        var submittedCsrfToken = httpContext.Request.Headers[AuthSessionEndpoints.CsrfHeaderName].ToString();
        return CloudCsrfTokenValidator.Matches(submittedCsrfToken, session.CsrfToken);
    }

    private static Guid ReadIdempotencyKey(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers[AccountEndpoints.IdempotencyKeyHeaderName].ToString();
        return Guid.TryParse(header, out var key) ? key : Guid.NewGuid();
    }

    private static async Task<CloudWebSession?> TryGetActiveSessionAsync(
        HttpContext httpContext, ICloudWebSessionStore sessionStore, CloudBackendOptions options, CancellationToken cancellationToken)
    {
        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        return await sessionStore.TryGetActiveSessionAsync(CloudWebSessionSecretHasher.Hash(secret), DateTime.UtcNow, cancellationToken);
    }
}
