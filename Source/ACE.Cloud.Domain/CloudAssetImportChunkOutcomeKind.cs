namespace ACE.Cloud.Domain;

/// <summary>The result of evaluating one uploaded chunk against a session's plan and prior chunks.</summary>
public enum CloudAssetImportChunkOutcomeKind
{
    /// <summary>A new chunk, not previously recorded, matching the plan.</summary>
    Accepted,

    /// <summary>
    /// A resend of a chunk index already recorded with an identical checksum -- the normal shape of
    /// a resumed upload retrying a chunk whose acknowledgement was lost (ASSET-002:
    /// "interrupted/resumed upload", "duplicate chunks"). Idempotent: applying it again changes
    /// nothing.
    /// </summary>
    DuplicateIgnored,

    /// <summary>The chunk is out of range, wrong-sized, oversized, conflicts with a previously
    /// recorded chunk at the same index, or the session cannot accept chunks right now.</summary>
    Rejected,
}
