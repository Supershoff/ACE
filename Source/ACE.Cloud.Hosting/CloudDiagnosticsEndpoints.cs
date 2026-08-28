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

            return report.IsFullyOperational
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
}
