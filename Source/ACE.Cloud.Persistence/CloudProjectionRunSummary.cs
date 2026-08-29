namespace ACE.Cloud.Persistence;

/// <summary>One projection consumer batch's outcome, for worker logging and health/recovery tooling.</summary>
public sealed record CloudProjectionRunSummary(int EventsRead, int EventsApplied, int EventsSkippedAsStale, int EventsDeadLettered)
{
    public bool CaughtUp => EventsRead == 0;
}
