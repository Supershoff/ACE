using System;
using System.Linq;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Administrator controls for Cloud Custodian locations (DEP-007, DEP-008, ADM-003: "Admin
    /// controls include Custodian sets/custom positions"). Every subcommand persists through
    /// <see cref="CloudCustodianConfigurationBoundary"/> and then calls
    /// <see cref="CloudCustodianManager.ReapplyAsync"/> so the change hot-applies immediately,
    /// without a restart (DEP-008).
    /// </summary>
    public static class CloudCustodianCommands
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [CommandHandler("custodian", AccessLevel.Admin, CommandHandlerFlag.None, 1,
            "Manages Cloud Custodian locations.",
            "marketplace <on|off>\n" +
            "mansions <on|off>\n" +
            "add <ACE position string>\n" +
            "remove <position ID>\n" +
            "list\n" +
            "Example: @custodian add 0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309")]
        public static void HandleCustodian(Session session, params string[] parameters)
        {
            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                CommandHandlerHelper.WriteOutputInfo(session, "AC Cloud Mule is disabled (CloudMule.Enabled = false in Config.js).");
                return;
            }

            var shardId = ConfigManager.Config.CloudMule.ShardId;
            if (string.IsNullOrWhiteSpace(shardId))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "AC Cloud Mule is enabled but CloudMule.ShardId is not configured.");
                return;
            }

            var subcommand = parameters[0].ToLowerInvariant();
            var rest = parameters.Skip(1).ToArray();

            switch (subcommand)
            {
                case "marketplace":
                    HandleToggle(session, shardId, rest, "Marketplace", (boundary, enabled, expectedVersion) =>
                        boundary.SetMarketplaceEnabledAsync(shardId, enabled, expectedVersion));
                    break;

                case "mansions":
                    HandleToggle(session, shardId, rest, "Mansion set", (boundary, enabled, expectedVersion) =>
                        boundary.SetMansionsEnabledAsync(shardId, enabled, expectedVersion));
                    break;

                case "add":
                    HandleAdd(session, shardId, string.Join(" ", rest));
                    break;

                case "remove":
                    HandleRemove(session, shardId, rest.FirstOrDefault());
                    break;

                case "list":
                    HandleList(session, shardId);
                    break;

                default:
                    CommandHandlerHelper.WriteOutputInfo(session, $"Unknown @custodian subcommand \"{subcommand}\". See @help custodian.");
                    break;
            }
        }

        private static void HandleToggle(
            Session session,
            string shardId,
            string[] args,
            string label,
            Func<CloudCustodianConfigurationBoundary, bool, int, System.Threading.Tasks.Task<CloudBoundaryOutcome<CloudCustodianConfiguration>>> apply)
        {
            if (args.Length < 1 || !TryParseOnOff(args[0], out var enabled))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @custodian marketplace|mansions <on|off>");
                return;
            }

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await apply(boundary, enabled, current.Version.Value);
            }, $"{label} {(enabled ? "enabled" : "disabled")}.");
        }

        private static void HandleAdd(Session session, string shardId, string rawPosition)
        {
            if (string.IsNullOrWhiteSpace(rawPosition))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @custodian add <ACE position string>");
                return;
            }

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await boundary.AddCustomPositionAsync(shardId, rawPosition, current.Version.Value);
            }, "Custodian Location added.");
        }

        private static void HandleRemove(Session session, string shardId, string positionIdText)
        {
            if (!Guid.TryParse(positionIdText, out var positionId))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @custodian remove <position ID> (see @custodian list)");
                return;
            }

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await boundary.RemoveCustomPositionAsync(shardId, positionId, current.Version.Value);
            }, "Custodian Location removed.");
        }

        private static void HandleList(Session session, string shardId)
        {
            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var boundary = new CloudCustodianConfigurationBoundary(context);

                var current = boundary.GetCurrentAsync(shardId).GetAwaiter().GetResult();

                var lines = new System.Collections.Generic.List<string>
                {
                    $"Custodian configuration (version {current.Version.Value}):",
                    $"  Marketplace: {(current.MarketplaceEnabled ? "enabled" : "disabled")}",
                    $"  Mansions: {(current.MansionsEnabled ? "enabled" : "disabled")}",
                    $"  Custom positions ({current.CustomPositions.Count}):",
                };

                foreach (var position in current.CustomPositions)
                {
                    lines.Add($"    {position.Id}: {position.Position.Raw}");
                }

                CommandHandlerHelper.WriteOutputInfo(session, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: @custodian list failed.", ex);
                CommandHandlerHelper.WriteOutputInfo(session, $"@custodian list failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs one boundary mutation to completion, reports its outcome to the admin, and -- only
        /// on success -- triggers <see cref="CloudCustodianManager.ReapplyAsync"/> so the change
        /// hot-applies immediately (DEP-008). Blocking the calling thread for this DB round trip is
        /// acceptable here: admin configuration changes are rare, low-frequency actions, unlike the
        /// per-tick work the rest of the world thread performs.
        /// </summary>
        private static void RunAndReport(
            Session session,
            Func<CloudCustodianConfigurationBoundary, System.Threading.Tasks.Task<CloudBoundaryOutcome<CloudCustodianConfiguration>>> action,
            string successMessage)
        {
            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var boundary = new CloudCustodianConfigurationBoundary(context);

                var outcome = action(boundary).GetAwaiter().GetResult();

                if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
                {
                    CommandHandlerHelper.WriteOutputInfo(session, $"@custodian failed: {outcome.Reason}");
                    return;
                }

                CommandHandlerHelper.WriteOutputInfo(session, successMessage);

                _ = CloudCustodianManager.ReapplyAsync();
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: @custodian command failed.", ex);
                CommandHandlerHelper.WriteOutputInfo(session, $"@custodian failed: {ex.Message}");
            }
        }

        private static bool TryParseOnOff(string text, out bool enabled)
        {
            switch (text?.ToLowerInvariant())
            {
                case "on":
                case "true":
                case "enable":
                case "enabled":
                    enabled = true;
                    return true;

                case "off":
                case "false":
                case "disable":
                case "disabled":
                    enabled = false;
                    return true;

                default:
                    enabled = false;
                    return false;
            }
        }
    }
}
