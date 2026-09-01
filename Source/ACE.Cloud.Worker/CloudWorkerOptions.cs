namespace ACE.Cloud.Worker;

/// <summary>
/// This deployment's configuration, bound from the "CloudWorker" configuration section.
/// <see cref="CloudConnectionString"/> must authenticate as the same narrowly privileged companion
/// identity ARCH-004 requires (full access to <c>ace_cloud</c>, none to native biota tables).
/// </summary>
public sealed class CloudWorkerOptions
{
    public const string SectionName = "CloudWorker";

    public required string CloudConnectionString { get; init; }

    public required string ExpectedAceExtensionVersion { get; init; }

    public required string ExpectedContractProtocolVersion { get; init; }

    public required Uri WorldBoundaryHealthEndpoint { get; init; }

    public string DatabaseServerVersion { get; init; } = "11.4.2-mariadb";

    public TimeSpan DiagnosticsInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The absolute path under which Asset Import protected storage lives (ASSET-002). Must match
    /// the Backend's own <c>CloudAssetStorage:RootDirectory</c> setting -- both processes read and
    /// write the same protected files.
    /// </summary>
    public required string AssetStorageRootDirectory { get; init; }

    public long AssetStorageMaxTotalBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    public int AssetStorageMaxChunkSizeBytes { get; init; } = 32 * 1024 * 1024;

    public TimeSpan AssetImportPollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often the Custody/Identity Outbox projection consumers poll for new events (ARCH-007).</summary>
    public TimeSpan ProjectionConsumerPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The maximum number of outbox events a projection consumer applies per poll tick.</summary>
    public int ProjectionConsumerBatchSize { get; init; } = 200;

    /// <summary>How often the icon composition worker polls for items missing a composed icon (issue #34).</summary>
    public TimeSpan IconCompositionPollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The maximum number of items the icon composition worker composes per poll tick.</summary>
    public int IconCompositionBatchSize { get; init; } = 50;

    /// <summary>How often the Transfer Offer expiry worker polls for offers past their seven-day deadline (issue #35, XFER-002).</summary>
    public TimeSpan TransferOfferExpiryPollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The maximum number of Transfer Offers the expiry worker expires per poll tick.</summary>
    public int TransferOfferExpiryBatchSize { get; init; } = 200;
}
