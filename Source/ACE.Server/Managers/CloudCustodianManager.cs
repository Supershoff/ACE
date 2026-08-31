using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using log4net;

using Microsoft.EntityFrameworkCore;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Owns the versioned Custodian configuration lifecycle for this shard (DEP-007, DEP-008,
    /// ADM-003): loading it from the ace_cloud schema, resolving it against this shard's live
    /// Marketplace and Mansion positions from ace_world, and spawning, despawning, or leaving alone
    /// each live <see cref="CloudCustodian"/> NPC on the world thread so that hot-applying an admin
    /// configuration change never requires an ACE restart. AC Cloud Mule is opt-in (CONTEXT.md);
    /// every entry point here is a no-op unless <c>CloudMule.Enabled</c> is configured.
    /// </summary>
    public static class CloudCustodianManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Guards both fields below. Writes happen inside the ActionEventDelegate
        /// <see cref="ReapplyAsync"/> enqueues, and reads happen from <see cref="ValidateSaleWindow"/>
        /// while a player's sell commit is handled; ACE's landblock groups can tick concurrently on
        /// separate threads (MultiThreadedLandblockGroupTicking), so a genuine lock -- not just the
        /// "single world thread" assumption -- is what keeps a concurrent reapply and sale commit
        /// from observing a torn update.
        /// </summary>
        private static readonly object _stateLock = new object();

        private static readonly Dictionary<CloudCustodianLocationKey, CloudCustodian> _spawned = new();

        private static CloudCustodianConfiguration _currentConfiguration;

        /// <summary>
        /// Kicks off the initial load and spawn. Called once from Program.cs, immediately after
        /// <see cref="WorldManager.Initialize"/>, the same way PlayerEnterWorld's DB read is followed
        /// by a world-thread-enqueued apply.
        /// </summary>
        public static void Initialize()
        {
            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                log.Info("AC Cloud Mule is disabled (CloudMule.Enabled = false in Config.js); Cloud Custodians will not be spawned.");
                return;
            }

            CloudIdentityEventManager.RunStartupIntegrityCheck();

            _ = ReapplyAsync();
        }

        /// <summary>
        /// Reloads the current Custodian configuration from ace_cloud and this shard's live
        /// Marketplace/Mansion positions from ace_world, then applies the resulting spawn/despawn
        /// plan on the world thread (DEP-008: "apply without an ACE restart"). Safe to call at any
        /// time, including from an admin command immediately after a configuration change commits.
        /// </summary>
        public static async Task ReapplyAsync()
        {
            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                return;
            }

            var shardId = ConfigManager.Config.CloudMule.ShardId;
            if (string.IsNullOrWhiteSpace(shardId))
            {
                log.Error("AC Cloud Mule is enabled but CloudMule.ShardId is not configured; Cloud Custodians will not be spawned.");
                return;
            }

            CloudCustodianConfiguration configuration;

            try
            {
                var options = CloudDbContextOptionsFactory.Create(BuildCloudConnectionString());
                await using var context = new CloudDbContext(options);
                var boundary = new CloudCustodianConfigurationBoundary(context);
                configuration = await boundary.GetCurrentAsync(shardId);
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: failed to read Custodian configuration from ace_cloud; leaving currently spawned Custodians unchanged.", ex);
                return;
            }

            await BackfillInventoryPropertiesAsync(shardId).ConfigureAwait(false);

            var marketplacePosition = ResolveMarketplacePosition();
            var mansions = ResolveMansionLocations();

            var desired = CloudCustodianLocationResolver.Resolve(configuration, marketplacePosition, mansions);

            WorldManager.EnqueueAction(new ActionEventDelegate(() => ApplyPlanOnWorldThread(configuration, desired)));
        }

        /// <summary>
        /// Repairs missing disposable inventory-display rows from the authoritative native biotas
        /// ACE deliberately retains while they are in Cloud custody. Deposit-time capture normally
        /// creates these rows; this bounded startup/reapply pass covers deployments upgraded from an
        /// earlier build and transient projection-write failures without giving a companion service
        /// direct access to ace_shard (ARCH-002/ARCH-004).
        /// </summary>
        private static async Task BackfillInventoryPropertiesAsync(string shardId, CancellationToken cancellationToken = default)
        {
            const int maxRowsPerPass = 500;

            try
            {
                var options = CloudDbContextOptionsFactory.Create(BuildCloudConnectionString());
                await using var context = new CloudDbContext(options);

                var missing = await context.CloudCustodyRecords
                    .AsNoTracking()
                    .Where(record => record.ShardId == shardId)
                    .Where(record => !context.CloudInventoryItemPropertiesProjections.Any(properties => properties.BiotaId == record.BiotaId))
                    .OrderBy(record => record.BiotaId)
                    .Select(record => new { record.BiotaId, record.Version })
                    .Take(maxRowsPerPass)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var gateway = new CloudInventoryItemPropertiesGateway(context);
                var applied = 0;

                foreach (var candidate in missing)
                {
                    var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(candidate.BiotaId, doNotAddToCache: true);
                    if (biota is null)
                    {
                        log.Warn($"AC Cloud Mule: Cloud custody record 0x{candidate.BiotaId:X8} has no retained native biota; inventory property backfill skipped it.");
                        continue;
                    }

                    var name = biota.GetProperty(PropertyString.Name);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = $"Item 0x{candidate.BiotaId:X8}";
                    }

                    var itemType = (ItemType)(uint)(biota.GetProperty(PropertyInt.ItemType) ?? 0);
                    var wasApplied = await gateway.UpsertAsync(
                        candidate.BiotaId,
                        shardId,
                        name,
                        itemType,
                        (WeenieType)biota.WeenieType,
                        biota.GetProperty(PropertyInt.Value),
                        biota.GetProperty(PropertyInt.EncumbranceVal),
                        iconCacheKeyHex: null,
                        revision: candidate.Version,
                        cancellationToken)
                        .ConfigureAwait(false);

                    if (wasApplied)
                    {
                        applied++;
                    }
                }

                if (applied > 0)
                {
                    log.Info($"AC Cloud Mule: backfilled inventory display properties for {applied} Cloud custody record(s).");
                }
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: inventory property backfill failed; custody remains authoritative and a later reapply will retry it.", ex);
            }
        }

        /// <summary>
        /// Called by a spawned <see cref="CloudCustodian"/> at sale commit (DEP-008: "revalidate the
        /// configuration version at sale commit"). Current only when <paramref name="custodian"/> is
        /// still the exact live instance this manager currently has spawned for its location -- a
        /// despawned or superseded instance (its location disabled, relocated, or replaced by a fresh
        /// respawn under a newer configuration version) is always reported stale, even if it has not
        /// yet been destroyed.
        /// </summary>
        public static CloudCustodianSaleWindowValidation ValidateSaleWindow(CloudCustodian custodian)
        {
            ArgumentNullException.ThrowIfNull(custodian);

            lock (_stateLock)
            {
                var isCurrentLiveInstance =
                    custodian.LocationKey is not null
                    && _spawned.TryGetValue(custodian.LocationKey, out var live)
                    && ReferenceEquals(live, custodian);

                var currentVersion = _currentConfiguration?.Version ?? custodian.SpawnedAtConfigVersion;

                return CloudCustodianSaleWindowPolicy.Validate(isCurrentLiveInstance, custodian.SpawnedAtConfigVersion, currentVersion);
            }
        }

        private static void ApplyPlanOnWorldThread(CloudCustodianConfiguration configuration, IReadOnlyList<CloudCustodianLocation> desired)
        {
            lock (_stateLock)
            {
                var plan = CloudCustodianSpawnPlanner.Plan(desired, _spawned.Keys.ToList());

                foreach (var key in plan.ToDespawn)
                {
                    if (_spawned.Remove(key, out var custodian))
                    {
                        custodian.Destroy();
                    }
                }

                foreach (var location in plan.ToSpawn)
                {
                    var custodian = SpawnCustodian(location, configuration.Version);
                    if (custodian is not null)
                    {
                        _spawned[location.Key] = custodian;
                    }
                }

                _currentConfiguration = configuration;
            }
        }

        private static CloudCustodian SpawnCustodian(CloudCustodianLocation location, CloudAggregateVersion configVersion)
        {
            var baseWeenieClassId = ConfigManager.Config.CloudMule.CustodianBaseWeenieClassId;
            if (baseWeenieClassId == 0)
            {
                log.Error($"AC Cloud Mule: CloudMule.CustodianBaseWeenieClassId is not configured; skipping Cloud Custodian spawn at {location.Key}.");
                return null;
            }

            var weenie = DatabaseManager.World.GetCachedWeenie(baseWeenieClassId);
            if (weenie == null)
            {
                log.Error($"AC Cloud Mule: base WeenieClassId {baseWeenieClassId} was not found in ace_world; skipping Cloud Custodian spawn at {location.Key}.");
                return null;
            }

            var guid = GuidManager.NewDynamicGuid();
            var custodian = new CloudCustodian(weenie, guid);
            custodian.AssignLocation(location.Key, configVersion);
            custodian.Location = ToPosition(location.Position);

            if (!custodian.EnterWorld())
            {
                log.Error($"AC Cloud Mule: Cloud Custodian failed to enter the world at {location.Key} ({location.Position.Raw}).");
                GuidManager.RecycleDynamicGuid(guid);
                return null;
            }

            return custodian;
        }

        private static Position ToPosition(CloudCustodianPosition position) =>
            new Position(position.Landblock, position.X, position.Y, position.Z, position.RotationX, position.RotationY, position.RotationZ, position.RotationW);

        /// <summary>
        /// Mirrors ACE's own <c>Player_Location.MarketplaceDrop</c> fallback (a hardcoded landblock
        /// used when the "portalmarketplace" weenie is not present), so Cloud Mule's notion of "the
        /// Marketplace" always matches wherever ordinary <c>@marketplace</c> teleport already sends
        /// players on this shard.
        /// </summary>
        private static CloudCustodianPosition ResolveMarketplacePosition()
        {
            var weenie = DatabaseManager.World.GetCachedWeenie("portalmarketplace");
            var position = weenie?.GetPosition(PositionType.Destination)
                ?? new Position(0x016C01BC, 49.206f, -31.935f, 0.005f, 0, 0, -0.707107f, 0.707107f);

            return CloudCustodianPosition.TryParse(position.ToLOCString());
        }

        /// <summary>
        /// Enumerates every Mansion-tier housing plot placed as static content in this shard's own
        /// ace_world database (DEP-007: "Default Custodian locations are every mansion"). There is no
        /// fixed, shippable list of "every mansion" -- housing plots are per-shard world content -- so
        /// this queries the operator's own live world database rather than any hardcoded coordinate
        /// table, the same way HouseManager.BuildHouseIdToGuid resolves house weenie instances.
        /// </summary>
        private static List<CloudCustodianMansionLocation> ResolveMansionLocations()
        {
            try
            {
                using var context = new ACE.Database.Models.World.WorldDbContext();

                var mansionInstances =
                    from weenie in context.Weenie
                    join instance in context.LandblockInstance on weenie.ClassId equals instance.WeenieClassId
                    join houseType in context.WeeniePropertiesInt on weenie.ClassId equals houseType.ObjectId
                    where weenie.Type == (int)WeenieType.House
                        && houseType.Type == (int)PropertyInt.HouseType
                        && houseType.Value == (int)HouseType.Mansion
                    select instance;

                var mansions = new List<CloudCustodianMansionLocation>();

                foreach (var instance in mansionInstances.ToList())
                {
                    var raw =
                        $"0x{instance.ObjCellId:X8} [{instance.OriginX:F6} {instance.OriginY:F6} {instance.OriginZ:F6}] " +
                        $"{instance.AnglesW:F6} {instance.AnglesX:F6} {instance.AnglesY:F6} {instance.AnglesZ:F6}";

                    var position = CloudCustodianPosition.TryParse(raw);
                    if (position is not null)
                    {
                        mansions.Add(new CloudCustodianMansionLocation(instance.Guid, position));
                    }
                }

                return mansions;
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: failed to enumerate Mansion positions from ace_world; treating the Mansion set as empty for this reapplication.", ex);
                return [];
            }
        }

        /// <summary>
        /// Read-only diagnostic for issue #34's blocking defect #5: every remaining prerequisite an
        /// operator needs to reach an actual Cloud Custodian deposit (matching ShardId, a reachable
        /// and matching CloudShardBinding, a resolvable Vendor-type base weenie, and at least one
        /// resolved Custodian location), reported so the disposable local acceptance launcher can give
        /// an actionable diagnostic before starting the web stack instead of silently no-op-spawning.
        /// Served over <see cref="CloudWorldBoundaryHealthHost"/>'s loopback/private endpoint; this
        /// never mutates custody state, only reads configuration and ace_world/ace_cloud diagnostics
        /// the same way <see cref="ReapplyAsync"/> already does.
        /// </summary>
        public static async Task<CloudMuleDepositReadinessReport> GetDepositReadinessAsync(CancellationToken cancellationToken = default)
        {
            var config = ConfigManager.Config.CloudMule;
            if (!config.Enabled)
            {
                return CloudMuleDepositReadinessReport.Disabled();
            }

            var shardId = config.ShardId;
            var shardIdConfigured = !string.IsNullOrWhiteSpace(shardId);

            string shardBindingStatus;
            string shardBindingDetail;

            if (!shardIdConfigured)
            {
                shardBindingStatus = "NotConfigured";
                shardBindingDetail = "CloudMule.ShardId is not configured.";
            }
            else
            {
                try
                {
                    var options = CloudDbContextOptionsFactory.Create(BuildCloudConnectionString());
                    await using var context = new CloudDbContext(options);
                    var diagnostics = new CloudGatewayDiagnostics(context);
                    var hasBinding = await diagnostics.HasShardBindingAsync(cancellationToken).ConfigureAwait(false);

                    if (!hasBinding)
                    {
                        shardBindingStatus = "Missing";
                        shardBindingDetail = "This deployment has no CloudShardBinding row; prepare ace_cloud first.";
                    }
                    else
                    {
                        var binding = await context.CloudShardBindings.AsNoTracking().SingleAsync(cancellationToken).ConfigureAwait(false);
                        if (binding.ShardId == shardId)
                        {
                            shardBindingStatus = "Matches";
                            shardBindingDetail = $"CloudShardBinding.ShardId matches CloudMule.ShardId ({shardId}).";
                        }
                        else
                        {
                            shardBindingStatus = "Mismatch";
                            shardBindingDetail = $"CloudMule.ShardId ({shardId}) does not match CloudShardBinding.ShardId ({binding.ShardId}).";
                        }
                    }
                }
                catch (Exception ex)
                {
                    shardBindingStatus = "Unavailable";
                    shardBindingDetail = $"Could not reach ace_cloud via MySql.Cloud: {ex.Message}";
                }
            }

            var baseWeenieClassId = config.CustodianBaseWeenieClassId;
            var weenieConfigured = baseWeenieClassId != 0;
            var weenieFound = false;
            var weenieIsVendorType = false;

            if (weenieConfigured)
            {
                var weenie = DatabaseManager.World.GetCachedWeenie(baseWeenieClassId);
                if (weenie is not null)
                {
                    weenieFound = true;
                    weenieIsVendorType = weenie.WeenieType == WeenieType.Vendor;
                }
            }

            var resolvedLocationCount = 0;
            if (shardBindingStatus == "Matches")
            {
                try
                {
                    var options = CloudDbContextOptionsFactory.Create(BuildCloudConnectionString());
                    await using var context = new CloudDbContext(options);
                    var boundary = new CloudCustodianConfigurationBoundary(context);
                    var configuration = await boundary.GetCurrentAsync(shardId, cancellationToken).ConfigureAwait(false);

                    var marketplacePosition = ResolveMarketplacePosition();
                    var mansions = ResolveMansionLocations();
                    resolvedLocationCount = CloudCustodianLocationResolver.Resolve(configuration, marketplacePosition, mansions).Count;
                }
                catch (Exception ex)
                {
                    log.Error("AC Cloud Mule: failed to resolve Custodian locations while reporting deposit readiness.", ex);
                }
            }

            var ready =
                shardIdConfigured
                && shardBindingStatus == "Matches"
                && weenieConfigured
                && weenieFound
                && weenieIsVendorType
                && resolvedLocationCount > 0;

            var reason = ready
                ? "Ready."
                : !shardIdConfigured ? "CloudMule.ShardId is not configured."
                : shardBindingStatus != "Matches" ? shardBindingDetail
                : !weenieConfigured ? "CloudMule.CustodianBaseWeenieClassId is not configured."
                : !weenieFound ? $"WeenieClassId {baseWeenieClassId} was not found in ace_world."
                : !weenieIsVendorType ? $"WeenieClassId {baseWeenieClassId} is not a Vendor-type weenie."
                : "No Custodian location resolved (Marketplace and Mansions are both disabled, and there are no custom positions).";

            return new CloudMuleDepositReadinessReport
            {
                CloudMuleEnabled = true,
                ShardId = shardId,
                ShardBindingStatus = shardBindingStatus,
                ShardBindingDetail = shardBindingDetail,
                CustodianWeenieConfigured = weenieConfigured,
                CustodianWeenieClassId = baseWeenieClassId,
                CustodianWeenieFound = weenieFound,
                CustodianWeenieIsVendorType = weenieIsVendorType,
                ResolvedCustodianLocationCount = resolvedLocationCount,
                Ready = ready,
                Reason = reason,
            };
        }

        /// <summary>
        /// Also used by the <c>@custodian</c> admin command (CloudCustodianCommands) so both share
        /// exactly one place that knows how to reach ace_cloud from ACE.Server.
        /// </summary>
        internal static string BuildCloudConnectionString()
        {
            var config = ConfigManager.Config.MySql.Cloud;
            return $"server={config.Host};port={config.Port};user={config.Username};password={config.Password};database={config.Database};{config.ConnectionOptions}";
        }
    }
}
