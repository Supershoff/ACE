namespace ACE.Cloud.Hosting;

/// <summary>
/// Probes ACE's private world-boundary health endpoint over HTTP (ARCH-008). Any failure to reach
/// it -- connection refused, DNS failure, timeout, or a non-success status -- is reported as the same
/// <see cref="CloudStartupComponent.WorldBoundary"/> outcome: from a companion service's point of
/// view, ACE's world process being offline and its endpoint being merely unreachable look identical,
/// and both must degrade to "world-boundary operations unavailable" (ARCH-008) rather than throw.
/// </summary>
public sealed class HttpCloudWorldBoundaryHealthProbe : ICloudWorldBoundaryHealthProbe
{
    private readonly HttpClient _httpClient;
    private readonly CloudWorldBoundaryProbeOptions _options;

    public HttpCloudWorldBoundaryHealthProbe(HttpClient httpClient, CloudWorldBoundaryProbeOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CloudStartupCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = new CancellationTokenSource(_options.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var response = await _httpClient.GetAsync(_options.HealthEndpoint, linkedSource.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? CloudStartupCheckResult.Healthy(CloudStartupComponent.WorldBoundary)
                : CloudStartupCheckResult.Unhealthy(
                    CloudStartupComponent.WorldBoundary,
                    $"ACE world process is offline: the world-boundary endpoint responded with status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CloudStartupCheckResult.Unhealthy(
                CloudStartupComponent.WorldBoundary, "ACE world process is offline: the world-boundary endpoint did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            return CloudStartupCheckResult.Unhealthy(
                CloudStartupComponent.WorldBoundary, $"ACE world process is offline: {ex.Message}");
        }
    }
}
