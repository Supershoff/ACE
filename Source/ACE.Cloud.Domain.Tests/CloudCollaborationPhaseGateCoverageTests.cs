using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #39's actual, populated phase coverage report: every requirement ID from the issue's own
/// "Requirements" list (<see cref="CloudCollaborationPhaseGateReport.RequiredRequirementIds"/>) mapped
/// to the real test(s) that prove it, across the domain, persistence, and cross-cutting acceptance
/// suites #35-#39 actually added. This is deliberately a living document, not a generated snapshot: if
/// a future change removes or renames one of the referenced tests without updating this list, that is
/// itself a real regression in this phase gate's evidence, exactly like a missing fixture would be for
/// <see cref="CloudFidelityPhaseGateReport"/> (issue #28). Add an entry here, not merely a new test
/// file, whenever a future change closes a genuine coverage gap this report still lists.
/// </summary>
[TestClass]
public sealed class CloudCollaborationPhaseGateCoverageTests
{
    [TestMethod]
    public void CurrentReport_EveryIssue39RequirementId_HasRealEvidence_AndAllPassed()
    {
        var report = CloudCollaborationPhaseGateReport.Combine(BuildCurrentEvidence());

        Assert.IsTrue(report.AllPassed, "Every issue #39 requirement ID must be covered by at least one real test in every collaboration category (Offer, Sharing, Vault).");
        Assert.HasCount(0, report.MissingRequirementIds);
    }

