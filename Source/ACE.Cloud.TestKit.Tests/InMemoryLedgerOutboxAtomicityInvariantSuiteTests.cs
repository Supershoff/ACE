using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// Adopts <see cref="CloudLedgerOutboxAtomicityInvariantSuite"/> with zero test logic of its own.
/// See <see cref="InMemoryIdempotentCommandInvariantSuiteTests"/> for why this proves issue #10's
/// "adopt without copying" acceptance criterion.
/// </summary>
[TestClass]
public sealed class InMemoryLedgerOutboxAtomicityInvariantSuiteTests : CloudLedgerOutboxAtomicityInvariantSuite
{
    protected override ICloudLedgerOutboxAtomicityHarness CreateHarness() => new InMemoryLedgerOutboxAtomicityHarness();
}
