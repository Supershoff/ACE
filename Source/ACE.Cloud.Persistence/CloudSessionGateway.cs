using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud backend's own transaction boundary for exchanging a verified ACE Auth Bridge grant for
/// a secure web session, and for revoking/rotating sessions (AUTH-002). Distinct from
/// <see cref="CloudCustodyBoundary"/> (ACE's World Boundary Authority gateway): this class is Cloud
/// Transaction Authority code, callable only by the Cloud backend, and never touches a native biota.
/// </summary>
public sealed class CloudSessionGateway : ICloudWebSessionStore
{
    private readonly CloudDbContext _context;

    public CloudSessionGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Records the grant's nonce as consumed and opens a new session, atomically. A repeated
    /// exchange attempt for the same nonce (a replayed grant) fails with
    /// <see cref="CloudSessionExchangeOutcomeKind.GrantAlreadyUsed"/> instead of creating a second
    /// session -- the unique constraint on <see cref="CloudAuthGrantConsumption.Nonce"/> is the
    /// actual enforcement; this call only classifies that failure for the caller.
    /// </summary>
    public async Task<CloudSessionExchangeResult> ExchangeGrantForSessionAsync(
        string shardId,
        uint accountId,
        Guid grantNonce,
        string secretHash,
        string csrfToken,
        DateTime nowUtc,
        TimeSpan sessionTimeToLive,
        CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.CloudAuthGrantConsumptions.Add(new CloudAuthGrantConsumption(grantNonce, accountId, nowUtc));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudSessionExchangeResult.GrantAlreadyUsed();
        }

        var session = CloudWebSession.Open(shardId, accountId, secretHash, csrfToken, nowUtc, nowUtc + sessionTimeToLive);
        _context.CloudWebSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return CloudSessionExchangeResult.Created(session);
    }

    /// <summary>Looks up a session by its secret hash and touches <c>LastSeenAtUtc</c>; returns null if it does not exist or is no longer active.</summary>
    public async Task<CloudWebSession?> TryGetActiveSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretHash))
        {
            throw new ArgumentException("A session secret hash is required.", nameof(secretHash));
        }

        var session = await _context.CloudWebSessions.SingleOrDefaultAsync(s => s.SecretHash == secretHash, cancellationToken);
        if (session is null || !session.IsActiveAt(nowUtc))
        {
            return null;
        }

        session.Touch(nowUtc);
        await _context.SaveChangesAsync(cancellationToken);

        return session;
    }

    /// <summary>Idempotent: revoking an already-revoked (or nonexistent) session is a no-op success.</summary>
    public async Task RevokeSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretHash))
        {
            throw new ArgumentException("A session secret hash is required.", nameof(secretHash));
        }

        var session = await _context.CloudWebSessions.SingleOrDefaultAsync(s => s.SecretHash == secretHash, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return;
        }

        session.Revoke(nowUtc);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ends the currently active session for <paramref name="currentSecretHash"/> and opens a
    /// replacement in one transaction (session rotation), preventing session fixation across a
    /// privilege-relevant event without forcing the user to re-authenticate through the Auth Bridge
    /// again. Returns null if <paramref name="currentSecretHash"/> does not currently name an active
    /// session.
    /// </summary>
    public async Task<CloudWebSession?> RotateSessionAsync(
        string currentSecretHash,
        string newSecretHash,
        string newCsrfToken,
        DateTime nowUtc,
        TimeSpan sessionTimeToLive,
        CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var current = await _context.CloudWebSessions.SingleOrDefaultAsync(s => s.SecretHash == currentSecretHash, cancellationToken);
        if (current is null || !current.IsActiveAt(nowUtc))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        current.Revoke(nowUtc);

        var next = CloudWebSession.Open(
            current.ShardId, current.AccountId, newSecretHash, newCsrfToken, nowUtc, nowUtc + sessionTimeToLive, rotatedFromSessionId: current.Id);
        _context.CloudWebSessions.Add(next);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return next;
    }
}
