namespace ACE.Common
{
    /// <summary>
    /// AC Cloud Mule is an opt-in, self-hosted ACE extension (CONTEXT.md); this section is inert
    /// unless Enabled. ShardId is this deployment's immutable Cloud Shard ID (ARCH-001) and must
    /// match the CloudShardBinding row already bootstrapped in the ace_cloud schema this server
    /// connects to via MySql.Cloud.
    /// </summary>
    public class CloudMuleConfiguration
    {
        public bool Enabled { get; set; } = false;

        public string ShardId { get; set; } = "";

        /// <summary>
        /// An existing Vendor-type WeenieClassId in this server's ace_world database that Cloud
        /// Custodians spawn from (name/merchandise properties are overridden at spawn time; only the
        /// visual/model appearance is reused). Left at 0 until an operator configures it; Cloud
        /// Custodians do not spawn while it is unset.
        /// </summary>
        public uint CustodianBaseWeenieClassId { get; set; } = 0;

        /// <summary>
        /// ACE's own loopback/private-bound world-boundary liveness endpoint (ARCH-008). Companion
        /// services and the disposable local acceptance launcher poll this so a deposit/withdrawal
        /// path degrades gracefully -- rather than hanging -- while the ACE world process is offline.
        /// </summary>
        public CloudMuleHealthEndpointConfiguration HealthEndpoint { get; set; } = new CloudMuleHealthEndpointConfiguration();
    }
}
