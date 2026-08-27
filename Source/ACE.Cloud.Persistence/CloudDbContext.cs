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

    public DbSet<CloudIdempotencyRecord> CloudIdempotencyRecords => Set<CloudIdempotencyRecord>();

    public DbSet<CloudActivityLedgerEvent> CloudActivityLedgerEvents => Set<CloudActivityLedgerEvent>();

    public DbSet<CloudCustodyOutboxEvent> CloudCustodyOutboxEvents => Set<CloudCustodyOutboxEvent>();

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

        modelBuilder.Entity<CloudIdempotencyRecord>(entity =>
        {
            entity.ToTable("CloudIdempotencyRecord");

            // ARCH-006 / transaction rule 4: the primary key is the idempotency key itself, so a
            // repeated request cannot insert a second row for the same key.
            entity.HasKey(record => record.IdempotencyKey);
            entity.Property(record => record.IdempotencyKey).ValueGeneratedNever();

            entity.Property(record => record.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(record => record.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(record => record.OperationType).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(record => record.BiotaId).IsRequired();
            entity.Property(record => record.OwnerId).IsRequired();

            // Not a foreign key: a withdrawal deletes its CloudCustodyRecord in the same
            // transaction that writes this row, so the referenced row legitimately may not exist.
            entity.Property(record => record.CustodyRecordId);

            entity.Property(record => record.TargetContainerId);
            entity.Property(record => record.CorrelationId).IsRequired();

            entity.Property(record => record.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudActivityLedgerEvent>(entity =>
        {
            entity.ToTable("CloudActivityLedgerEvent");

            entity.HasKey(evt => evt.Id);
            entity.Property(evt => evt.Id).ValueGeneratedNever();

            entity.Property(evt => evt.CorrelationId).IsRequired();
            entity.HasIndex(evt => evt.CorrelationId);

            entity.Property(evt => evt.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(evt => evt.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(evt => evt.EventType).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(evt => evt.BiotaId).IsRequired();
            entity.HasIndex(evt => evt.BiotaId);
            entity.Property(evt => evt.OwnerId).IsRequired();
            entity.Property(evt => evt.Outcome).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(evt => evt.Reason).HasMaxLength(512);

            // Database time (transaction rule 1), not application time.
            entity.Property(evt => evt.OccurredAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudCustodyOutboxEvent>(entity =>
        {
            entity.ToTable("CloudCustodyOutboxEvent");

            entity.HasKey(evt => evt.Id);
            entity.Property(evt => evt.Id).ValueGeneratedNever();

            entity.Property(evt => evt.CorrelationId).IsRequired();
            entity.HasIndex(evt => evt.CorrelationId);

            entity.Property(evt => evt.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(evt => evt.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(evt => evt.EventType).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(evt => evt.BiotaId).IsRequired();
            entity.Property(evt => evt.OwnerId).IsRequired();

            entity.Property(evt => evt.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
