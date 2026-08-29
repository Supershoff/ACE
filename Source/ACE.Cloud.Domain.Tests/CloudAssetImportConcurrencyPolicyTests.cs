namespace ACE.Cloud.Domain.Tests;

/// <summary>ASSET-002 Red test: "concurrent imports".</summary>
[TestClass]
public sealed class CloudAssetImportConcurrencyPolicyTests
{
    [TestMethod]
    public void CanStartNewImport_NoExistingSession_IsTrue()
    {
        Assert.IsTrue(CloudAssetImportConcurrencyPolicy.CanStartNewImport(existingState: null));
    }

    [TestMethod]
    [DataRow(CloudAssetImportSessionState.Uploading)]
    [DataRow(CloudAssetImportSessionState.Staging)]
    public void CanStartNewImport_AnInFlightSessionExists_IsFalse(CloudAssetImportSessionState existingState)
    {
        Assert.IsFalse(CloudAssetImportConcurrencyPolicy.CanStartNewImport(existingState));
    }

    [TestMethod]
    [DataRow(CloudAssetImportSessionState.ChecksumFailed)]
    [DataRow(CloudAssetImportSessionState.StagingFailed)]
    [DataRow(CloudAssetImportSessionState.StagingComplete)]
    [DataRow(CloudAssetImportSessionState.Cancelled)]
    public void CanStartNewImport_APriorSessionIsTerminal_IsTrue(CloudAssetImportSessionState existingState)
    {
        Assert.IsTrue(CloudAssetImportConcurrencyPolicy.CanStartNewImport(existingState));
    }

    [TestMethod]
    public void CanResume_OnlyTheUploadingStateIsResumable()
    {
        Assert.IsTrue(CloudAssetImportConcurrencyPolicy.CanResume(CloudAssetImportSessionState.Uploading));
        Assert.IsFalse(CloudAssetImportConcurrencyPolicy.CanResume(CloudAssetImportSessionState.Staging));
        Assert.IsFalse(CloudAssetImportConcurrencyPolicy.CanResume(CloudAssetImportSessionState.StagingComplete));
    }
}
