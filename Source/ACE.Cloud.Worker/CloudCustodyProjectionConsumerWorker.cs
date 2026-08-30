using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// Polls the Custody Outbox and idempotently applies new events into
/// <see cref="CloudInventoryReadProjection"/> and the Live State Stream (ARCH-007, EVT-007). Follows
/// <see cref="CloudAssetImportStagingWorker"/>'s established shape: no lease/claim protocol (a single
/// active worker instance is this deployment's assumption), and every failure -- including the
/// database being unreachable, which <see cref="CloudCustodyProjectionConsumer.RunBatchAsync(string, int, CancellationToken)"/>
/// already reports as an explicit <see cref="CloudBoundaryOutcomeKind.Unavailable"/> result rather
/// than an exception -- simply waits for the next poll tick instead of crashing the process.
/// </summary>
public sealed class CloudCustodyProjectionConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly ILogger<CloudCustodyProjectionConsumerWorker> _logger;

    public CloudCustodyProjectionConsumerWorker(
        IServiceScopeFactory scopeFactory, IOptions<CloudWorkerOptions> options, ILogger<CloudCustodyProjectionConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ProjectionConsumerPollInterval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Custody projection consumer poll failed; retrying on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var shardId = await context.CloudShardBindings.AsNoTracking().Select(b => b.ShardId).SingleOrDefaultAsync(stoppingToken);
        if (shardId is null)
        {
            return;
        }

        var consumer = new CloudCustodyProjectionConsumer(context);
        var outcome = await consumer.RunBatchAsync(shardId, _options.ProjectionConsumerBatchSize, stoppingToken);

        switch (outcome.Kind)
        {
            case CloudBoundaryOutcomeKind.Committed when outcome.Value!.EventsRead > 0:
                _logger.LogInformation(
                    "Custody projection consumer applied {Applied}/{Read} events ({Skipped} stale, {DeadLettered} dead-lettered).",
                    outcome.Value.EventsApplied, outcome.Value.EventsRead, outcome.Value.EventsSkippedAsStale, outcome.Value.EventsDeadLettered);
                break;
            case CloudBoundaryOutcomeKind.Unavailable:
                _logger.LogWarning("Custody projection consumer poll skipped: {Reason}", outcome.Reason);
                break;
        }
    }
}
