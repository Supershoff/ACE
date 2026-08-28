using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// The dedicated Cloud Custodian deposit handler (AC Cloud Mule issue #13, DEP-001..005, DEP-009,
    /// ARCH-002, ARCH-005): <see cref="Player_Commerce.HandleActionSellItem"/> routes every
    /// <see cref="CloudCustodian"/> sale here instead of the ordinary
    /// <see cref="Vendor.ProcessItemsForPurchase"/> resell/destroy path, because a Cloud deposit must
    /// atomically replace world possession with a Cloud Custody Record rather than resell or destroy
    /// the item.
    /// </summary>
    partial class Player
    {
        private static readonly ILog cloudCustodianLog = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Validates and commits every submitted row independently (DEP-002): a row that fails
        /// eligibility, quantity, or duplicate checks -- or whose deposit is rejected by the Cloud
        /// persistence boundary -- stays with the player and reports its exact reason, while every
        /// other valid row in the same submission still deposits.
        /// </summary>
        public void HandleCloudCustodianDeposit(CloudCustodian custodian, List<ItemProfile> itemProfiles)
        {
            var shardId = ConfigManager.Config.CloudMule.ShardId;

            var allPossessions = GetAllPossessions();
            var possessionsByGuid = new Dictionary<uint, WorldObject>();
            foreach (var wo in allPossessions)
            {
                possessionsByGuid[wo.Guid.Full] = wo;
            }

            var seenGuids = new HashSet<uint>();
            var pendingDeposits = new List<PendingCloudDeposit>();

            foreach (var itemProfile in itemProfiles)
            {
                if (!possessionsByGuid.TryGetValue(itemProfile.ObjectGuid, out var item))
                {
                    cloudCustodianLog.Warn(
                        $"[CLOUD CUSTODIAN] {Name} tried to deposit item 0x{itemProfile.ObjectGuid:X8} not in their possession to {custodian.Name}.");
                    continue;
                }

                var isDuplicate = !seenGuids.Add(item.Guid.Full);

                var saleWindow = CloudCustodianManager.ValidateSaleWindow(custodian);

                var request = new CloudCustodianDepositRowRequest(
                    new CloudItemId(item.Guid.Full),
                    itemProfile.Amount,
                    currentStackSize: item.StackSize ?? 1,
                    isStackable: item.StackSize.HasValue,
                    isDuplicateInSubmission: isDuplicate,
                    BuildEligibilitySnapshot(item),
                    rawPyrealAmount: IsRawPyrealCoinStack(item) ? (long)(item.Value ?? 0) : null);

                var decision = CloudCustodianDepositRowPolicy.Decide(request, saleWindow);

                if (decision.Kind == CloudCustodianDepositRowDecisionKind.Reject)
                {
                    SendTransientError(decision.PlayerMessage);
                    continue;
                }

                var pending = PrepareCloudDepositRow(item, decision, shardId);
                if (pending != null)
                {
                    pendingDeposits.Add(pending);
                }
            }

            // DEP-006: a raw Pyreal row mutates the account's single Pyreal Remainder rather than an
            // independent biota, so those rows commit one at a time (never concurrently with each
            // other) after every ordinary row has already committed concurrently as before.
            var ordinaryDeposits = pendingDeposits.Where(p => p.Decision.Kind != CloudCustodianDepositRowDecisionKind.ConvertPyreal).ToList();
            var pyrealConversions = pendingDeposits.Where(p => p.Decision.Kind == CloudCustodianDepositRowDecisionKind.ConvertPyreal).ToList();

            var depositedCount = ordinaryDeposits.Count == 0 ? 0 : CommitPendingCloudDeposits(custodian, ordinaryDeposits);
            depositedCount += pyrealConversions.Count == 0 ? 0 : CommitPendingPyrealConversions(custodian, pyrealConversions);

            if (depositedCount == 0)
            {
                Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
                SendUseDoneEvent();
                return;
            }

            Session.Network.EnqueueSend(new GameMessageSound(Guid, Sound.PickUpItem));
            SendUseDoneEvent();
        }

        /// <summary>
        /// The local half of a deposit row that has cleared eligibility (DEP-002, ARCH-005): removes
        /// <paramref name="item"/> from this player's in-memory possession for immediate UI feedback.
        /// Deliberately does not persist that removal to ace_shard itself: the Cloud persistence
        /// boundary (<see cref="DepositRowToCloudAsync"/>) removes the biota's world possession and
        /// creates its Cloud Custody Record together in one MariaDB transaction, so a crash or
        /// rejected Cloud commit can never leave the biota with neither world possession nor Cloud
        /// custody (AC Cloud Mule review of issue #13, finding 1: a separate, already-committed
        /// removal here left exactly that orphan window open). Kept separate from the Cloud custody
        /// call itself so every prepared row's Cloud-DB round trip can run concurrently instead of one
        /// at a time (AC Cloud Mule review of issue #13, finding 3: sequential per-row Cloud calls
        /// stalled the whole world tick for the cumulative round-trip time of every row in the
        /// submission).
        /// </summary>
        private PendingCloudDeposit PrepareCloudDepositRow(WorldObject item, CloudCustodianDepositRowDecision decision, string shardId)
        {
            // Equipped items are already rejected by eligibility (DEP-003:
            // CloudEligibilityRejectionCode.MustBeInOrdinaryInventory) before this method is ever
            // called, so only the ordinary-inventory removal path applies here.
            if (!TryRemoveFromInventoryWithNetworking(item.Guid, out _, RemoveFromInventoryAction.SellItem))
            {
                cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Item 0x{item.Guid.Full:X8}:{item.Name} for player {Name} not found in HandleCloudCustodianDeposit.");
                SendTransientError("That item could not be removed from your possession.");
                return null;
            }

            return new PendingCloudDeposit(
                item,
                decision,
                shardId,
                CloudOwnerIdentity.ForAccount(shardId, Session.AccountId),
                CloudOwnerIdentity.DepositIdempotencyKey(shardId, item.Guid.Full));
        }

        /// <summary>
        /// Commits every prepared row's Cloud custody call concurrently rather than one at a time
        /// (<see cref="RunConcurrentlyAsync{T}"/>): each row targets a distinct biota and idempotency
        /// key, so running them together is safe and bounds this submission's Cloud-side world-tick
        /// stall to roughly one round trip instead of one per row.
        /// </summary>
        private int CommitPendingCloudDeposits(CloudCustodian custodian, List<PendingCloudDeposit> pendingDeposits)
        {
            var operations = pendingDeposits
                .Select(pending => (Func<Task<(CloudBoundaryOutcomeKind Kind, string Reason)>>)(() => DepositRowToCloudAsync(pending)))
                .ToList();

            var outcomes = RunConcurrentlyAsync(operations).GetAwaiter().GetResult();

            var depositedCount = 0;

            for (var i = 0; i < pendingDeposits.Count; i++)
            {
                var pending = pendingDeposits[i];
                var (outcomeKind, reason) = outcomes[i];

                if (outcomeKind != CloudBoundaryOutcomeKind.Committed)
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Deposit of 0x{pending.Item.Guid.Full:X8}:{pending.Item.Name} for player {Name} was not committed: {reason}");
                    }

                    RestoreCloudDepositCandidate(pending.Item);
                    SendTransientError($"The Cloud Custodian could not accept the {pending.Item.Name}. Please try again.");
                    continue;
                }

                Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, pending.Item, custodian));
                depositedCount++;
            }

            return depositedCount;
        }

        private async Task<(CloudBoundaryOutcomeKind Kind, string Reason)> DepositRowToCloudAsync(PendingCloudDeposit pending)
        {
            try
            {
                using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
                var boundary = new CloudCustodyBoundary(context);

                if (pending.Decision.Kind == CloudCustodianDepositRowDecisionKind.DepositStack)
                {
                    var outcome = await boundary.DepositStackAsync(
                        pending.Item.Guid.Full, pending.ShardId, pending.OwnerId, pending.Decision.Quantity, pending.IdempotencyKey,
                        preservationRequirements: pending.Decision.PreservationRequirements);
                    return (outcome.Kind, outcome.Reason);
                }
                else
                {
                    var outcome = await boundary.DepositAsync(
                        pending.Item.Guid.Full, pending.ShardId, pending.OwnerId, pending.IdempotencyKey,
                        preservationRequirements: pending.Decision.PreservationRequirements);
                    return (outcome.Kind, outcome.Reason);
                }
            }
            catch (Exception ex)
            {
                cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Deposit of 0x{pending.Item.Guid.Full:X8}:{pending.Item.Name} for player {Name} threw.", ex);
                return (CloudBoundaryOutcomeKind.Unavailable, null);
            }
        }

        /// <summary>
        /// True for a raw Pyreal coin-stack row (WCID 273, DEP-006): the row that a Cloud Custodian
        /// deposit converts into MMDs plus an updated Pyreal Remainder instead of depositing as
        /// itself. Distinguished from a Trade Note (a different, already-converted WCID whose name
        /// starts with "tradenote") and from every other coin-type row by its exact WCID.
        /// </summary>
        private static bool IsRawPyrealCoinStack(WorldObject item) =>
            item.WeenieType == WeenieType.Coin && item.WeenieClassId == 273;

        /// <summary>
        /// Commits every prepared raw-Pyreal conversion row one at a time (DEP-006): unlike an
        /// ordinary deposit row, a conversion mutates the account's single shared Pyreal Remainder,
        /// so running two of them concurrently for the same submission would race that shared
        /// resource. Each row still only ever competes with a <em>different</em> submission's
        /// conversion for the same account -- a rare case <see cref="TryConvertPyrealDepositRow"/>'s
        /// bounded retry already handles safely.
        /// </summary>
        private int CommitPendingPyrealConversions(CloudCustodian custodian, List<PendingCloudDeposit> pendingConversions)
        {
            var convertedCount = 0;

            foreach (var pending in pendingConversions)
            {
                if (TryConvertPyrealDepositRow(pending))
                {
                    Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, pending.Item, custodian));
                    convertedCount++;
                }
                else
                {
                    RestoreCloudDepositCandidate(pending.Item);
                    SendTransientError($"The Cloud Custodian could not accept the {pending.Item.Name}. Please try again.");
                }
            }

            return convertedCount;
        }

        /// <summary>
        /// Converts one raw-Pyreal row (DEP-006): reads the account's current Pyreal Remainder,
        /// computes the exact MMD count that combined total requires (<see cref="PyrealConversionPolicy"/>),
        /// materializes that many MMD biotas through ACE's own factory/GUID allocator (ARCH-002,
        /// ARCH-010 -- <see cref="CloudCustodyBoundary.ConvertPyrealDepositAsync"/> never allocates one
        /// itself), and hands them to the boundary for the atomic commit. The remainder read here is
        /// not transactionally authoritative, so a concurrent conversion for the same account (a rare
        /// race between two of that account's characters depositing raw Pyreals at literally the same
        /// moment) can make the boundary refuse with a Conflict; this is retried a bounded number of
        /// times with a freshly read remainder, destroying any unused pre-created MMDs between
        /// attempts so no orphan biota is ever left behind.
        /// </summary>
        private bool TryConvertPyrealDepositRow(PendingCloudDeposit pending)
        {
            const int maxAttempts = 3;
            var rawAmount = pending.Decision.RawPyrealAmount;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                long remainder;
                PyrealConversionResult conversion;

                try
                {
                    remainder = ReadPyrealRemainderAsync(pending.ShardId, pending.OwnerId).GetAwaiter().GetResult();
                    conversion = PyrealConversionPolicy.Convert(remainder, rawAmount);
                }
                catch (Exception ex)
                {
                    cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Reading/computing the Pyreal conversion for player {Name} threw.", ex);
                    return false;
                }

                var mmdBiotas = CreateMmdBiotas(conversion.MmdCount);
                if (mmdBiotas == null)
                {
                    return false;
                }

                var (outcomeKind, reason) = ConvertPyrealDepositToCloudAsync(pending, rawAmount, mmdBiotas).GetAwaiter().GetResult();

                if (outcomeKind == CloudBoundaryOutcomeKind.Committed)
                {
                    return true;
                }

                DestroyUnusedMmdBiotas(mmdBiotas);

                if (outcomeKind != CloudBoundaryOutcomeKind.Conflict)
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Pyreal conversion for player {Name} was not committed: {reason}");
                    }

                    return false;
                }
            }

            cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Pyreal conversion for player {Name} lost {maxAttempts} consecutive races on their own Pyreal Remainder.");
            return false;
        }

        /// <summary>
        /// Materializes and synchronously persists <paramref name="count"/> new MMD (Trade Note
        /// (250,000), WCID 20630) biotas with no Container/Wielder/Location -- ACE's own GUID
        /// allocator assigns each one's GUID (ARCH-002, ARCH-010). Returns null (destroying any
        /// biotas already created in this attempt) if any single creation/persist fails, so a caller
        /// never hands a partial batch to the Cloud boundary.
        /// </summary>
        private List<WorldObject> CreateMmdBiotas(long count)
        {
            var mmdBiotas = new List<WorldObject>();

            for (var i = 0L; i < count; i++)
            {
                var mmd = WorldObjectFactory.CreateNewWorldObject((uint)ACE.Server.Factories.Enum.WeenieClassName.tradenote250000);
                if (mmd == null || !SynchronouslyPersist(mmd))
                {
                    cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Failed to create/persist an MMD for player {Name}'s Pyreal conversion.");
                    DestroyUnusedMmdBiotas(mmdBiotas);
                    return null;
                }

                mmdBiotas.Add(mmd);
            }

            return mmdBiotas;
        }

        /// <summary>
        /// Deletes a batch of MMD biotas that were persisted (<see cref="CreateMmdBiotas"/>) but never
        /// entered Cloud custody -- either because a later step in the same attempt failed, or because
        /// <see cref="TryConvertPyrealDepositRow"/>'s conversion attempt lost a race and must retry
        /// with a freshly computed count. Leaving them would violate ARCH-005: a biota with neither
        /// world possession nor a Cloud Custody Record must never persist.
        /// </summary>
        private void DestroyUnusedMmdBiotas(List<WorldObject> mmdBiotas)
        {
            foreach (var mmd in mmdBiotas)
            {
                try
                {
                    DatabaseManager.Shard.BaseDatabase.RemoveBiota(mmd.Biota.Id);
                }
                catch (Exception ex)
                {
                    cloudCustodianLog.Error(
                        $"[CLOUD CUSTODIAN] Failed to clean up unused MMD 0x{mmd.Guid.Full:X8} after a Pyreal conversion retry for player {Name}.", ex);
                }
            }
        }

        private async Task<long> ReadPyrealRemainderAsync(string shardId, Guid ownerId)
        {
            using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
            var boundary = new CloudCustodyBoundary(context);
            return await boundary.GetPyrealRemainderAsync(shardId, ownerId);
        }

        private async Task<(CloudBoundaryOutcomeKind Kind, string Reason)> ConvertPyrealDepositToCloudAsync(
            PendingCloudDeposit pending, long rawAmount, List<WorldObject> mmdBiotas)
        {
            try
            {
                using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
                var boundary = new CloudCustodyBoundary(context);

                var mmdBiotaIds = mmdBiotas.Select(mmd => mmd.Guid.Full).ToList();

                var outcome = await boundary.ConvertPyrealDepositAsync(
                    pending.Item.Guid.Full, pending.ShardId, pending.OwnerId, rawAmount, mmdBiotaIds, pending.IdempotencyKey);
                return (outcome.Kind, outcome.Reason);
            }
            catch (Exception ex)
            {
                cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Pyreal conversion for player {Name} threw.", ex);
                return (CloudBoundaryOutcomeKind.Unavailable, null);
            }
        }

        /// <summary>
        /// Starts every operation before awaiting any of them, so the whole batch runs concurrently
        /// instead of one at a time. Kept free of any live WorldObject/Player dependency so it can run
        /// in a table-driven unit test (AC Cloud Mule review of issue #13, finding 3).
        /// </summary>
        internal static async Task<IReadOnlyList<T>> RunConcurrentlyAsync<T>(IReadOnlyList<Func<Task<T>>> operations)
        {
            var tasks = new Task<T>[operations.Count];
            for (var i = 0; i < operations.Count; i++)
            {
                tasks[i] = operations[i]();
            }

            return await Task.WhenAll(tasks);
        }

        private sealed record PendingCloudDeposit(
            WorldObject Item,
            CloudCustodianDepositRowDecision Decision,
            string ShardId,
            Guid OwnerId,
            Guid IdempotencyKey);

        /// <summary>
        /// Runs <paramref name="persist"/> and reports any exception it throws to
        /// <paramref name="onException"/> instead of letting it propagate, converting it to a plain
        /// <c>false</c> return so a caller can use a single boolean check instead of a try/catch (AC
        /// Cloud Mule review of issue #13, finding 1: an uncaught exception from
        /// <c>ShardDatabase.GetBiota</c>/<c>SaveBiota</c> used to bypass
        /// <see cref="SynchronouslyPersist"/>'s failure handling entirely and destroy the deposited
        /// item). Kept free of any live WorldObject/Player dependency so it can run in a unit test.
        /// </summary>
        internal static bool TryRunSynchronousPersist(Func<bool> persist, Action<Exception> onException)
        {
            try
            {
                return persist();
            }
            catch (Exception ex)
            {
                onException(ex);
                return false;
            }
        }

        private bool SynchronouslyPersist(WorldObject item) =>
            TryRunSynchronousPersist(
                () =>
                {
                    item.SaveBiotaToDatabase(false);
                    return DatabaseManager.Shard.BaseDatabase.SaveBiota(item.Biota, item.BiotaDatabaseLock);
                },
                ex => cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Synchronous persist of 0x{item.Guid.Full:X8}:{item.Name} threw.", ex));

        private void RestoreCloudDepositCandidate(WorldObject item)
        {
            if (!TryCreateInInventoryWithNetworking(item))
            {
                cloudCustodianLog.Error(
                    $"[CLOUD CUSTODIAN] Failed to restore item 0x{item.Guid.Full:X8}:{item.Name} to player {Name} after a rejected Cloud deposit.");
                return;
            }

            SynchronouslyPersist(item);
        }

        /// <summary>
        /// Maps a live ACE <c>WorldObject</c> to the pure facts <see cref="CloudItemEligibilityPolicy"/>
        /// needs (DEP-003, DEP-004). <see cref="CloudItemEligibilitySnapshot.HasActiveCooldownOrAttachment"/>
        /// always resolves to false: no ACE item currently exposes generic, item-instance-scoped
        /// active-cooldown state outside pet devices, which <see cref="CloudItemEligibilitySnapshot.HasActivePetAttached"/>
        /// already covers; this flag is reserved for a future cooldown-bearing item type.
        /// </summary>
        private CloudItemEligibilitySnapshot BuildEligibilitySnapshot(WorldObject item)
        {
            var isCurrentlyTradedOrReserved = IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow);

            return new CloudItemEligibilitySnapshot(
                new CloudItemId(item.Guid.Full),
                isLegalForPlayerToPlayerTrade: !item.Retained,
                isEquipped: item.CurrentWieldedLocation.HasValue,
                isContainer: item is Container,
                isAttunedOrContainsAttuned: item.IsAttunedOrContainsAttuned,
                hasActivePetAttached: item is PetDevice petDevice && petDevice.Pet.HasValue,
                isCharacterBoundOrUnsafeStateful: item.Bonded is BondedStatus.Bonded or BondedStatus.Sticky,
                hasFiniteLifespan: item.Lifespan.HasValue,
                hasActiveCooldownOrAttachment: false,
                isCurrentlyTradedOrReserved: isCurrentlyTradedOrReserved,
                runtimeEnchantments: BuildRuntimeEnchantments(item.Biota.PropertiesEnchantmentRegistry.Clone(item.BiotaDatabaseLock)));
        }

        /// <summary>
        /// Reduces a live item's active enchantment registry to the Frozen Enchantment preservation
        /// list DEP-005 requires (<see cref="CloudItemEligibilitySnapshot.RuntimeEnchantments"/>):
        /// only runtime (temporary) enchantments, each with its currently remaining duration
        /// (Duration + StartTime -- StartTime ticks backwards toward -Duration every heartbeat, see
        /// <c>PropertiesEnchantmentRegistryExtensions.HeartBeatEnchantmentsAndReturnExpired</c>).
        /// Permanent built-in item spells (Duration == -1, an equip-linked enchantment that lasts
        /// only as long as the item stays equipped) and cooldown pseudo-entries (SpellId greater
        /// than <see cref="short.MaxValue"/>, the same range <c>EnchantmentManager.GetCooldownSpellID</c>
        /// reserves) are excluded, matching <c>EnchantmentManager.RemoveAllEnchantments</c>'s own
        /// exclusion pattern -- DEP-005: "Permanent built-in spells remain ordinary static
        /// properties." Kept free of any live <c>WorldObject</c>/<c>EnchantmentManager</c> dependency
        /// (only the plain registry entries it is handed) so it can run in a table-driven unit test.
        /// </summary>
        internal static IReadOnlyList<CloudRuntimeEnchantmentSnapshot> BuildRuntimeEnchantments(
            IEnumerable<PropertiesEnchantmentRegistry> activeEnchantments)
        {
            if (activeEnchantments is null)
            {
                return [];
            }

            var preserved = new List<CloudRuntimeEnchantmentSnapshot>();

            foreach (var entry in activeEnchantments)
            {
                if (entry.Duration == -1 || entry.SpellId > short.MaxValue)
                {
                    continue;
                }

                var remainingDurationSeconds = entry.Duration + entry.StartTime;
                if (remainingDurationSeconds <= 0)
                {
                    continue;
                }

                preserved.Add(new CloudRuntimeEnchantmentSnapshot(entry.SpellId, remainingDurationSeconds));
            }

            return preserved;
        }
    }
}
