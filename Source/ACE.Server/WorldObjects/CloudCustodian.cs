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
    /// Real deposit-into-Cloud-custody routing (DEP-002, DEP-003, eligibility, ledger/outbox) is
    /// <see cref="Player.HandleCloudCustodianDeposit"/> (AC Cloud Mule issue #13):
    /// <see cref="Player_Commerce.HandleActionSellItem"/> routes every sale to a
    /// <see cref="CloudCustodian"/> there instead of the ordinary <see cref="Vendor.ProcessItemsForPurchase"/>
    /// resell/destroy path, so <see cref="ProcessItemsForPurchase"/> below is never reached in
    /// production; it exists only as a defensive guard against a future caller accidentally
    /// reintroducing that path for this vendor type.
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

            // Nothing is offered for sale (DEP-001: never a personal vendor with resale inventory).
            // MerchandiseItemTypes/MinValue/MaxValue no longer gate incoming sell rows -- Player_Commerce
            // .HandleActionSellItem routes every Cloud Custodian sale to Player.HandleCloudCustodianDeposit
            // before VerifySellItems ever runs, and Cloud eligibility is decided by
            // CloudItemEligibilityPolicy instead of an ItemType bitmask -- but these are still kept
            // non-null so ValidateVendorRequirements() passes and the vendor window opens normally.
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
        /// Never reached in production: <see cref="Player_Commerce.HandleActionSellItem"/> routes
        /// every Cloud Custodian sale to <see cref="Player.HandleCloudCustodianDeposit"/> before
        /// reaching this call. Ordinary <see cref="Vendor.ProcessItemsForPurchase"/> resell/destroy
        /// semantics are unsafe for a Cloud deposit, so this override throws rather than silently
        /// falling back to them.
        /// </summary>
        public override void ProcessItemsForPurchase(Player player, Dictionary<uint, WorldObject> items)
        {
            throw new InvalidOperationException(
                "CloudCustodian.ProcessItemsForPurchase must never be called; Cloud Custodian sales route through Player.HandleCloudCustodianDeposit.");
        }
    }
}
