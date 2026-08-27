using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// Adopts <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> with zero test logic of its
/// own -- proving issue #10's acceptance criterion that adapter projects can adopt the shared
/// invariant suites without copying them. This adopter is in-memory; see
/// ACE.Cloud.PersistenceIntegrationTests for a second, MariaDB-backed adopter of the same suite
/// against the real <c>CloudCustodyBoundary</c>.
/// </summary>
[TestClass]
public sealed class InMemoryIdempotentCommandInvariantSuiteTests : CloudIdempotentCommandInvariantSuite<InMemoryEffect>
{
    protected override ICloudIdempotentCommandHarness<InMemoryEffect> CreateHarness() => new InMemoryIdempotentCommandHarness();
}
