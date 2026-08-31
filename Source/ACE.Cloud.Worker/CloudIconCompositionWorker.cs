using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Entity.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// Composes every deposited/backfilled item's icon and writes its resulting
/// <see cref="CloudInventoryItemPropertiesProjection.IconCacheKeyHex"/> (issue #34 human-acceptance
/// correction: "no runtime producer schedules deposited/backfilled item composition"). Mirrors
/// <see cref="CloudNotificationProjectionConsumerWorker"/>'s polling shape; without this hosted
/// service registered, every deposited item keeps showing the client's neutral fallback glyph
/// forever, since nothing else ever calls <see cref="CloudIconCompositionCache.GetOrComposeAsync"/>
/// at runtime.
///
/// A row whose composition resolves to <see cref="CloudIconCompositionOutcomeKind.Fallback"/> (a
/// missing/broken DAT reference) is retried on every subsequent poll rather than being marked
/// permanently failed -- acceptable for now since <see cref="CloudIconDiagnosticGateway"/> already
/// records and deduplicates the exact reason, giving an operator/admin something actionable; a
/// follow-up could add a backoff once that diagnostic volume is observed in practice.
/// </summary>
public sealed class CloudIconCompositionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly IProtectedAssetBlobStore _blobStore;
    private readonly ILogger<CloudIconCompositionWorker> _logger;

    public CloudIconCompositionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CloudWorkerOptions> options,
        IProtectedAssetBlobStore blobStore,
        ILogger<CloudIconCompositionWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.IconCompositionPollInterval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Icon composition poll failed; retrying on the next tick.");
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

        var activeManifest = await context.CloudActiveAssetManifests.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ShardId == shardId && m.Kind == CloudAssetKind.Portal, stoppingToken);
        if (activeManifest is null)
        {
            // No client_portal.dat has been staged/activated for this shard yet (ASSET-001/ASSET-002);
            // every item keeps its neutral fallback glyph until an operator activates one.
            return;
        }

        var pending = await context.CloudInventoryItemPropertiesProjections
            .Where(row => row.ShardId == shardId && row.IconCacheKeyHex == null)
            .OrderBy(row => row.BiotaId)
            .Take(_options.IconCompositionBatchSize)
            .ToListAsync(stoppingToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        var entries = await context.CloudAssetManifestEntryRecords.AsNoTracking()
            .Where(entry => entry.ManifestId == activeManifest.ManifestId)
            .ToListAsync(stoppingToken)
            .ConfigureAwait(false);

        var blobReader = CloudAssetManifestBlobReader.FromEntries(entries, _blobStore);
        var layerSource = new PortalDatIconLayerSource(blobReader);
        var clothingEffectResolver = new PortalDatIconClothingEffectResolver(blobReader);
        var compositionCache = new CloudIconCompositionCache(_blobStore);

        var propertiesGateway = new CloudInventoryItemPropertiesGateway(context);
        var iconInputsGateway = new CloudIconCompositionInputsGateway(context);
        var diagnosticGateway = new CloudIconDiagnosticGateway(context);

        var composed = 0;
        var fellBack = 0;
        var skippedNoInputs = 0;

        foreach (var row in pending)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var inputs = await iconInputsGateway.TryGetAsync(row.BiotaId, shardId, stoppingToken).ConfigureAwait(false);
            if (inputs is null)
            {
                // Deposited/backfilled before issue #34's capture landed; a later reapply/backfill
                // pass will populate this once ACE.Server catches it up.
                skippedNoInputs++;
                continue;
            }

            var entry = await compositionCache.GetOrComposeAsync(
                inputs, activeManifest.ManifestVersion, clothingEffectResolver, layerSource, stoppingToken).ConfigureAwait(false);

            if (entry.Outcome == CloudIconCompositionOutcomeKind.Composed)
            {
                await propertiesGateway.UpsertAsync(
                    row.BiotaId,
                    shardId,
                    row.Name,
                    (ItemType)row.ItemTypeFlags,
                    (WeenieType)row.WeenieType,
                    row.Value,
                    row.Burden,
                    entry.CacheKey.Hex,
                    revision: row.Revision + 1,
                    stoppingToken).ConfigureAwait(false);
                composed++;
            }
            else
            {
                fellBack++;
                foreach (var diagnostic in entry.Diagnostics)
                {
                    await diagnosticGateway.RecordAsync(shardId, diagnostic, DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
                }
            }
        }

        if (composed > 0 || fellBack > 0)
        {
            _logger.LogInformation(
                "Icon composition pass: composed {Composed}, fell back {FellBack}, skipped (no captured inputs yet) {SkippedNoInputs}.",
                composed, fellBack, skippedNoInputs);
        }
    }
}
