namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Adds <c>LayerId</c> to <c>CloudFrozenEnchantment</c> (issue #15 review, P1): a Frozen
/// Enchantment's resume step must match the exact registry row it was captured from, and
/// <c>ace_shard.biota_properties_enchantment_registry</c>'s real per-spell identity is
/// (object_Id, spell_Id, layer_Id) -- <c>EnchantmentManager.Add</c> assigns successive LayerIds to
/// multiple layers of the same spell on the same object (e.g. independent DoTs from different
/// casters), a case <c>SpellId</c> alone cannot distinguish. Existing rows default to 0, matching
/// this repository's existing single-layer Frozen Enchantment rows and
/// <c>PropertiesEnchantmentRegistry</c>'s own default LayerId.
/// </summary>
public sealed class AddLayerIdToCloudFrozenEnchantment : CloudSchemaMigrationStep
{
    public AddLayerIdToCloudFrozenEnchantment()
        : base("20260828000004_AddLayerIdToCloudFrozenEnchantment")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        "ALTER TABLE CloudFrozenEnchantment ADD COLUMN LayerId SMALLINT UNSIGNED NOT NULL DEFAULT 0 AFTER SpellId;",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "ALTER TABLE CloudFrozenEnchantment DROP COLUMN LayerId;",
    ];
}
