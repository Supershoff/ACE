namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Red -> Green coverage for the code review finding on issue #25's acceptance criterion "No DAT,
/// extracted art, private capture, secret, or absolute operator path is committed or posted
/// publicly": <see cref="CloudAssetImportStagingWorker"/> must never turn a raw
/// <see cref="Exception.Message"/> from the extractor into the Activity Ledger's failure reason,
/// since <c>ACE.DatLoader</c>'s <c>FileNotFoundException(filePath)</c> (and similar I/O exceptions)
/// puts the absolute operator storage path verbatim into <c>.Message</c>.
/// </summary>
[TestClass]
public sealed class CloudAssetImportStagingWorkerTests
{
    [TestMethod]
    public void DescribeExtractionFailure_FileNotFoundExceptionWithAbsolutePath_DoesNotLeakThePath()
    {
        var absolutePath = "/var/lib/ace-cloud/asset-import/retained/us1/portal.dat";
        var ex = new FileNotFoundException(absolutePath);

        var description = CloudAssetImportStagingWorker.DescribeExtractionFailure(ex);

        Assert.IsFalse(description.Contains(absolutePath, StringComparison.Ordinal));
        Assert.IsFalse(description.Contains("/var/lib", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DescribeExtractionFailure_ExceptionWithPathEmbeddedInMessage_DoesNotLeakThePath()
    {
        var absolutePath = @"C:\ace-cloud\storage\retained\us1\portal.dat";
        var ex = new InvalidOperationException($"Failed reading {absolutePath}: corrupt header");

        var description = CloudAssetImportStagingWorker.DescribeExtractionFailure(ex);

        Assert.IsFalse(description.Contains(absolutePath, StringComparison.Ordinal));
        Assert.IsFalse(description.Contains(@"C:\ace-cloud", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DescribeExtractionFailure_ReturnsANonEmptyBoundedReason()
    {
        var ex = new InvalidOperationException("some internal detail nobody outside the worker log should see");

        var description = CloudAssetImportStagingWorker.DescribeExtractionFailure(ex);

        Assert.IsFalse(string.IsNullOrWhiteSpace(description));
        Assert.IsLessThanOrEqualTo(256, description.Length);
    }
}
