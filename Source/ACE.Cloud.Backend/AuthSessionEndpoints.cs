using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// The Cloud backend's browser-facing authentication endpoints (AUTH-002, ADM-001): login (which
/// calls the private Auth Bridge and exchanges the grant it returns for a secure session), logout,
/// and a demonstration admin-gated endpoint that revalidates ACE access level 5 fresh on every
/// call.
/// </summary>
public static class AuthSessionEndpoints
{
    public const string CsrfHeaderName = "X-Csrf-Token";

    public static IEndpointRouteBuilder MapAuthSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", HandleLoginAsync);
        endpoints.MapPost("/auth/logout", HandleLogoutAsync);
        endpoints.MapGet("/admin/whoami", HandleAdminWhoAmIAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpContext httpContext,
        LoginRequest request,
        ICloudAuthBridgeClient authBridgeClient,
        ICloudWebSessionStore sessionStore,
        CloudLoginAttemptRateLimiter rateLimiter,
        CloudPrivateServiceKeyRing keyRing,
        CloudBackendOptions options,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.AccountName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // Keyed by source IP, not account name: this is the public browser-facing endpoint, so
        // callers genuinely come from many different addresses. The Auth Bridge separately applies
        // its own account-name-scoped limit for calls it receives.
        var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitResult = rateLimiter.RegisterAttempt(clientKey, nowUtc);
        if (!rateLimitResult.IsAllowed)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(rateLimitResult.RetryAfter!.Value.TotalSeconds)).ToString();
            return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var grantResult = await authBridgeClient.IssueGrantAsync(request.AccountName, request.Password, CloudAuthAudiences.CloudBackend, cancellationToken);

        if (grantResult.Kind == CloudAuthBridgeGrantOutcomeKind.Unavailable)
        {
            return Results.Json(new { error = "authentication_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (grantResult.Kind != CloudAuthBridgeGrantOutcomeKind.Issued)
        {
            // Never distinguish "unknown account" / "wrong password" / "banned" to the browser --
            // that would let an attacker enumerate account names.
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var validation = CloudAuthGrantValidator.Validate(grantResult.Grant, CloudAuthAudiences.CloudBackend, nowUtc, keyRing);
        if (!validation.IsValid)
        {
            logger.LogWarning("Auth Bridge issued a grant that failed independent backend validation: {Kind}.", validation.Kind);
            return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var secret = CloudWebSessionSecretHasher.Generate();
        var csrfToken = CloudCsrfTokenGenerator.Generate();
        var sessionTimeToLive = TimeSpan.FromMinutes(options.SessionTimeToLiveMinutes);

        var exchangeResult = await sessionStore.ExchangeGrantForSessionAsync(
            options.ShardId, validation.Grant!.AccountId, validation.Grant.Nonce, secret.Hash, csrfToken, nowUtc, sessionTimeToLive, cancellationToken);

        if (!exchangeResult.IsCreated)
        {
            logger.LogWarning("Rejected a replayed Auth Bridge grant for account {AccountId}.", validation.Grant.AccountId);
            return Results.Json(new { error = "grant_already_used" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        httpContext.Response.Cookies.Append(options.SessionCookieName, secret.Secret, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = new DateTimeOffset(nowUtc + sessionTimeToLive, TimeSpan.Zero),
        });

        logger.LogInformation("Web session created for account {AccountId}.", validation.Grant.AccountId);

        return Results.Ok(new LoginResponse(csrfToken));
    }

    private static async Task<IResult> HandleLogoutAsync(
        HttpContext httpContext, ICloudWebSessionStore sessionStore, CloudBackendOptions options, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (!string.IsNullOrEmpty(secret))
        {
            var secretHash = CloudWebSessionSecretHasher.Hash(secret);
            var session = await sessionStore.TryGetActiveSessionAsync(secretHash, nowUtc, cancellationToken);

            if (session is not null)
            {
                var submittedCsrfToken = httpContext.Request.Headers[CsrfHeaderName].ToString();
                if (!CloudCsrfTokenValidator.Matches(submittedCsrfToken, session.CsrfToken))
                {
                    return Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden);
                }
            }

            await sessionStore.RevokeSessionAsync(secretHash, nowUtc, cancellationToken);
        }

        httpContext.Response.Cookies.Delete(options.SessionCookieName);
        return Results.Ok();
    }

    private static async Task<IResult> HandleAdminWhoAmIAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAuthBridgeClient authBridgeClient,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (string.IsNullOrEmpty(secret))
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await sessionStore.TryGetActiveSessionAsync(CloudWebSessionSecretHasher.Hash(secret), nowUtc, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // ADM-001: "Revalidate on every sensitive request; session claims alone are insufficient" --
        // this session record carries no access-level claim at all; the only source of truth is a
        // fresh Auth Bridge read of ace_auth.account for this exact request.
        var freshAccessLevel = await authBridgeClient.GetFreshAccessLevelAsync(session.AccountId, cancellationToken);
        var revalidation = freshAccessLevel is null
            ? CloudAdminAccessRevalidationResult.Denied()
            : CloudAdminAccessRevalidationPolicy.Evaluate(freshAccessLevel.Value);

        if (!revalidation.IsAuthorized)
        {
            return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new { accountId = session.AccountId, accessLevel = freshAccessLevel });
    }
}
