using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Runs one world-boundary handoff attempt with bounded retry on a genuine MariaDB deadlock or
/// lock-wait timeout (transaction rule 2 asks for deterministic lock ordering to avoid deadlocks in
/// the first place, but a boundary transaction can still lose a deadlock to an unrelated concurrent
/// transaction, e.g. an ordinary world-side inventory write racing the same biota). A deadlock or
/// lock-wait timeout always aborts the whole MariaDB transaction, so retrying means re-running the
/// entire attempt delegate, not resuming partway through.
///
/// Any other transient MySqlException (connection loss, unavailable server) is not retried here:
/// it is translated into an explicit <see cref="CloudBoundaryOutcomeKind.Unavailable"/> outcome so
/// the caller never queues a mutation for later replay (ARCH-009) and never infers success or
/// failure from an exception alone (transaction rule 8).
/// </summary>
public static class CloudBoundaryRetry
{
    public const int DefaultMaxAttempts = 4;

    private const int DeadlockErrorNumber = 1213; // ER_LOCK_DEADLOCK
    private const int LockWaitTimeoutErrorNumber = 1205; // ER_LOCK_WAIT_TIMEOUT

    public static async Task<CloudBoundaryOutcome<T>> ExecuteAsync<T>(
        Func<Task<CloudBoundaryOutcome<T>>> attempt,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");
        }

        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await attempt();
            }
            catch (Exception ex) when (IsDeadlockOrLockTimeout(ex) && attemptNumber < maxAttempts)
            {
                // The transaction already rolled back (MariaDB aborts it automatically on
                // deadlock; a lock-wait timeout is rolled back by the attempt's own `await using`
                // transaction disposal). Re-running `attempt` from the top is safe and, thanks to
                // idempotency keys, so is re-running a request whose earlier attempt actually did
                // reach MariaDB before losing the deadlock.
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                var mySqlException = UnwrapMySqlException(ex)!;
                return CloudBoundaryOutcome<T>.Unavailable(
                    $"The Cloud schema database is unavailable: {mySqlException.Message}");
            }
        }

        throw new InvalidOperationException(
            $"Exhausted {maxAttempts} attempts on repeated deadlocks/lock-wait timeouts. Repeated " +
            "contention on the same rows indicates a locking-order bug (transaction rule 2), not a " +
            "transient condition that more retries would resolve.");
    }

    private static bool IsDeadlockOrLockTimeout(Exception ex) =>
        UnwrapMySqlException(ex) is { Number: DeadlockErrorNumber or LockWaitTimeoutErrorNumber };

    private static bool IsUnavailable(Exception ex)
    {
        var mySqlException = UnwrapMySqlException(ex);
        return mySqlException is { IsTransient: true }
            && mySqlException.Number is not (DeadlockErrorNumber or LockWaitTimeoutErrorNumber);
    }

    private static MySqlException? UnwrapMySqlException(Exception ex) =>
        ex as MySqlException ?? (ex as DbUpdateException)?.InnerException as MySqlException;
}
