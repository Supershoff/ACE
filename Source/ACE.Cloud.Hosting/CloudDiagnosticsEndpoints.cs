using ACE.Cloud.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Hosting;

/// <summary>
/// The explicit health/version handshake OPS-002 and issue #18's acceptance criteria require every
/// companion host to expose: a bare liveness probe, a startup-diagnostics readiness probe that
/// identifies the precise unhealthy component, and this component's own versions. Shared here so
/// Backend, Auth Bridge, and Worker wire identical endpoints instead of duplicating them.
/// </summary>
public static class CloudDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapCloudDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints, CloudComponentVersions selfVersion)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(selfVersion);

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        endpoints.MapGet("/health/ready", async (CloudStartupDiagnosticsService diagnostics, CancellationToken cancellationToken) =>
        {
            var report = await diagnostics.EvaluateAsync(cancellationToken).ConfigureAwait(false);
            var body = new
            {
                mode = report.Mode.ToString(),
                results = report.Results.Select(result => new
                {
                    component = result.Component.ToString(),
                    healthy = result.IsHealthy,
                    reason = result.Reason,
                }),
            };

            return IsRoutable(report.Mode)
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        endpoints.MapGet("/version", () => Results.Ok(new
        {
            aceExtensionVersion = selfVersion.AceExtensionVersion,
            cloudSchemaVersion = selfVersion.CloudSchemaVersion,
            contractProtocolVersion = selfVersion.ContractProtocolVersion,
        }));

        return endpoints;
    }

    /// <summary>
    /// ARCH-008: the ACE world process being offline must leave this service routable for login and
    /// every off-world operation -- only Withdrawal Token creation/redemption and deposits are
    /// unavailable. A readiness probe that returns a generic 503 for WorldBoundaryUnavailable would
    /// pull a healthy Backend/Auth Bridge out of a load balancer's rotation entirely, silently failing
    /// login and every other off-world request too. Operational and WorldBoundaryUnavailable
    /// therefore both route; only ReadOnly (the database itself is unavailable, ARCH-009) and
    /// VersionIncompatible genuinely cannot serve any request and report unready. Extracted as a pure
    /// function so its exact routing decision is unit-testable without hosting an HTTP endpoint.
    /// </summary>
    public static bool IsRoutable(CloudServiceAvailabilityMode mode) =>
        mode is CloudServiceAvailabilityMode.Operational or CloudServiceAvailabilityMode.WorldBoundaryUnavailable;
}
