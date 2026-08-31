using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// Polls the Custody Outbox and idempotently applies new events into the Notification Center
/// (<see cref="CloudNotificationProjectionConsumer"/>) and the Live State Stream (EVT-003, EVT-007).
/// Mirrors <see cref="CloudCustodyProjectionConsumerWorker"/>'s polling shape exactly -- without this
/// hosted service registered, <see cref="CloudNotificationProjectionConsumer.RunBatchAsync(string, int, System.Threading.CancellationToken)"/>
/// is never invoked in production and no notification is ever created.
/// </summary>
public sealed class CloudNotificationProjectionConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly ILogger<CloudNotificationProjectionConsumerWorker> _logger;

    public CloudNotificationProjectionConsumerWorker(
        IServiceScopeFactory scopeFactory, IOptions<CloudWorkerOptions> options, ILogger<CloudNotificationProjectionConsumerWorker> logger)
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
                _logger.LogWarning(ex, "Notification projection consumer poll failed; retrying on the next tick.");
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

        var consumer = new CloudNotificationProjectionConsumer(context);
        var outcome = await consumer.RunBatchAsync(shardId, _options.ProjectionConsumerBatchSize, stoppingToken);

        switch (outcome.Kind)
        {
            case CloudBoundaryOutcomeKind.Committed when outcome.Value!.EventsRead > 0:
                _logger.LogInformation(
                    "Notification projection consumer applied {Applied}/{Read} events ({Skipped} stale, {DeadLettered} dead-lettered).",
                    outcome.Value.EventsApplied, outcome.Value.EventsRead, outcome.Value.EventsSkippedAsStale, outcome.Value.EventsDeadLettered);
                break;
            case CloudBoundaryOutcomeKind.Unavailable:
                _logger.LogWarning("Notification projection consumer poll skipped: {Reason}", outcome.Reason);
                break;
        }
    }
}
