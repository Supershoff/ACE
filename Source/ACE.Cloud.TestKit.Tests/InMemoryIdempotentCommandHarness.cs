using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// One committed effect of <see cref="InMemoryIdempotentCommandHarness"/>: a fresh identity minted
/// the first time a given idempotency key is executed.
/// </summary>
public sealed record InMemoryEffect(Guid Id);

/// <summary>
/// A minimal, storage-agnostic reference implementation of
/// <see cref="ICloudIdempotentCommandHarness{TEffect}"/> that keeps its committed effects in
/// memory instead of a database. This is the proof that
/// <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> is reusable by an adapter that has
/// nothing to do with EF Core or MariaDB -- exactly what a future backend or worker adapter would
/// look like.
/// </summary>
public sealed class InMemoryIdempotentCommandHarness : ICloudIdempotentCommandHarness<InMemoryEffect>
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, InMemoryEffect> _committedByKey = [];

    public Task<InMemoryEffect> ExecuteAsync(Guid idempotencyKey)
    {
        lock (_gate)
        {
            if (_committedByKey.TryGetValue(idempotencyKey, out var existing))
            {
                return Task.FromResult(existing);
            }

            var effect = new InMemoryEffect(Guid.NewGuid());
            _committedByKey[idempotencyKey] = effect;
            return Task.FromResult(effect);
        }
    }

    public Task<int> CountCommittedEffectsAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_committedByKey.Values.Select(e => e.Id).Distinct().Count());
        }
    }

    public Guid IdentityOf(InMemoryEffect effect) => effect.Id;
}
