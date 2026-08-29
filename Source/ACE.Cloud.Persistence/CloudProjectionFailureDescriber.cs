namespace ACE.Cloud.Persistence;

/// <summary>
/// Maps a poison-event exception to a bounded, operator-safe reason for
/// <see cref="CloudProjectionDeadLetter.Reason"/>. <c>ex.Message</c> must never be persisted
/// directly: some .NET I/O exceptions embed absolute filesystem paths or connection detail in
/// <c>.Message</c>, which would otherwise violate AGENTS.md's "no absolute operator path is
/// committed" rule now that dead-letter diagnostics are a durable, admin-visible table -- the same
/// concern <c>CloudAssetImportStagingWorker.DescribeExtractionFailure</c> already guards against for
/// Asset Import failures. The full exception is still available to server-side structured logs; this
/// redacted string is the only thing that reaches the database.
/// </summary>
public static class CloudProjectionFailureDescriber
{
    public static string Describe(Exception ex) => ex switch
    {
        ArgumentException => "The event's payload failed validation and could not be applied to its projection.",
        InvalidOperationException => "The event could not be applied due to an unexpected projection state.",
        _ => "Applying this event to its projection failed; see worker logs for details.",
    };
}