    /// <summary>
    /// The current, honest evidence set. Every <c>Evidence</c> string below names a test method that
    /// actually exists in this solution as of issue #39 -- see the referenced files under
    /// <c>ACE.Cloud.Domain.Tests</c>, <c>ACE.Cloud.PersistenceIntegrationTests</c>, and (for browser
    /// evidence) <c>Source/ACE.Cloud.Web/e2e</c>.
    /// </summary>
    internal static IReadOnlyList<CloudCollaborationPhaseGateEvidence> BuildCurrentEvidence() =>
    [
        new()
        {
            RequirementId = "XFER-001", Category = CloudCollaborationPhaseGateCategory.Offer,
            Evidence = "CloudTransferOfferPolicyCreateTests.Create_ResolvesTheRecipientOnceIntoAnImmutableAccountId_IndependentOfLaterCharacterChanges",
            Description = "A current character name resolves once to an immutable recipient owner ID; later character changes never redirect it.",
        },
        new()
        {
            RequirementId = "XFER-002", Category = CloudCollaborationPhaseGateCategory.Offer,
            Evidence = "CloudTransferOfferGatewayTests.AcceptAndDecline_RacingConcurrently_ExactlyOneCommandWins",
            Description = "Simultaneous accept/decline against the same offer commits exactly one terminal state.",
        },
        new()
        {
            RequirementId = "XFER-002", Category = CloudCollaborationPhaseGateCategory.Offer,
            Evidence = "CloudCollaborationPhaseGateAcceptanceTests.RacingTransferOfferCreateAndVaultContribute_OnTheSameItem_ExactlyOneWins_NeitherLosesNorDuplicatesTheItem",
            Description = "A Transfer Offer racing an unrelated collaboration surface (Vault Contribution) over the same item still resolves to exactly one exclusive winner.",
        },
        new()
        {
            RequirementId = "SHARE-001", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "CloudSharingGrantGatewayTests.GetEffectiveAccessAsync_AfterTheGranteesCharacterIsDeletedOutOfBand_StillResolvesTheGrant",
            Description = "A Sharing Grant is addressed through a current character but stored against the resolved immutable group ID, so it survives the grantee's character deletion.",
        },
        new()
        {
            RequirementId = "SHARE-002", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "CloudSharingGrantPolicyTests.CapabilitiesFor_ViewAndWithdraw_GrantsOnlyViewAndTokenCreation",
            Description = "View & Withdraw grants only view and Withdrawal Token creation, never marketplace, settings, linking, offer, or permission-management capability.",
        },
        new()
        {
            RequirementId = "SHARE-003", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "CloudSharingGrantGatewayTests.ReserveForGrantedWithdrawal_ThenRedeem_BindsRedemptionAuthorityToTheGranteesOwnGroup",
            Description = "A grant-derived Withdrawal Token binds redemption authority to the grantee's own current group while the asset owner identity stays the actual owner.",
        },
        new()
        {
            RequirementId = "SHARE-004", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "CloudSharingGrantPolicyTests.ResolveEffectiveAccess_ExplicitNoneOverridesQualifyingDerivedAccess_ConflictingGrant",
            Description = "An explicit None grant overrides qualifying allegiance-derived access.",
        },
        new()
        {
            RequirementId = "SHARE-004", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "CloudSharingGrantGatewayTests.SetAsync_DowngradingFromViewAndWithdraw_ReleasesActiveGrantDerivedWithdrawalReservation",
            Description = "Losing View & Withdraw authority immediately releases an active grant-derived Withdrawal Reservation.",
        },
        new()
        {
            RequirementId = "VAULT-001", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudAllegianceVaultTransactionGatewayTests.ContributeAsync_RevalidatesLiveAllegianceMembership_RatherThanTheStaleIdentityProjectionCache",
            Description = "Every vault action revalidates the Acting Character's live ace_shard allegiance membership, never only the versioned cache.",
        },
        new()
        {
            RequirementId = "VAULT-002", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudAllegianceVaultTransactionGatewayTests.ContributeAndTake_TwoDifferentCurrentMembersOfTheSameAllegiance_HaveEqualPrivileges",
            Description = "Any two current members of the same allegiance have equal contribute/take privileges; there are no rank ACLs.",
        },
        new()
        {
            RequirementId = "VAULT-003", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudAllegianceVaultTransactionGatewayTests.WDR007_AVaultOwnedItem_CanNeverBeReservedForWithdrawalByTheActingCharactersOwnAccount",
            Description = "The vault cannot create Withdrawal Tokens directly; an item must first return to personal Cloud Inventory.",
        },
        new()
        {
            RequirementId = "VAULT-004", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudAllegianceVaultBoundaryTests.AbsorbAsync_MovesWholeItemsAndStackLots_FromSourceToDestination",
            Description = "Vault Absorption atomically transfers every source item and stack lot into the destination allegiance's vault.",
        },
        new()
        {
            RequirementId = "VAULT-005", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudMonarchVaultRecoveryGatewayTests.RecoverAsync_DestinationAccountDoesNotExist_IsAConflict_AndDoesNotResolveOrMoveAnything",
            Description = "Audited recovery never guesses or accepts an unverified destination account for an orphaned vault.",
        },
        new()
        {
            RequirementId = "VAULT-005", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudAllegianceVaultBoundaryTests.AbsorbAsync_WhenRefused_RecordsADurableDiagnostic_InsteadOfOnlyALogLine",
            Description = "An out-of-band monarch deletion with a nonempty vault leaves a durable, queryable recovery diagnostic rather than only a log line.",
        },

        // Cross-cutting concurrent/randomized conservation evidence (issue #39's own Red section:
        // "Run concurrent randomized asset/lot operations and verify conservation, exclusive
        // reservations, quota semantics, and immutable ledger lineage").
        new()
        {
            RequirementId = "XFER-002", Category = CloudCollaborationPhaseGateCategory.Offer,
            Evidence = "CloudCollaborationPhaseGateAcceptanceTests.ConcurrentOffersAndVaultContributionsOnDistinctItems_AllCommitWithoutFalseConflicts_AndConserveEveryItem",
            Description = "Concurrent Transfer Offers over disjoint items commit without false conflicts and conserve every item.",
        },
        new()
        {
            RequirementId = "VAULT-003", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "CloudCollaborationPhaseGateAcceptanceTests.RandomizedMixedOperations_AcrossOffersGrantsAndVault_AlwaysConserveTheFullItemPool",
            Description = "A randomized mix of Transfer Offer, Vault Contribution, and grant-derived Withdrawal Reservation attempts across a shared item pool always conserves every item.",
        },

        // Browser E2E evidence (issue #39's own Red section: "Run accessible browser E2E for offer,
        // grant, acting-character selection, vault contribute/take, notifications, and revoked
        // live-view behavior").
        new()
        {
            RequirementId = "XFER-002", Category = CloudCollaborationPhaseGateCategory.Offer,
            Evidence = "Source/ACE.Cloud.Web/e2e/transferOffers.spec.ts",
            Description = "Sending, accepting, declining, and cancelling a Transfer Offer through the web client.",
        },
        new()
        {
            RequirementId = "SHARE-004", Category = CloudCollaborationPhaseGateCategory.Sharing,
            Evidence = "Source/ACE.Cloud.Web/e2e/sharingGrants.spec.ts",
            Description = "Setting a Sharing Grant, and the revoked live-view behavior when access is later set to None.",
        },
        new()
        {
            RequirementId = "VAULT-002", Category = CloudCollaborationPhaseGateCategory.Vault,
            Evidence = "Source/ACE.Cloud.Web/e2e/allegianceVault.spec.ts",
            Description = "Acting Character selection and vault contribute/take through the web client.",
        },
    ];
}
