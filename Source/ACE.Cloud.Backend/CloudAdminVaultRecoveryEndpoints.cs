using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

public sealed record CloudRecoverVaultRequestBody(uint DestinationAccountId, string? Reason, bool Confirm);

/// <summary>
/// Issue #38's audited administrator Allegiance Vault recovery surface (VAULT-005, ADM-002): the
/// manual recovery command/UI contract CONTEXT.md requires for a monarch character deleted
/// out-of-band while their Allegiance Vault was nonempty. Every route revalidates ACE administrator
/// authority fresh for this exact request (ADM-001) -- never a cached session claim -- and the
/// recovery mutation additionally enforces the same origin/CSRF discipline every other Cloud mutation
/// endpoint uses. There is deliberately no candidate-destination suggestion anywhere in this surface:
/// the administrator always supplies an explicit <see cref="CloudRecoverVaultRequestBody.DestinationAccountId"/>,
/// so this never guesses a successor regardless of how many other characters or accounts might look
/// like a plausible destination (VAULT-005).
/// </summary>
public static class CloudAdminVaultRecoveryEndpoints
{
    public static IEndpointRouteBuilder MapCloudAdminVaultRecoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/vault-recovery", HandleListUnresolvedAsync);
        endpoints.MapPost("/admin/vault-recovery/{diagnosticId}/recover", HandleRecoverAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListUnresolvedAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAuthBridgeClient authBridgeClient,
        ICloudMonarchVaultRecoveryDiagnosticReader diagnosticReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var (admin, adminError) = await CloudEndpointSessionHelpers.TryRequireAdminSessionAsync(
            httpContext, sessionStore, authBridgeClient, options, cancellationToken);
        if (adminError is not null)
        {
            return adminError;
        }

        var diagnostics = await diagnosticReader.GetUnresolvedAsync(options.ShardId, cancellationToken);

        return Results.Ok(new
        {
            diagnostics = diagnostics.Select(diagnostic => new
            {
                id = diagnostic.Id,
                monarchCharacterId = diagnostic.MonarchCharacterId,
                vaultOwnerId = diagnostic.VaultOwnerId,
                reason = diagnostic.Reason,
                detectedAtUtc = diagnostic.DetectedAtUtc,
            }),
        });
    }

    private static async Task<IResult> HandleRecoverAsync(
        HttpContext httpContext,
        Guid diagnosticId,
        CloudRecoverVaultRequestBody request,
        ICloudWebSessionStore sessionStore,
        ICloudAuthBridgeClient authBridgeClient,
        ICloudMonarchVaultRecoveryService recoveryService,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var originCheck = CloudRequestOriginPolicy.Evaluate(httpContext.Request.Headers.Origin, options.AllowedOrigins);
        if (!originCheck.IsAllowed)
        {
            return Results.Json(new { error = "origin_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var (admin, adminError) = await CloudEndpointSessionHelpers.TryRequireAdminSessionAsync(
            httpContext, sessionStore, authBridgeClient, options, cancellationToken);
        if (adminError is not null)
        {
            return adminError;
        }

        if (!CloudEndpointSessionHelpers.CsrfMatches(httpContext, admin!.Session))
        {
            return Results.Json(new { error = "csrf_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (diagnosticId == Guid.Empty || request.DestinationAccountId == 0)
        {
            return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
        }

        // A fresh Auth Bridge existence check for the administrator-typed destination (ADM-001's own
        // discipline applied to the destination, not just the caller): a resolved diagnostic can
        // never be re-applied, so a typo here must be refused now rather than permanently stranding
        // the vault's contents on an owner identity with no real account behind it.
        var destinationAccountExists = await authBridgeClient.GetFreshAccessLevelAsync(request.DestinationAccountId, cancellationToken) is not null;

        var outcome = await recoveryService.RecoverAsync(
            options.ShardId, diagnosticId, admin.Session.AccountId, request.DestinationAccountId, destinationAccountExists,
            request.Reason, request.Confirm, cancellationToken);

        return outcome.Kind switch
        {
            CloudBoundaryOutcomeKind.Committed => Results.Ok(new
            {
                diagnosticId = outcome.Value!.DiagnosticId,
                destinationOwnerId = outcome.Value.DestinationOwnerId,
                custodyRecordsMoved = outcome.Value.CustodyRecordsMoved,
                stackLotsMoved = outcome.Value.StackLotsMoved,
            }),
            CloudBoundaryOutcomeKind.Conflict => Results.Json(new { error = "conflict", reason = outcome.Reason }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "unavailable", reason = outcome.Reason }, statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
