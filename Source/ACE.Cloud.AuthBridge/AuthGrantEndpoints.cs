using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.AuthBridge;

/// <summary>
/// The Auth Bridge's own private, internal-only endpoints (AUTH-002, ADM-001): issuing a signed
/// grant after verifying Main Account credentials, and revalidating an account's current access
/// level fresh from <c>ace_auth.account</c> for the Cloud backend's admin-request gate. Both require
/// a valid <see cref="CloudPrivateServiceRequestAuthenticator"/> signature -- only the Cloud
/// backend, not a browser, is an intended caller (security baseline: "Do not expose these endpoints
/// publicly").
/// </summary>
public static class AuthGrantEndpoints
{
    private static readonly string[] AllowedAudiences = [CloudAuthAudiences.CloudBackend];

    public static IEndpointRouteBuilder MapAuthGrantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/auth/grants", HandleIssueGrantAsync);
        endpoints.MapGet("/internal/auth/access-level/{accountId}", HandleGetAccessLevelAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleIssueGrantAsync(
        HttpContext httpContext,
        IssueGrantRequest request,
        IAceAuthAccountReader accountReader,
        CloudLoginAttemptRateLimiter rateLimiter,
        CloudPrivateServiceKeyRing keyRing,
        AuthBridgeOptions options,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        if (!IsAuthenticPrivateServiceRequest(httpContext, "POST", "/internal/auth/grants", nowUtc, keyRing, options))
        {
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.AccountName)
            || string.IsNullOrWhiteSpace(request.Password)
            || !AllowedAudiences.Contains(request.Audience, StringComparer.Ordinal))
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // Keyed by account name (case-folded to match ACE's own lowercase-on-create convention),
        // not by caller IP: this endpoint's only intended caller is the Cloud backend over a
        // private network, so every request shares roughly the same source; per-account limiting is
        // what actually slows a credential-stuffing attempt against one target account. The public
        // browser-facing endpoint that calls this one applies its own IP-scoped limit.
        var rateLimitResult = rateLimiter.RegisterAttempt(request.AccountName.ToLowerInvariant(), nowUtc);
        if (!rateLimitResult.IsAllowed)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(rateLimitResult.RetryAfter!.Value.TotalSeconds)).ToString();
            return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var account = await accountReader.FindByAccountNameAsync(request.AccountName, cancellationToken);
        if (account is null)
        {
            logger.LogInformation("Auth Bridge grant request denied: no matching account.");
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var eligibility = CloudAccountLoginEligibilityPolicy.Evaluate(account, nowUtc);
        if (!eligibility.IsEligible)
        {
            logger.LogInformation("Auth Bridge grant request denied for account {AccountId}: account is banned.", account.AccountId);
            return Results.Json(new { error = "account_banned" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!CloudLegacyPasswordVerifier.Matches(account.PasswordHash, account.PasswordSalt, request.Password))
        {
            logger.LogInformation("Auth Bridge grant request denied for account {AccountId}: password mismatch.", account.AccountId);
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var timeToLive = TimeSpan.FromSeconds(options.GrantTimeToLiveSeconds);
        var grant = CloudAuthGrantIssuer.Issue(account.AccountId, request.Audience, nowUtc, timeToLive, keyRing);

        logger.LogInformation("Auth Bridge issued a grant for account {AccountId}.", account.AccountId);

        return Results.Ok(new IssueGrantResponse(grant, nowUtc + timeToLive, account.AccountId, account.AccessLevel));
    }

    private static async Task<IResult> HandleGetAccessLevelAsync(
        HttpContext httpContext,
        uint accountId,
        IAceAuthAccountReader accountReader,
        CloudPrivateServiceKeyRing keyRing,
        AuthBridgeOptions options,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var path = $"/internal/auth/access-level/{accountId}";

        if (!IsAuthenticPrivateServiceRequest(httpContext, "GET", path, nowUtc, keyRing, options))
        {
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (accountId == 0)
        {
            return Results.NotFound();
        }

        var account = await accountReader.FindByAccountIdAsync(accountId, cancellationToken);
        return account is null ? Results.NotFound() : Results.Ok(new AccessLevelResponse(account.AccountId, account.AccessLevel));
    }

    private static bool IsAuthenticPrivateServiceRequest(
        HttpContext httpContext, string method, string path, DateTime nowUtc, CloudPrivateServiceKeyRing keyRing, AuthBridgeOptions options)
    {
        var header = httpContext.Request.Headers[CloudPrivateServiceHeaders.SignatureHeaderName].ToString();
        var maxClockSkew = TimeSpan.FromSeconds(options.PrivateServiceRequestMaxClockSkewSeconds);

        return CloudPrivateServiceRequestAuthenticator.Validate(header, method, path, nowUtc, maxClockSkew, keyRing);
    }
}
