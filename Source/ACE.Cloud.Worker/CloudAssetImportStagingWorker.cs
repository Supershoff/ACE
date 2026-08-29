using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACE.Cloud.Worker;

/// <summary>
/// Polls for queued Asset Import sessions and drives them through extraction (ASSET-002's
/// "background staging/extraction jobs"). A crash mid-extraction simply leaves the session in
/// <see cref="CloudAssetImportSessionState.Staging"/>; the next poll tick picks it up again and
/// re-runs the (idempotent) extraction from scratch, matching the Red test "worker crash" -- there
/// is deliberately no lease/claim protocol here (see <see cref="CloudAssetImportBoundary.TryDequeueNextStagingSessionAsync"/>'s
/// doc comment for why that is safe for this deployment shape).
/// </summary>
public sealed class CloudAssetImportStagingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudWorkerOptions _options;
    private readonly ILogger<CloudAssetImportStagingWorker> _logger;

    private static readonly CloudAssetKind[] Kinds = [CloudAssetKind.Portal, CloudAssetKind.HighRes];

    public CloudAssetImportStagingWorker(IServiceScopeFactory scopeFactory, IOptions<CloudWorkerOptions> options, ILogger<CloudAssetImportStagingWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.AssetImportPollInterval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Asset Import staging poll failed; retrying on the next tick.");
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

        var boundary = scope.ServiceProvider.GetRequiredService<CloudAssetImportBoundary>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPortalDatAssetExtractor>();
        var storageOptions = scope.ServiceProvider.GetRequiredService<CloudAssetStorageOptions>();
        var blobStore = scope.ServiceProvider.GetRequiredService<IProtectedAssetBlobStore>();

        foreach (var kind in Kinds)
        {
            await ProcessNextQueuedSessionAsync(shardId, kind, boundary, extractor, blobStore, storageOptions, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessNextQueuedSessionAsync(
        string shardId,
        CloudAssetKind kind,
        CloudAssetImportBoundary boundary,
        IPortalDatAssetExtractor extractor,
        IProtectedAssetBlobStore blobStore,
        CloudAssetStorageOptions storageOptions,
        CancellationToken stoppingToken)
    {
        var queued = await boundary.TryDequeueNextStagingSessionAsync(shardId, kind, stoppingToken);
        if (queued is null)
        {
            return;
        }

        var sourceRelativePath = CloudAssetStagingPathPolicy.BuildRetainedSourceRelativePath(shardId, kind);
        var sourceAbsolutePath = Path.Combine(storageOptions.RootDirectory, sourceRelativePath);
        var manifestId = Guid.NewGuid();

        try
        {
            var entries = await extractor.ExtractAsync(sourceAbsolutePath, manifestId, blobStore, stoppingToken);
            var outcome = await boundary.CompleteStagingAsync(queued.Id, manifestId, entries, stoppingToken);

            if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
            {
                _logger.LogWarning("Asset Import session {SessionId} finished extraction but could not complete staging: {Reason}", queued.Id, outcome.Reason);
            }
            else
            {
                _logger.LogInformation(
                    "Asset Import session {SessionId} staged manifest version {Version} with {EntryCount} entries.",
                    queued.Id, outcome.Value!.Version, outcome.Value.EntryCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Asset Import session {SessionId} failed extraction.", queued.Id);
            await boundary.FailStagingAsync(queued.Id, DescribeExtractionFailure(ex), stoppingToken);
        }
    }

    /// <summary>
    /// Maps an extraction exception to a bounded, path-free reason safe to commit to the Activity
    /// Ledger. <c>ex.Message</c> must never be persisted directly here: <c>ACE.DatLoader</c>'s
    /// <see cref="FileNotFoundException"/>(string) constructor (and similar I/O exceptions) puts the
    /// absolute operator storage path verbatim into <c>.Message</c>, which would otherwise violate
    /// issue #25's "no absolute operator path is committed" acceptance criterion. The full exception,
    /// including its message, is still logged server-side via <see cref="ILogger.LogError"/> above --
    /// that structured log is an operator-only surface distinct from the Activity Ledger.
    /// </summary>
    internal static string DescribeExtractionFailure(Exception ex) => ex switch
    {
        FileNotFoundException => "Source DAT is missing or unreadable.",
        IOException => "Source DAT could not be read due to an I/O error.",
        InvalidDataException => "Source DAT is malformed.",
        _ => "Extraction failed; see worker logs for details.",
    };
}
