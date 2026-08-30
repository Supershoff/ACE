using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned wire response for one <see cref="CloudInventorySearchRequest"/> (issue #32).
/// <see cref="Kind"/> mirrors <see cref="CloudSafeRegexSearchOutcomeKind"/> directly rather than a
/// parallel enum: <see cref="Page"/> is populated only for <c>Matched</c> (used for every completed
/// plain or Safe Regex Search), and every other value is one of the stable, actionable non-completion
/// outcomes issue #32's acceptance criteria require (a disabled/rate-limited/malformed/too-expensive
/// request, never a thrown exception or a hang). <see cref="AsOfCustodyOutboxSequenceNumber"/> is the
/// same projection-lag freshness signal <see cref="CloudInventoryQueryResponse"/> already reports (see
/// its doc comment). Never implements <see cref="ICloudPublicContract"/>: an inventory search is
/// always authorization-scoped, never a public surface.
/// </summary>
public sealed record CloudInventorySearchResponse(
    CloudSafeRegexSearchOutcomeKind Kind,
    CloudInventoryQueryPageResult? Page,
    string? Reason,
    long AsOfCustodyOutboxSequenceNumber);
