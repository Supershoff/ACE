namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #34's human-acceptance correction schema addition: the rebuildable read-model caches ACE's
/// world-boundary deposit/backfill code writes into so a Full Cloud Appraisal panel can show the
/// complete in-game-style ID content (<see cref="CloudAppraisalSnapshotProjection"/>) and a runtime
/// icon composition worker can compose a missing/stale icon without direct ace_shard access
/// (<see cref="CloudIconCompositionInputsProjection"/>). Both mirror
/// <see cref="CloudInventoryItemPropertiesProjection"/>'s "fully disposable and rebuildable from
/// ACE's own biota properties" shape.
/// </summary>
public sealed class AddAppraisalSnapshotAndIconCompositionInputs : CloudSchemaMigrationStep
{
    public AddAppraisalSnapshotAndIconCompositionInputs()
        : base("20260831000001_AddAppraisalSnapshotAndIconCompositionInputs")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudAppraisalSnapshotProjection (
            BiotaId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SnapshotJson LONGTEXT NOT NULL,
            Revision BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (BiotaId),
            CONSTRAINT FK_CloudAppraisalSnapshot_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudIconCompositionInputsProjection (
            BiotaId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            BaseIconDid INT UNSIGNED NULL,
            ClothingBaseDid INT UNSIGNED NULL,
            SetupTableId INT UNSIGNED NOT NULL,
            PaletteTemplate INT NULL,
            Shade FLOAT NULL,
            IgnoreCloIcons TINYINT(1) NOT NULL,
            UnderlayDid INT UNSIGNED NULL,
            OverlayDid INT UNSIGNED NULL,
            OverlaySecondaryDid INT UNSIGNED NULL,
            Revision BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (BiotaId),
            CONSTRAINT FK_CloudIconCompositionInputs_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudIconCompositionInputsProjection;",
        "DROP TABLE CloudAppraisalSnapshotProjection;",
    ];
}
