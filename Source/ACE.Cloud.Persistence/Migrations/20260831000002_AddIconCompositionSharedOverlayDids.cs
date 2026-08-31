namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #34's follow-up human-acceptance correction: <see cref="CloudIconCompositionInputsProjection"/>
/// left <c>ItemTypeBackgroundDid</c> and <c>UiEffectDids</c> off the table entirely, so the ACE-world-
/// boundary-resolved shared background and static UiEffect overlay DIDs never survived the round trip
/// to the runtime icon composition worker, even once ACE.Server started resolving them.
/// </summary>
public sealed class AddIconCompositionSharedOverlayDids : CloudSchemaMigrationStep
{
    public AddIconCompositionSharedOverlayDids()
        : base("20260831000002_AddIconCompositionSharedOverlayDids")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        ALTER TABLE CloudIconCompositionInputsProjection
            ADD COLUMN ItemTypeBackgroundDid INT UNSIGNED NULL,
            ADD COLUMN UiEffectDids VARCHAR(512) NOT NULL DEFAULT '';
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        """
        ALTER TABLE CloudIconCompositionInputsProjection
            DROP COLUMN ItemTypeBackgroundDid,
            DROP COLUMN UiEffectDids;
        """,
    ];
}
