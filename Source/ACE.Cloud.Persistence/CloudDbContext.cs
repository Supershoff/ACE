using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own MariaDB schema (ARCH-003, ARCH-012). Table shape is
/// applied by the versioned migrations under Migrations/, not by EnsureCreated/model diffing;
/// this OnModelCreating exists so the DbContext can query the same shape those migrations create.
/// </summary>
public sealed class CloudDbContext : DbContext
{
    public CloudDbContext(DbContextOptions<CloudDbContext> options)
        : base(options)
    {
    }

    public DbSet<CloudShardBinding> CloudShardBindings => Set<CloudShardBinding>();

    public DbSet<CloudCustodyRecord> CloudCustodyRecords => Set<CloudCustodyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CloudShardBinding>(entity =>
        {
            // ARCH-001: one Cloud Mule deployment serves exactly one immutable Cloud Shard ID,
            // so this table may never hold more than its single Id = 1 row.
            entity.ToTable("CloudShardBinding", table =>
                table.HasCheckConstraint("CK_CloudShardBinding_Singleton", "`Id` = 1"));

            entity.HasKey(binding => binding.Id);
            entity.Property(binding => binding.Id).ValueGeneratedNever();

            entity.Property(binding => binding.ShardId).IsRequired().HasMaxLength(64);
            entity.HasIndex(binding => binding.ShardId).IsUnique();

            entity.Property(binding => binding.SchemaVersion).IsRequired().HasMaxLength(32);
            entity.Property(binding => binding.AceExtensionVersion).IsRequired().HasMaxLength(32);
            entity.Property(binding => binding.ContractProtocolVersion).IsRequired().HasMaxLength(32);

            entity.Property(binding => binding.AppliedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudCustodyRecord>(entity =>
        {
            entity.ToTable("CloudCustodyRecord");

            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).ValueGeneratedNever();

            // INV-001 / duplicate-custody rejection: at most one custody record per native biota.
            entity.Property(record => record.BiotaId).IsRequired();
            entity.HasIndex(record => record.BiotaId).IsUnique();

            // Cross-shard ownership rejection: ShardId must match this deployment's singleton
            // Cloud Shard binding (ARCH-001).
            entity.Property(record => record.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(record => record.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(record => record.OwnerId).IsRequired();
            entity.Property(record => record.LedgerCorrelationId).IsRequired();

            entity.Property(record => record.Version).IsRequired().IsConcurrencyToken();

            entity.Property(record => record.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(record => record.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
