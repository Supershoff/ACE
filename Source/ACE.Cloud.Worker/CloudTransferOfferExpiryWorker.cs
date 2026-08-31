using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// XFER-002's database-time expiry worker: polls for Pending Transfer Offers whose seven-day
/// deadline has passed and expires them (<see cref="CloudTransferOfferGateway.ExpireDueOffersAsync"/>),
/// releasing their reservations back to the sender. Mirrors
/// <see cref="CloudNotificationProjectionConsumerWorker"/>'s polling shape exactly -- without this
/// hosted service registered, a Pending offer's <see cref="CloudTransferOfferRecord.ExpiresAtUtc"/>
/// deadline passing has no effect until something else acts on it.
/// </summary>
public sealed class CloudTransferOfferExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly ILogger<CloudTransferOfferExpiryWorker> _logger;

    public CloudTransferOfferExpiryWorker(
        IServiceScopeFactory scopeFactory, IOptions<CloudWorkerOptions> options, ILogger<CloudTransferOfferExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.TransferOfferExpiryPollInterval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Transfer Offer expiry poll failed; retrying on the next tick.");
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

        var ownershipResolver = scope.ServiceProvider.GetRequiredService<ICloudAccountOwnershipResolver>();
        var gateway = new CloudTransferOfferGateway(context, ownershipResolver);

        var expiredCount = await gateway.ExpireDueOffersAsync(shardId, _options.TransferOfferExpiryBatchSize, stoppingToken);
        if (expiredCount > 0)
        {
            _logger.LogInformation("Expired {ExpiredCount} Transfer Offer(s) past their seven-day deadline.", expiredCount);
        }
    }
}
