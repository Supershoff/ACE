namespace ACE.Server.Managers
{
    /// <summary>
    /// The read-only diagnostic <see cref="CloudCustodianManager.GetDepositReadinessAsync"/> reports
    /// over ACE's own world-boundary liveness endpoint (issue #34's blocking defect #5): every
    /// remaining prerequisite an operator needs to reach an actual Cloud Custodian deposit, so the
    /// disposable local acceptance launcher can give an actionable diagnostic before starting the web
    /// stack instead of leaving the operator to discover a silent no-op spawn. Never exposes anything
    /// beyond configuration/diagnostic facts already visible to the operator running this ACE process.
    /// </summary>
    public sealed class CloudMuleDepositReadinessReport
    {
        public bool CloudMuleEnabled { get; init; }

        public string ShardId { get; init; } = "";

        public string ShardBindingStatus { get; init; } = "";

        public string ShardBindingDetail { get; init; } = "";

        public bool CustodianWeenieConfigured { get; init; }

        public uint CustodianWeenieClassId { get; init; }

        public bool CustodianWeenieFound { get; init; }

        public bool CustodianWeenieIsVendorType { get; init; }

        public int ResolvedCustodianLocationCount { get; init; }

        public bool Ready { get; init; }

        public string Reason { get; init; } = "";

        public static CloudMuleDepositReadinessReport Disabled() => new()
        {
            CloudMuleEnabled = false,
            ShardBindingStatus = "NotChecked",
            Ready = false,
            Reason = "CloudMule.Enabled is false in Config.js.",
        };
    }
}
