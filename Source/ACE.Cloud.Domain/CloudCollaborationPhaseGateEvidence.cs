namespace ACE.Cloud.Domain;

/// <summary>
/// One piece of evidence that a specific Phase 6 collaboration requirement ID (issue #39's
/// `XFER-001, XFER-002, SHARE-001..004, VAULT-001..005`) is actually proven somewhere -- a named
/// test, fixture, or acceptance scenario -- rather than merely asserted in prose. Mirrors
/// <see cref="CloudFidelityPhaseGateFixtureResult"/>'s shape (a requirement ID stands in for that
/// type's fixture category), so <see cref="CloudCollaborationPhaseGateReport"/> can apply the exact
/// same "a missing required category blocks the gate" discipline issue #28 established.
/// </summary>
public sealed record CloudCollaborationPhaseGateEvidence
{
    /// <summary>The exact requirement ID this evidence satisfies, e.g. <c>"XFER-002"</c> or <c>"VAULT-004"</c>.</summary>
    public required string RequirementId { get; init; }

    /// <summary>Which collaboration surface this evidence belongs to, for grouping and required-category coverage.</summary>
    public required CloudCollaborationPhaseGateCategory Category { get; init; }

    /// <summary>The fully qualified test, fixture, or acceptance method that proves this requirement, e.g. <c>"CloudTransferOfferGatewayTests.AcceptAndDecline_RacingConcurrently_ExactlyOneCommandWins"</c>.</summary>
    public required string Evidence { get; init; }

    /// <summary>One sentence describing what the evidence actually demonstrates, so the report reads without opening the referenced file.</summary>
    public required string Description { get; init; }
}

/// <summary>The three collaboration surfaces issue #39 gates: Transfer Offers, Sharing Grants, and the Allegiance Vault.</summary>
public enum CloudCollaborationPhaseGateCategory
{
    Offer,
    Sharing,
    Vault,
}
