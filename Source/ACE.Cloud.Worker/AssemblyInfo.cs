using System.Runtime.CompilerServices;

// Exposes CloudAssetImportStagingWorker's internal DescribeExtractionFailure mapping (a pure test
// seam, AGENTS.md) to the test project that proves it never leaks an absolute operator path into
// the Activity Ledger (issue #25 acceptance criteria; code review finding on PR #123).
// No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Cloud.Worker.Tests")]
