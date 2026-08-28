using System.Reflection;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Red -> Green test for issue #11's acceptance criterion "Privilege tests prove the authority
/// split rather than merely documenting it" and its Red section: "Test that... the ACE gateway
/// cannot perform Cloud-only marketplace ownership operations." <see cref="CloudCustodyBoundary"/>
/// is ACE's exclusive World Boundary Authority gateway (ARCH-002); the companion backend's own
/// Cloud Transaction Authority code, not this type, owns listings, bids, offers, and similar
/// off-world ownership transactions (ARCH-003). Enumerating this type's actual public method
/// surface -- rather than reading its doc comments -- means a future change that accidentally
/// grows a marketplace-shaped operation here fails this test immediately instead of only being
/// caught by someone re-reading the authority split documentation.
/// </summary>
[TestClass]
public sealed class CloudWorldBoundaryAuthoritySurfaceTests
{
    private static readonly HashSet<string> AllowedPublicMethodNames = new(StringComparer.Ordinal)
    {
        nameof(CloudCustodyBoundary.DepositAsync),
        nameof(CloudCustodyBoundary.TryGetDepositOutcomeAsync),
        nameof(CloudCustodyBoundary.WithdrawAsync),
        nameof(CloudCustodyBoundary.TryGetWithdrawalOutcomeAsync),
        nameof(CloudCustodyBoundary.DepositStackAsync),
        nameof(CloudCustodyBoundary.TryGetStackDepositOutcomeAsync),
        nameof(CloudCustodyBoundary.WithdrawLotAsync),
        nameof(CloudCustodyBoundary.TryGetLotWithdrawalOutcomeAsync),
        nameof(CloudCustodyBoundary.ReserveForWithdrawalAsync),
        nameof(CloudCustodyBoundary.TryGetWithdrawalReservationOutcomeAsync),
        nameof(CloudCustodyBoundary.CancelWithdrawalReservationAsync),
        nameof(CloudCustodyBoundary.TryGetActiveWithdrawalReservationAsync),
        nameof(CloudCustodyBoundary.RedeemWithdrawalReservationAsync),
        nameof(CloudCustodyBoundary.TryGetWithdrawalRedemptionOutcomeAsync),
    };

    /// <summary>
    /// Substrings that name a Cloud Transaction Authority concept (CONTEXT.md: "transact owners,
    /// reservations, offers, vault activity, bids, listings, and settlements"). "Reservation" is
    /// deliberately not listed: this gateway's own Withdrawal Reservation is a World Boundary
    /// Authority concept (WDR-001), not a marketplace one.
    /// </summary>
    private static readonly string[] ForbiddenMarketplaceAuthorityTerms =
    [
        "Listing", "Bid", "Auction", "Settle", "Vault", "Sharing", "Grant", "Currency", "Offer",
    ];

    [TestMethod]
    public void CloudCustodyBoundary_ExposesOnlyTheAllowListedWorldBoundaryAuthorityOperations()
    {
        var publicMethodNames = PublicMethodNames().ToHashSet(StringComparer.Ordinal);

        var unexpected = publicMethodNames.Except(AllowedPublicMethodNames).ToList();

        Assert.HasCount(
            0,
            unexpected,
            "ARCH-002/ARCH-003: CloudCustodyBoundary is ACE's exclusive World Boundary Authority gateway. It must never grow a "
                + $"Cloud-only marketplace/ownership-transaction operation ({string.Join(", ", unexpected)}); implement that in the "
                + $"companion backend's own Cloud Transaction Authority code instead, or extend {nameof(AllowedPublicMethodNames)} "
                + "here only after a deliberate authority-boundary review.");
    }

    [TestMethod]
    public void CloudCustodyBoundary_NeverExposesAMethodNamedAfterACloudOnlyMarketplaceOwnershipConcept()
    {
        foreach (var methodName in PublicMethodNames())
        {
            foreach (var term in ForbiddenMarketplaceAuthorityTerms)
            {
                Assert.IsFalse(
                    methodName.Contains(term, StringComparison.OrdinalIgnoreCase),
                    $"'{methodName}' on the ACE-side CloudCustodyBoundary gateway looks like a Cloud-only marketplace/ownership "
                        + $"operation ('{term}'); only the companion backend's Cloud Transaction Authority may implement those (ARCH-003).");
            }
        }
    }

    private static IEnumerable<string> PublicMethodNames() =>
        typeof(CloudCustodyBoundary)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);
}
