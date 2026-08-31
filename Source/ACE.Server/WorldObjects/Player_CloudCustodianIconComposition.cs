using System;
using ACE.Cloud.Domain;
using ACE.Database.Models.Shard;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Captures <see cref="CloudIconCompositionInputs"/> from a live or reconstructed native
    /// <see cref="WorldObject"/> (issue #34 human-acceptance correction: "complete icon-composition
    /// inputs at the ACE world boundary"). Every field is a direct, already-typed WorldObject property
    /// read -- no property lookup tables or examiner/skill-check context is involved -- so, unlike
    /// <see cref="Player.BuildAppraisalSnapshot"/>, this can run against a biota-reconstructed
    /// WorldObject with no live session (see <c>CloudCustodianManager.BackfillInventoryPropertiesAsync</c>),
    /// not only at deposit time.
    ///
    /// <see cref="CloudIconCompositionInputs.ItemTypeBackgroundDid"/> and
    /// <see cref="CloudIconCompositionInputs.UiEffectDids"/> are left at their defaults (null / empty):
    /// the ItemType-&gt;background mapping and still-glow overlay resolution are explicitly a
    /// different, not-yet-owned issue's scope per <see cref="CloudIconCompositionInputs"/>'s own doc
    /// comment, not something this correction fabricates.
    /// </summary>
    partial class Player
    {
        internal static CloudIconCompositionInputs BuildIconCompositionInputs(WorldObject item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new CloudIconCompositionInputs
            {
                BaseIconDid = item.IconId == 0 ? null : item.IconId,
                ClothingBaseDid = item.ClothingBase,
                SetupTableId = item.SetupTableId,
                PaletteTemplate = item.PaletteTemplate,
                Shade = item.Shade.HasValue ? (float)item.Shade.Value : null,
                IgnoreCloIcons = item.IgnoreCloIcons ?? false,
                UnderlayDid = item.IconUnderlayId,
                OverlayDid = item.IconOverlayId,
                OverlaySecondaryDid = item.IconOverlaySecondary,
            };
        }

        /// <summary>
        /// The startup/reapply backfill overload (<c>CloudCustodianManager.BackfillInventoryPropertiesAsync</c>):
        /// reads the same properties directly from a retained native <see cref="ACE.Database.Models.Shard.Biota"/>
        /// row rather than a live <see cref="WorldObject"/>, since backfill has no live session to
        /// reconstruct one against. <see cref="ACE.Database.Models.Shard.Biota"/>'s own
        /// <c>GetProperty</c> extensions require no lock (unlike the live, thread-shared
        /// <c>WorldObject</c>/<c>ACE.Entity.Models.Biota</c> equivalents), matching how the rest of
        /// <c>BackfillInventoryPropertiesAsync</c> already reads this same retained row.
        /// </summary>
        internal static CloudIconCompositionInputs BuildIconCompositionInputs(ACE.Database.Models.Shard.Biota biota)
        {
            ArgumentNullException.ThrowIfNull(biota);

            var iconId = biota.GetProperty(PropertyDataId.Icon);

            return new CloudIconCompositionInputs
            {
                BaseIconDid = iconId is null or 0 ? null : iconId,
                ClothingBaseDid = biota.GetProperty(PropertyDataId.ClothingBase),
                SetupTableId = biota.GetProperty(PropertyDataId.Setup) ?? 0,
                PaletteTemplate = biota.GetProperty(PropertyInt.PaletteTemplate),
                Shade = biota.GetProperty(PropertyFloat.Shade) is double shade ? (float)shade : null,
                IgnoreCloIcons = biota.GetProperty(PropertyBool.IgnoreCloIcons) ?? false,
                UnderlayDid = biota.GetProperty(PropertyDataId.IconUnderlay),
                OverlayDid = biota.GetProperty(PropertyDataId.IconOverlay),
                OverlaySecondaryDid = biota.GetProperty(PropertyDataId.IconOverlaySecondary),
            };
        }
    }
}
