namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// ASSET-002's "activate a completed manifest with one database transaction/pointer swap" and the
/// Red test "activation race": an older completed manifest must never be able to clobber a newer
/// one that already won.
/// </summary>
[TestClass]
public sealed class CloudAssetManifestActivationPolicyTests
{
    [TestMethod]
    public void Evaluate_CompleteNonEmptyManifestAndNoActiveManifestYet_IsApproved()
    {
        var decision = CloudAssetManifestActivationPolicy.Evaluate(
            CloudAssetManifestState.StagingComplete, manifestVersion: 1, entryCount: 5, currentActiveVersion: null);

        Assert.IsTrue(decision.IsApproved);
    }

    [TestMethod]
    public void Evaluate_NewerThanTheCurrentlyActiveManifest_IsApproved()
    {
        var decision = CloudAssetManifestActivationPolicy.Evaluate(
            CloudAssetManifestState.StagingComplete, manifestVersion: 2, entryCount: 5, currentActiveVersion: 1);

        Assert.IsTrue(decision.IsApproved);
    }

    [TestMethod]
    [DataRow(CloudAssetManifestState.Active)]
    [DataRow(CloudAssetManifestState.Superseded)]
    public void Evaluate_ManifestNotStagingComplete_IsRejected(CloudAssetManifestState state)
    {
        var decision = CloudAssetManifestActivationPolicy.Evaluate(state, manifestVersion: 1, entryCount: 5, currentActiveVersion: null);

        Assert.IsFalse(decision.IsApproved);
    }

    [TestMethod]
    public void Evaluate_AnEmptyManifest_IsRejected()
    {
        var decision = CloudAssetManifestActivationPolicy.Evaluate(
            CloudAssetManifestState.StagingComplete, manifestVersion: 1, entryCount: 0, currentActiveVersion: null);

        Assert.IsFalse(decision.IsApproved);
    }

    [TestMethod]
    public void Evaluate_AnOlderOrEqualVersionThanTheCurrentlyActiveOne_IsRejected()
    {
        // The activation race: a slower request for the already-superseded manifest version 1
        // arrives after version 2 has already won.
        var equalDecision = CloudAssetManifestActivationPolicy.Evaluate(
            CloudAssetManifestState.StagingComplete, manifestVersion: 2, entryCount: 5, currentActiveVersion: 2);
        var olderDecision = CloudAssetManifestActivationPolicy.Evaluate(
            CloudAssetManifestState.StagingComplete, manifestVersion: 1, entryCount: 5, currentActiveVersion: 2);

        Assert.IsFalse(equalDecision.IsApproved);
        Assert.IsFalse(olderDecision.IsApproved);
    }
}
