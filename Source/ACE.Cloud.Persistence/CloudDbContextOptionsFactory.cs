using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Builds <see cref="CloudDbContext"/> options against a MariaDB connection string, exposing one
/// explicit, deterministic configuration seam for the Cloud schema (ARCH-012).
/// </summary>
public static class CloudDbContextOptionsFactory
{
    public static DbContextOptions<CloudDbContext> Create(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A Cloud schema connection string is required.", nameof(connectionString));
        }

        var serverVersion = ServerVersion.AutoDetect(connectionString);

        return Create(connectionString, serverVersion);
    }

    /// <summary>
    /// Builds options against an explicit <paramref name="serverVersion"/> instead of
    /// auto-detecting one by connecting. <see cref="ServerVersion.AutoDetect"/> requires a live
    /// connection at options-build time, which is unusable for a test or diagnostic that
    /// deliberately targets an unreachable server (for example proving MariaDB-unavailable
    /// behavior); this overload lets such a caller supply the version out of band.
    /// </summary>
    public static DbContextOptions<CloudDbContext> Create(string connectionString, ServerVersion serverVersion)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A Cloud schema connection string is required.", nameof(connectionString));
        }

        ArgumentNullException.ThrowIfNull(serverVersion);

        return new DbContextOptionsBuilder<CloudDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;
    }
}
