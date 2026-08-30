using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's transaction boundary for Display Character selection
/// (AUTH-003): recomputes and persists one <see cref="CloudOwnershipGroup"/>'s current Display
/// Character pointer from an authoritative current-character candidate roster, and records an
/// immutable history snapshot of every change. The candidate roster itself is gathered by the
/// caller from ACE's authoritative character data (<c>ace_shard.character</c>, including
/// <c>total_Logins</c>) for every character across the group's Main and Linked Accounts -- this
/// class has no ACE-side character read of its own, matching the existing precedent that
/// <see cref="CloudIdentityOutboxEvent"/> carries only the character snapshot ACE already decided
/// to publish rather than this schema re-deriving it.
/// </summary>
public sealed class CloudDisplayCharacterGateway : ICloudDisplayCharacterReader
{
    private readonly CloudDbContext _context;

    public CloudDisplayCharacterGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Recomputes the group's Display Character via <see cref="CloudDisplayCharacterSelectionPolicy.SelectDefault"/>
    /// and persists the result, creating the pointer row on the group's first-ever selection.
    /// Always writes a <see cref="CloudDisplayCharacterSelectionHistoryEvent"/> snapshot, even when
    /// the winning candidate is unchanged, so history reflects every reselection trigger (a rename,
    /// deletion, link, or unlink) rather than only actual winner changes.
    /// </summary>
    public async Task<CloudDisplayCharacterSelectionResult> ReselectAsync(
        string shardId,
        Guid ownershipGroupId,
        IReadOnlyList<CloudDisplayCharacterCandidate> candidates,
        CloudDisplayCharacterSelectionReason reason,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reselecting a Display Character requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownershipGroupId == Guid.Empty)
        {
            throw new ArgumentException("Reselecting a Display Character requires an ownership group ID.", nameof(ownershipGroupId));
        }

        ArgumentNullException.ThrowIfNull(candidates);

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Reselecting a Display Character requires a correlation ID.", nameof(correlationId));
        }

        _context.ChangeTracker.Clear();

        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault(candidates);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _context.Set<CloudDisplayCharacterSelection>()
            .FromSqlInterpolated($"SELECT * FROM CloudDisplayCharacterSelection WHERE OwnershipGroupId = {ownershipGroupId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        if (existing is null)
        {
            _context.Add(CloudDisplayCharacterSelection.Create(ownershipGroupId, shardId, result, nowUtc));
        }
        else
        {
            existing.ReplaceWith(result, nowUtc);
            _context.Update(existing);
        }

        _context.Add(new CloudDisplayCharacterSelectionHistoryEvent(
            correlationId,
            shardId,
            ownershipGroupId,
            reason,
            result.HasSelection ? result.CharacterId : null,
            result.HasSelection ? result.CharacterName : null,
            result.HasSelection ? result.TotalLogins : null));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    /// <summary>Reads the group's current Display Character pointer, or null if it has never been selected.</summary>
    public async Task<CloudDisplayCharacterSelection?> GetCurrentSelectionAsync(Guid ownershipGroupId, CancellationToken cancellationToken = default) =>
        await _context.Set<CloudDisplayCharacterSelection>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.OwnershipGroupId == ownershipGroupId, cancellationToken);

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }
}
