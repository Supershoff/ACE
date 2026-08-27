using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>A trivial mutable in-memory aggregate carrying only a version.</summary>
public sealed class InMemoryVersionedState
{
    private int _version = 1;

    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Atomically checks <paramref name="expectedVersion"/> against the current version and, if it
    /// matches, advances the version -- the in-memory equivalent of a locked row read-then-write.
    /// </summary>
    public bool TryAdvance(int expectedVersion)
    {
        lock (this)
        {
            if (_version != expectedVersion)
            {
                return false;
            }

            _version++;
            return true;
        }
    }
}

/// <summary>
/// A minimal, storage-agnostic reference implementation of
/// <see cref="ICloudOptimisticConflictHarness{TState}"/>.
/// </summary>
public sealed class InMemoryOptimisticConflictHarness : ICloudOptimisticConflictHarness<InMemoryVersionedState>
{
    public Task<InMemoryVersionedState> ArrangeAsync() => Task.FromResult(new InMemoryVersionedState());

    public int VersionOf(InMemoryVersionedState state) => state.Version;

    public Task<bool> TryMutateAsync(InMemoryVersionedState state, int expectedVersion) =>
        Task.FromResult(state.TryAdvance(expectedVersion));
}
