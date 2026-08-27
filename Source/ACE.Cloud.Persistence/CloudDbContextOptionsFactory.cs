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

        return new DbContextOptionsBuilder<CloudDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;
    }
}
