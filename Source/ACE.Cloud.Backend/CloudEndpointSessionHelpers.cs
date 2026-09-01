using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Http;

namespace ACE.Cloud.Backend;

public sealed record CloudAdminSessionResult(CloudWebSession Session, uint AccessLevel);

/// <summary>
/// The session-cookie lookup every authenticated Cloud backend endpoint needs, shared by the
/// account identity/linking and Withdrawal Token endpoints added in issue #33. Mirrors
/// <c>CloudInventoryEndpoints.TryGetActiveSessionAsync</c>'s own shape exactly; extracted here
/// rather than duplicated a third time.
/// </summary>
internal static class CloudEndpointSessionHelpers
{
    public static async Task<CloudWebSession?> TryGetActiveSessionAsync(
        HttpContext httpContext, ICloudWebSessionStore sessionStore, CloudBackendOptions options, CancellationToken cancellationToken)
    {
        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        return await sessionStore.TryGetActiveSessionAsync(CloudWebSessionSecretHasher.Hash(secret), DateTime.UtcNow, cancellationToken);
    }

    /// <summary>
    /// AUTH-004's server-side enforcement, matching <c>CloudInventoryEndpoints</c>'s own
    /// <c>TryResolveOwnMainAccountViewerAsync</c>: resolves the caller's session and refuses with
    /// <c>linked_account_restricted</c> unless it is itself a Main Account (never only relying on the
    /// web client's own route guard).
    /// </summary>
    public static async Task<(CloudWebSession? Session, IResult? Error)> TryRequireMainAccountSessionAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return (null, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            return (null, Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (session, null);
    }

    /// <summary>
    /// ADM-001's "revalidate on every sensitive request; session claims alone are insufficient",
    /// extracted here so every admin-only mutation endpoint (starting with issue #38's Allegiance
    /// Vault recovery) shares the exact same fresh-Auth-Bridge-read discipline
    /// <c>CloudActivityLedgerEndpoints</c> already established inline for its own admin-scoped read.
    /// </summary>
    public static async Task<(CloudAdminSessionResult? Result, IResult? Error)> TryRequireAdminSessionAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAuthBridgeClient authBridgeClient,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return (null, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var freshAccessLevel = await authBridgeClient.GetFreshAccessLevelAsync(session.AccountId, cancellationToken);
        if (freshAccessLevel is null || !CloudAdminAccessRevalidationPolicy.Evaluate(freshAccessLevel.Value).IsAuthorized)
        {
            return (null, Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (new CloudAdminSessionResult(session, freshAccessLevel.Value), null);
    }

    /// <summary>Matches <c>AuthSessionEndpoints.HandleLogoutAsync</c>'s own CSRF check for every state-changing request.</summary>
    public static bool CsrfMatches(HttpContext httpContext, CloudWebSession session)
    {
        var submittedCsrfToken = httpContext.Request.Headers[AuthSessionEndpoints.CsrfHeaderName].ToString();
        return CloudCsrfTokenValidator.Matches(submittedCsrfToken, session.CsrfToken);
    }
}
