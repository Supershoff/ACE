namespace ACE.Cloud.Domain;

/// <summary>One page of an Activity Ledger query (EVT-001's "pagination" Red test).</summary>
public sealed record CloudActivityLedgerPage(
    IReadOnlyList<CloudActivityLedgerEntry> Entries,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);
