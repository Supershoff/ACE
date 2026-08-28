using ACE.Cloud.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// Periodically evaluates the same <see cref="CloudStartupDiagnosticsService"/> checks the Backend
/// exposes over HTTP, but for a process with no inbound web surface of its own (OPS-002: "Expose
/// health/version diagnostics"). Later Custody Outbox consumption work should gate its own mutations
/// on the most recently observed <see cref="CloudStartupDiagnosticsReport.Mode"/> rather than
/// attempting work this scaffold has already found unavailable. Creates a fresh DI scope per
/// evaluation, the same way ASP.NET Core scopes one per HTTP request, because
/// <see cref="CloudStartupDiagnosticsService"/> resolves scoped dependencies (<c>CloudDbContext</c>
/// via <c>CloudGatewayDiagnostics</c>) and this hosted service itself lives for the whole process.
/// </summary>
public sealed class CloudWorkerDiagnosticsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly ILogger<CloudWorkerDiagnosticsHostedService> _logger;

    public CloudWorkerDiagnosticsHostedService(
        IServiceScopeFactory scopeFactory, IOptions<CloudWorkerOptions> options, ILogger<CloudWorkerDiagnosticsHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.DiagnosticsInterval);

        do
        {
            using var scope = _scopeFactory.CreateScope();
            var diagnostics = scope.ServiceProvider.GetRequiredService<CloudStartupDiagnosticsService>();
            var report = await diagnostics.EvaluateAsync(stoppingToken).ConfigureAwait(false);

            if (report.IsFullyOperational)
            {
                _logger.LogInformation("Cloud worker startup diagnostics: {Mode}.", report.Mode);
            }
            else
            {
                var failure = report.Results[^1];
                _logger.LogWarning(
                    "Cloud worker startup diagnostics: {Mode} ({Component}: {Reason}).", report.Mode, failure.Component, failure.Reason);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
