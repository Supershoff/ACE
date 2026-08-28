using System;
using System.Collections.Generic;

using ACE.Cloud.Domain;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// A shared zero-payout Cloud Custodian vendor NPC (DEP-001, DEP-007, DEP-008, ADM-003): one
    /// instance occupies exactly one enabled Custodian Location, is never player-specific, and never
    /// creates a Pyreal payout or ordinary vendor resale inventory. Every instance is spawned and
    /// despawned exclusively by <see cref="CloudCustodianManager"/> on the world thread; nothing else
    /// constructs one.
    ///
    /// Real deposit-into-Cloud-custody routing (DEP-002, DEP-003, eligibility, ledger/outbox) is a
    /// dedicated handler added by a later issue that replaces <see cref="Vendor.ProcessItemsForPurchase"/>
    /// (see AC Cloud Mule issue #13). Until that handler exists, this class deliberately keeps every
    /// sold item intact -- neither destroyed nor resold -- rather than routing it through ordinary
    /// Vendor commerce, which would either destroy the item outright or silently resell it to other
    /// players; both are unacceptable for an item a player believes they are depositing off-world.
    /// </summary>
    public class CloudCustodian : Vendor
    {
        /// <summary>The stable identity of the Custodian Location this instance currently occupies.</summary>
        public CloudCustodianLocationKey LocationKey { get; private set; }

        /// <summary>The Custodian configuration version in effect when this instance was (re)spawned (DEP-008).</summary>
        public CloudAggregateVersion SpawnedAtConfigVersion { get; private set; }

        public CloudCustodian(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            ConfigureCloudCustodianDefaults();
        }

        public CloudCustodian(Biota biota) : base(biota)
        {
            ConfigureCloudCustodianDefaults();
        }

        /// <summary>
        /// Set once by <see cref="CloudCustodianManager"/> immediately after construction, before
        /// this instance enters the world, so <see cref="ValidateSaleCommit"/> always has a location
        /// and spawn-time version to revalidate against.
        /// </summary>
        public void AssignLocation(CloudCustodianLocationKey locationKey, CloudAggregateVersion spawnedAtConfigVersion)
        {
            LocationKey = locationKey ?? throw new ArgumentNullException(nameof(locationKey));
            SpawnedAtConfigVersion = spawnedAtConfigVersion ?? throw new ArgumentNullException(nameof(spawnedAtConfigVersion));
        }

        private void ConfigureCloudCustodianDefaults()
        {
            Name = "Cloud Custodian";

            // Nothing is offered for sale (DEP-001: never a personal vendor with resale inventory)
            // and, until issue #13's dedicated deposit handler exists, nothing is accepted for sale
            // either: MerchandiseItemTypes = 0 makes Player_Commerce.VerifySellItems' existing
            // `(acceptedItemTypes & wo.ItemType) == 0` check reject every row through ACE's own,
            // already-tested vendor validation, before ProcessItemsForPurchase below is ever reached
            // in ordinary play. These are all still non-null so ValidateVendorRequirements() passes
            // and the vendor window opens normally.
            MerchandiseItemTypes = 0;
            MerchandiseMinValue = 0;
            MerchandiseMaxValue = 0;
            BuyPrice = 0;
            SellPrice = 0;
        }

        /// <inheritdoc />
        public override bool ValidateSaleCommit(Player player, out string rejectionMessage)
        {
            var validation = CloudCustodianManager.ValidateSaleWindow(this);

            if (!validation.IsCurrent)
            {
                rejectionMessage = validation.StaleReason;
                return false;
            }

            rejectionMessage = null;
            return true;
        }

        /// <summary>
        /// Zero-payout (DEP-001: "It never creates payout currency"), regardless of what items were
        /// submitted.
        /// </summary>
        public override int CalculatePayoutCoinAmount(Dictionary<uint, WorldObject> items) => 0;

        /// <summary>
        /// Never resells (no ordinary vendor resale inventory) and never destroys a sold item -- see
        /// this class's doc comment for why real custody deposit is deliberately not implemented
        /// here yet.
        /// </summary>
        public override void ProcessItemsForPurchase(Player player, Dictionary<uint, WorldObject> items)
        {
            foreach (var item in items.Values)
            {
                item.ContainerId = Guid.Full;
                item.CalculateObjDesc();
            }

            ApproachVendor(player, VendorType.Sell);
        }
    }
}
