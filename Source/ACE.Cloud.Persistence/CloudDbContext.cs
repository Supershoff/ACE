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

    public DbSet<CloudStackLot> CloudStackLots => Set<CloudStackLot>();

    public DbSet<CloudStackLotLineageEvent> CloudStackLotLineageEvents => Set<CloudStackLotLineageEvent>();

    public DbSet<CloudCustodyOutboxSequence> CloudCustodyOutboxSequences => Set<CloudCustodyOutboxSequence>();

    public DbSet<CloudWithdrawalReservation> CloudWithdrawalReservations => Set<CloudWithdrawalReservation>();

    public DbSet<CloudCustodianConfigurationRecord> CloudCustodianConfigurations => Set<CloudCustodianConfigurationRecord>();

    public DbSet<CloudCustodianCustomPositionRecord> CloudCustodianCustomPositions => Set<CloudCustodianCustomPositionRecord>();

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

            // OwnerId is required for a non-stack record and null for a stack record; TotalQuantity
            // is the reverse. CK_CloudCustodyRecord_OwnerXorStack (added by the AddCloudStackLots
            // migration) enforces that exclusivity at the database level.
            entity.Property(record => record.OwnerId);
            entity.Property(record => record.TotalQuantity);
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

            // Application-assigned within the same transaction via CloudCustodyOutboxSequence, not
            // database-generated, so EF must send the value this app computed rather than omit it.
            entity.Property(evt => evt.SequenceNumber).IsRequired().ValueGeneratedNever();
            entity.HasIndex(evt => evt.SequenceNumber).IsUnique();

            entity.Property(evt => evt.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudCustodyOutboxSequence>(entity =>
        {
            // One counter row per deployment (ARCH-001), the same singleton shape as
            // CloudShardBinding: CK_CloudCustodyOutboxSequence_Singleton keeps this table at exactly
            // one row so every writer reserves its next sequence number from the same place.
            entity.ToTable("CloudCustodyOutboxSequence", table =>
                table.HasCheckConstraint("CK_CloudCustodyOutboxSequence_Singleton", "`Id` = 1"));

            entity.HasKey(seq => seq.Id);
            entity.Property(seq => seq.Id).ValueGeneratedNever();
            entity.Property(seq => seq.NextValue).IsRequired();
        });

        modelBuilder.Entity<CloudWithdrawalReservation>(entity =>
        {
            entity.ToTable("CloudWithdrawalReservation");

            entity.HasKey(reservation => reservation.Id);
            entity.Property(reservation => reservation.Id).ValueGeneratedNever();

            entity.Property(reservation => reservation.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(reservation => reservation.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            // WDR-001/INV-001: at most one active reservation may target the same biota at a time.
            // The Cloud Custody Record row for BiotaId is locked (FOR UPDATE) for the whole opening
            // transaction (CloudCustodyBoundary.ReserveForWithdrawalAsync), so concurrent opens for
            // the same biota already serialize on that lock; this index exists for lookup, not as
            // the sole enforcement mechanism.
            entity.Property(reservation => reservation.BiotaId).IsRequired();
            entity.HasIndex(reservation => reservation.BiotaId);

            entity.Property(reservation => reservation.OwnerId).IsRequired();

            // Not a foreign key: CustodyRecordId is intentionally not stored here (looked up by
            // BiotaId at redemption time instead), matching CloudIdempotencyRecord's precedent that
            // a withdrawal can legitimately delete the row a reservation once referenced.
            entity.Property(reservation => reservation.TokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(reservation => reservation.TokenHash).IsUnique();

            entity.Property(reservation => reservation.OpenIdempotencyKey).IsRequired();
            entity.HasIndex(reservation => reservation.OpenIdempotencyKey).IsUnique();

            entity.Property(reservation => reservation.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(reservation => reservation.ReleaseReason).HasConversion<string>().HasMaxLength(32);

            entity.Property(reservation => reservation.Version).IsRequired();

            entity.Property(reservation => reservation.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(reservation => reservation.ExpiresAtUtc).IsRequired();
            entity.Property(reservation => reservation.ReleasedAtUtc);
        });

        modelBuilder.Entity<CloudStackLot>(entity =>
        {
            entity.ToTable("CloudStackLot");

            entity.HasKey(lot => lot.Id);
            entity.Property(lot => lot.Id).ValueGeneratedNever();

            // INV-001: every lot belongs to exactly one backing stack Cloud Custody Record. Not
            // cascading on delete: a CloudCustodyRecord is only ever deleted once its last lot is
            // also deleted in the same transaction (CloudCustodyBoundary.WithdrawLotAsync's
            // full-stack-withdrawal case), never independently of its lots.
            entity.Property(lot => lot.CustodyRecordId).IsRequired();
            entity.HasIndex(lot => lot.CustodyRecordId);
            entity.HasOne<CloudCustodyRecord>()
                .WithMany()
                .HasForeignKey(lot => lot.CustodyRecordId)
                .HasPrincipalKey(record => record.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(lot => lot.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(lot => lot.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(lot => lot.OwnerId).IsRequired();
            entity.Property(lot => lot.Quantity).IsRequired();
            entity.Property(lot => lot.Version).IsRequired();

            entity.Property(lot => lot.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(lot => lot.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudStackLotLineageEvent>(entity =>
        {
            entity.ToTable("CloudStackLotLineageEvent");

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

            entity.Property(evt => evt.ParentBiotaId).IsRequired();
            entity.Property(evt => evt.ChildBiotaId).IsRequired();
            entity.Property(evt => evt.Quantity).IsRequired();
            entity.Property(evt => evt.OwnerId).IsRequired();

            entity.Property(evt => evt.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudCustodianConfigurationRecord>(entity =>
        {
            // ARCH-001: exactly one Custodian configuration row per deployment, the same singleton
            // shape as CloudShardBinding.
            entity.ToTable("CloudCustodianConfiguration", table =>
                table.HasCheckConstraint("CK_CloudCustodianConfiguration_Singleton", "`Id` = 1"));

            entity.HasKey(config => config.Id);
            entity.Property(config => config.Id).ValueGeneratedNever();

            entity.Property(config => config.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(config => config.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(config => config.MarketplaceEnabled).IsRequired();
            entity.Property(config => config.MansionsEnabled).IsRequired();
            entity.Property(config => config.Version).IsRequired().IsConcurrencyToken();

            entity.Property(config => config.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudCustodianCustomPositionRecord>(entity =>
        {
            entity.ToTable("CloudCustodianCustomPosition");

            entity.HasKey(position => position.Id);
            entity.Property(position => position.Id).ValueGeneratedNever();

            entity.Property(position => position.ShardId).IsRequired().HasMaxLength(64);
            entity.HasIndex(position => position.ShardId);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(position => position.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(position => position.PositionRaw).IsRequired().HasMaxLength(255);

            entity.Property(position => position.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
