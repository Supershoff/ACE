namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #38, VAULT-005/ADM-002: pure preconditions for an audited administrator
/// Allegiance Vault recovery. The actual item-by-item transfer and diagnostic-resolution locking are
/// proved at the persistence layer (they need real rows to enumerate and lock); this covers only the
/// pure authorization/gate/request-shape preconditions every recovery attempt must satisfy first.
/// </summary>
[TestClass]
public sealed class CloudMonarchVaultRecoveryPolicyTests
{
    private static readonly Guid SourceVaultOwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DestinationOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CloudMonarchVaultRecoveryRequest ValidRequest() => new(
        AdminAuthorized: true,
        GateState: CloudMutationGateState.Open,
        DiagnosticFound: true,
        AlreadyResolved: false,
        Reason: "Monarch character was deleted directly against the database outside ACE's guarded path; reassigning to the guild's designated successor account.",
        Confirmed: true,
        SourceVaultOwnerId: SourceVaultOwnerId,
        DestinationOwnerId: DestinationOwnerId);

    [TestMethod]
    public void Authorize_EveryPreconditionSatisfied_Succeeds()
    {
        var result = CloudMonarchVaultRecoveryPolicy.Authorize(ValidRequest());

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Authorize_WithoutFreshAdminAuthorization_Fails()
    {
        var request = ValidRequest() with { AdminAuthorized = false };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.Unauthorized, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_WhenMutationsAreFrozen_Fails()
    {
        var request = ValidRequest() with { GateState = CloudMutationGateState.Frozen };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.MutationsFrozen, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_WhenNoDiagnosticMatches_Fails()
    {
        var request = ValidRequest() with { DiagnosticFound = false };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.DiagnosticNotFound, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_WhenAlreadyResolved_Fails_AndCanNeverOverrideACommittedTransfer()
    {
        var request = ValidRequest() with { AlreadyResolved = true };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.AlreadyResolved, result.RejectionCode);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Authorize_WithoutAWrittenReason_Fails(string? blankReason)
    {
        var request = ValidRequest() with { Reason = blankReason };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.ReasonRequired, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_WithoutExplicitConfirmation_Fails()
    {
        var request = ValidRequest() with { Confirmed = false };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.NotConfirmed, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_DestinationSameAsOrphanedVault_Fails_NeverGuessesTheVaultAsItsOwnSuccessor()
    {
        var request = ValidRequest() with { DestinationOwnerId = SourceVaultOwnerId };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.InvalidDestination, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_EmptyDestination_Fails()
    {
        var request = ValidRequest() with { DestinationOwnerId = Guid.Empty };

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.InvalidDestination, result.RejectionCode);
    }

    [TestMethod]
    public void Authorize_ChecksAuthorizationBeforeLeakingAnyOtherState()
    {
        // Even when every other precondition is also violated, an unauthorized caller must only
        // ever learn "unauthorized" -- never that a diagnostic exists, is already resolved, or any
        // other fact about this vault.
        var request = new CloudMonarchVaultRecoveryRequest(
            AdminAuthorized: false,
            GateState: CloudMutationGateState.Frozen,
            DiagnosticFound: false,
            AlreadyResolved: true,
            Reason: null,
            Confirmed: false,
            SourceVaultOwnerId: SourceVaultOwnerId,
            DestinationOwnerId: SourceVaultOwnerId);

        var result = CloudMonarchVaultRecoveryPolicy.Authorize(request);

        Assert.AreEqual(CloudMonarchVaultRecoveryRejectionCode.Unauthorized, result.RejectionCode);
    }
}
