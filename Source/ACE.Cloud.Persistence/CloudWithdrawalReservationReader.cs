using ACE.Cloud.Domain;

using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>See <see cref="ICloudWithdrawalReservationReader"/>'s doc comment for why this read lives outside <see cref="CloudCustodyBoundary"/>.</summary>
public sealed class CloudWithdrawalReservationReader : ICloudWithdrawalReservationReader
{
    private readonly CloudDbContext _context;

    public CloudWithdrawalReservationReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// <see cref="CloudReservationPolicy.Open"/> already guarantees at most one active Withdrawal
    /// Reservation can exist per target, but nothing in this schema forbids an owner from having
    /// several simultaneously active reservations over disjoint targets; this returns the most
    /// recently opened one, matching "what would the player expect to see right now."
    /// </summary>
    public async Task<CloudWithdrawalReservation?> TryGetActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Looking up a Withdrawal Reservation requires a real owner.", nameof(ownerId));
        }

        return await _context.CloudWithdrawalReservations.AsNoTracking()
            .Where(r => r.OwnerId == ownerId && r.Status == CloudReservationStatus.Active)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
