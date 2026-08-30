using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned wire response for one <see cref="CloudInventoryQueryRequest"/> (issue #30: "Expose a
/// versioned inventory read API/projection"). <see cref="Page"/> carries the categorized/sorted/paged
/// item rows themselves (<see cref="CloudInventoryQueryPageResult"/>, already authorization-scoped and
/// leak-free by construction). <see cref="AsOfCustodyOutboxSequenceNumber"/> is this response's
/// freshness/lag signal (issue #30 Red: "projection lag ... responses"): the highest Custody Outbox
/// sequence number this read model had durably applied when the query ran, taken from the same
/// <c>CloudProjectionCheckpoint</c> row the custody consumer already advances (ARCH-007), so a client
/// can compare it against a Live State Stream cursor to detect "this page may not yet reflect the very
/// latest committed deposit/withdrawal" without the read model needing any new bookkeeping of its own.
/// Never implements <see cref="ICloudPublicContract"/>: an inventory query is always
/// authorization-scoped, never a public surface.
/// </summary>
public sealed record CloudInventoryQueryResponse(CloudInventoryQueryPageResult Page, long AsOfCustodyOutboxSequenceNumber);
