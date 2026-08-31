using System.Collections.Generic;

namespace ACE.Common
{
    /// <summary>
    /// Operator-supplied Item-Type-derived background and static UiEffect overlay DIDs (issue #34
    /// human-acceptance correction / issue #24: "Select the shared background DID from the item's
    /// ItemType; it is not an item icon property"). Every DID here still names a resource in the
    /// operator's own active <c>client_portal.dat</c> -- ACE.Common does not fabricate, guess, or
    /// hard-code any specific item's mapping (issue #24: "Process the server operator's configured
    /// DAT rather than hard-coding the standard DAT contents"); an unconfigured category or effect
    /// simply composes without that layer instead of guessing a value.
    /// </summary>
    public class CloudMuleIconOverlayConfiguration
    {
        /// <summary>
        /// Keyed by <c>ACE.Cloud.Domain.CloudInventoryCategory</c>'s enum member name (for example
        /// <c>"MeleeWeapons"</c>), the same deterministic ItemType-derived grouping the Mule Page grid
        /// already uses (CONTEXT.md's "Inventory Category"), so this correction does not invent a
        /// second, competing ItemType priority order.
        /// </summary>
        public Dictionary<string, uint> ItemTypeBackgroundDidsByCategory { get; set; } = new Dictionary<string, uint>();

        /// <summary>
        /// Keyed by <c>ACE.Entity.Enum.UiEffects</c>'s enum member name (for example <c>"Magical"</c>).
        /// </summary>
        public Dictionary<string, uint> UiEffectOverlayDidsByEffect { get; set; } = new Dictionary<string, uint>();
    }
}
