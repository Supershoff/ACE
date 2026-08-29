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

    public DbSet<CloudWithdrawalReservationTarget> CloudWithdrawalReservationTargets => Set<CloudWithdrawalReservationTarget>();

    public DbSet<CloudWithdrawalRedemptionDeliveryItem> CloudWithdrawalRedemptionDeliveryItems => Set<CloudWithdrawalRedemptionDeliveryItem>();

    public DbSet<CloudCustodianConfigurationRecord> CloudCustodianConfigurations => Set<CloudCustodianConfigurationRecord>();

    public DbSet<CloudCustodianCustomPositionRecord> CloudCustodianCustomPositions => Set<CloudCustodianCustomPositionRecord>();

    public DbSet<CloudFrozenEnchantment> CloudFrozenEnchantments => Set<CloudFrozenEnchantment>();

    public DbSet<CloudPyrealRemainder> CloudPyrealRemainders => Set<CloudPyrealRemainder>();

    public DbSet<CloudPyrealConversionRecord> CloudPyrealConversionRecords => Set<CloudPyrealConversionRecord>();

    public DbSet<CloudPyrealConversionMmd> CloudPyrealConversionMmds => Set<CloudPyrealConversionMmd>();

    public DbSet<CloudPyrealRemainderWithdrawalRecord> CloudPyrealRemainderWithdrawalRecords => Set<CloudPyrealRemainderWithdrawalRecord>();

    public DbSet<CloudPyrealRemainderWithdrawalBiota> CloudPyrealRemainderWithdrawalBiotas => Set<CloudPyrealRemainderWithdrawalBiota>();

    public DbSet<CloudWithdrawalLocationConfigurationRecord> CloudWithdrawalLocationConfigurations => Set<CloudWithdrawalLocationConfigurationRecord>();

    public DbSet<CloudWithdrawalNamedLandblockRecord> CloudWithdrawalNamedLandblocks => Set<CloudWithdrawalNamedLandblockRecord>();

    public DbSet<CloudIdentityOutboxEvent> CloudIdentityOutboxEvents => Set<CloudIdentityOutboxEvent>();

    public DbSet<CloudIdentityOutboxSequence> CloudIdentityOutboxSequences => Set<CloudIdentityOutboxSequence>();

    public DbSet<CloudAllegianceVaultBinding> CloudAllegianceVaultBindings => Set<CloudAllegianceVaultBinding>();

    public DbSet<CloudMonarchDeletionDiagnostic> CloudMonarchDeletionDiagnostics => Set<CloudMonarchDeletionDiagnostic>();

    public DbSet<CloudAuthGrantConsumption> CloudAuthGrantConsumptions => Set<CloudAuthGrantConsumption>();

    public DbSet<CloudWebSession> CloudWebSessions => Set<CloudWebSession>();

    public DbSet<CloudOwnershipGroup> CloudOwnershipGroups => Set<CloudOwnershipGroup>();

    public DbSet<CloudAccountLink> CloudAccountLinks => Set<CloudAccountLink>();

    public DbSet<CloudActiveAccountLinkMarker> CloudActiveAccountLinkMarkers => Set<CloudActiveAccountLinkMarker>();

    public DbSet<CloudAccountLinkIdempotencyRecord> CloudAccountLinkIdempotencyRecords => Set<CloudAccountLinkIdempotencyRecord>();

    public DbSet<CloudAccountLinkLedgerEvent> CloudAccountLinkLedgerEvents => Set<CloudAccountLinkLedgerEvent>();

    public DbSet<CloudDisplayCharacterSelection> CloudDisplayCharacterSelections => Set<CloudDisplayCharacterSelection>();

    public DbSet<CloudDisplayCharacterSelectionHistoryEvent> CloudDisplayCharacterSelectionHistoryEvents => Set<CloudDisplayCharacterSelectionHistoryEvent>();

    public DbSet<CloudAssetImportSession> CloudAssetImportSessions => Set<CloudAssetImportSession>();

    public DbSet<CloudAssetImportChunkRecord> CloudAssetImportChunkRecords => Set<CloudAssetImportChunkRecord>();

    public DbSet<CloudAssetImportCurrentSessionMarker> CloudAssetImportCurrentSessionMarkers => Set<CloudAssetImportCurrentSessionMarker>();

    public DbSet<CloudAssetManifest> CloudAssetManifests => Set<CloudAssetManifest>();

    public DbSet<CloudAssetManifestEntryRecord> CloudAssetManifestEntryRecords => Set<CloudAssetManifestEntryRecord>();

    public DbSet<CloudActiveAssetManifest> CloudActiveAssetManifests => Set<CloudActiveAssetManifest>();

    public DbSet<CloudRetainedSourceAsset> CloudRetainedSourceAssets => Set<CloudRetainedSourceAsset>();

    public DbSet<CloudAssetImportLedgerEvent> CloudAssetImportLedgerEvents => Set<CloudAssetImportLedgerEvent>();

    public DbSet<CloudIconDiagnostic> CloudIconDiagnostics => Set<CloudIconDiagnostic>();

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

            entity.Property(reservation => reservation.OwnerId).IsRequired();

            // Issue #122: exactly one shared unique index across every target kind, closing the gap
            // where two independent per-target-type tables previously let the same token secret
            // address two different, independently consumable reservations at once.
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

        modelBuilder.Entity<CloudWithdrawalReservationTarget>(entity =>
        {
            entity.ToTable("CloudWithdrawalReservationTarget");

            entity.HasKey(target => target.Id);
            entity.Property(target => target.Id).ValueGeneratedNever();

            // WDR-001/INV-001: at most one active target may claim the same biota/lot at a time. The
            // relevant Cloud Custody Record and/or Cloud Stack Lot row is locked (FOR UPDATE) for the
            // whole opening transaction (CloudCustodyBoundary.ReserveForWithdrawalAsync), so
            // concurrent opens for the same target already serialize on that lock; these indexes
            // exist for lookup, not as the sole enforcement mechanism.
            entity.Property(target => target.ReservationId).IsRequired();
            entity.HasIndex(target => target.ReservationId);
            entity.HasOne<CloudWithdrawalReservation>()
                .WithMany()
                .HasForeignKey(target => target.ReservationId)
                .HasPrincipalKey(reservation => reservation.Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(target => target.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(target => target.ItemBiotaId);
            entity.HasIndex(target => target.ItemBiotaId);
            entity.Property(target => target.StackLotId);
            entity.HasIndex(target => target.StackLotId);
            entity.Property(target => target.Quantity);
        });

        modelBuilder.Entity<CloudWithdrawalRedemptionDeliveryItem>(entity =>
        {
            entity.ToTable("CloudWithdrawalRedemptionDeliveryItem");

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();

            entity.Property(item => item.RedemptionIdempotencyKey).IsRequired();
            entity.HasIndex(item => item.RedemptionIdempotencyKey);
            entity.HasOne<CloudIdempotencyRecord>()
                .WithMany()
                .HasForeignKey(item => item.RedemptionIdempotencyKey)
                .HasPrincipalKey(record => record.IdempotencyKey)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(item => item.OrdinalPosition).IsRequired();
            entity.Property(item => item.DeliveredBiotaId).IsRequired();
            entity.Property(item => item.Quantity);
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

        modelBuilder.Entity<CloudFrozenEnchantment>(entity =>
        {
            entity.ToTable("CloudFrozenEnchantment");

            entity.HasKey(frozen => frozen.Id);
            entity.Property(frozen => frozen.Id).ValueGeneratedNever();

            entity.Property(frozen => frozen.CustodyRecordId).IsRequired();
            entity.HasIndex(frozen => frozen.CustodyRecordId);
            entity.HasOne<CloudCustodyRecord>()
                .WithMany()
                .HasForeignKey(frozen => frozen.CustodyRecordId)
                .HasPrincipalKey(record => record.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(frozen => frozen.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(frozen => frozen.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(frozen => frozen.SpellId).IsRequired();
            entity.Property(frozen => frozen.LayerId).IsRequired();
            entity.Property(frozen => frozen.RemainingDurationSeconds).IsRequired();

            entity.Property(frozen => frozen.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudPyrealRemainder>(entity =>
        {
            entity.ToTable("CloudPyrealRemainder");

            // One remainder row per account per shard (DEP-006); no surrogate Id.
            entity.HasKey(remainder => new { remainder.OwnerId, remainder.ShardId });

            entity.Property(remainder => remainder.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(remainder => remainder.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(remainder => remainder.OwnerId).IsRequired();
            entity.Property(remainder => remainder.RemainderAmount).IsRequired();
            entity.Property(remainder => remainder.Version).IsRequired().IsConcurrencyToken();

            entity.Property(remainder => remainder.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(remainder => remainder.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudPyrealConversionRecord>(entity =>
        {
            entity.ToTable("CloudPyrealConversionRecord");

            entity.HasKey(record => record.IdempotencyKey);
            entity.Property(record => record.IdempotencyKey).ValueGeneratedNever();

            entity.Property(record => record.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(record => record.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(record => record.OwnerId).IsRequired();
            entity.Property(record => record.RawBiotaId).IsRequired();
            entity.Property(record => record.RawPyrealAmount).IsRequired();
            entity.Property(record => record.RemainderBefore).IsRequired();
            entity.Property(record => record.RemainderAfter).IsRequired();
            entity.Property(record => record.CorrelationId).IsRequired();

            entity.Property(record => record.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudPyrealConversionMmd>(entity =>
        {
            entity.ToTable("CloudPyrealConversionMmd");

            entity.HasKey(mmd => mmd.Id);
            entity.Property(mmd => mmd.Id).ValueGeneratedNever();

            entity.Property(mmd => mmd.ConversionIdempotencyKey).IsRequired();
            entity.HasIndex(mmd => mmd.ConversionIdempotencyKey);
            entity.HasOne<CloudPyrealConversionRecord>()
                .WithMany()
                .HasForeignKey(mmd => mmd.ConversionIdempotencyKey)
                .HasPrincipalKey(record => record.IdempotencyKey)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(mmd => mmd.MmdBiotaId).IsRequired();
            entity.Property(mmd => mmd.CustodyRecordId).IsRequired();
        });

        modelBuilder.Entity<CloudPyrealRemainderWithdrawalRecord>(entity =>
        {
            entity.ToTable("CloudPyrealRemainderWithdrawalRecord");

            entity.HasKey(record => record.IdempotencyKey);
            entity.Property(record => record.IdempotencyKey).ValueGeneratedNever();

            entity.Property(record => record.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(record => record.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(record => record.OwnerId).IsRequired();
            entity.Property(record => record.Amount).IsRequired();
            entity.Property(record => record.RemainderBefore).IsRequired();
            entity.Property(record => record.RemainderAfter).IsRequired();
            entity.Property(record => record.RecipientContainerId).IsRequired();
            entity.Property(record => record.CorrelationId).IsRequired();

            entity.Property(record => record.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudPyrealRemainderWithdrawalBiota>(entity =>
        {
            entity.ToTable("CloudPyrealRemainderWithdrawalBiota");

            entity.HasKey(biota => biota.Id);
            entity.Property(biota => biota.Id).ValueGeneratedNever();

            entity.Property(biota => biota.WithdrawalIdempotencyKey).IsRequired();
            entity.HasIndex(biota => biota.WithdrawalIdempotencyKey);
            entity.HasOne<CloudPyrealRemainderWithdrawalRecord>()
                .WithMany()
                .HasForeignKey(biota => biota.WithdrawalIdempotencyKey)
                .HasPrincipalKey(record => record.IdempotencyKey)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(biota => biota.BiotaId).IsRequired();
        });

        modelBuilder.Entity<CloudWithdrawalLocationConfigurationRecord>(entity =>
        {
            // ARCH-001: exactly one Withdrawal Location configuration row per deployment, the same
            // singleton shape as CloudShardBinding.
            entity.ToTable("CloudWithdrawalLocationConfiguration", table =>
                table.HasCheckConstraint("CK_CloudWithdrawalLocationConfiguration_Singleton", "`Id` = 1"));

            entity.HasKey(config => config.Id);
            entity.Property(config => config.Id).ValueGeneratedNever();

            entity.Property(config => config.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(config => config.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(config => config.WithdrawAnywhereEnabled).IsRequired();
            entity.Property(config => config.Version).IsRequired().IsConcurrencyToken();

            entity.Property(config => config.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudWithdrawalNamedLandblockRecord>(entity =>
        {
            entity.ToTable("CloudWithdrawalNamedLandblock");

            entity.HasKey(landblock => landblock.Id);
            entity.Property(landblock => landblock.Id).ValueGeneratedNever();

            entity.Property(landblock => landblock.ShardId).IsRequired().HasMaxLength(64);
            entity.HasIndex(landblock => landblock.ShardId);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(landblock => landblock.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(landblock => landblock.Landblock).IsRequired();
            entity.Property(landblock => landblock.Name).IsRequired().HasMaxLength(128);

            entity.Property(landblock => landblock.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudIdentityOutboxEvent>(entity =>
        {
            entity.ToTable("CloudIdentityOutboxEvent");

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
            entity.Property(evt => evt.CharacterId).IsRequired();
            entity.HasIndex(evt => evt.CharacterId);

            entity.Property(evt => evt.AccountId);
            entity.Property(evt => evt.CharacterName).HasMaxLength(64);
            entity.Property(evt => evt.TotalLogins);
            entity.Property(evt => evt.MonarchId);
            entity.Property(evt => evt.PriorMonarchId);

            // Application-assigned within the same transaction via CloudIdentityOutboxSequence, not
            // database-generated (mirrors CloudCustodyOutboxEvent.SequenceNumber).
            entity.Property(evt => evt.SequenceNumber).IsRequired().ValueGeneratedNever();
            entity.HasIndex(evt => evt.SequenceNumber).IsUnique();

            entity.Property(evt => evt.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudIdentityOutboxSequence>(entity =>
        {
            // One counter row per deployment (ARCH-001), the same singleton shape as
            // CloudCustodyOutboxSequence.
            entity.ToTable("CloudIdentityOutboxSequence", table =>
                table.HasCheckConstraint("CK_CloudIdentityOutboxSequence_Singleton", "`Id` = 1"));

            entity.HasKey(seq => seq.Id);
            entity.Property(seq => seq.Id).ValueGeneratedNever();
            entity.Property(seq => seq.NextValue).IsRequired();
        });

        modelBuilder.Entity<CloudAllegianceVaultBinding>(entity =>
        {
            entity.ToTable("CloudAllegianceVaultBinding");

            entity.HasKey(binding => binding.OwnerId);
            entity.Property(binding => binding.OwnerId).ValueGeneratedNever();

            entity.Property(binding => binding.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(binding => binding.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(binding => binding.MonarchCharacterId).IsRequired();
            entity.HasIndex(binding => new { binding.ShardId, binding.MonarchCharacterId }).IsUnique();

            entity.Property(binding => binding.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudMonarchDeletionDiagnostic>(entity =>
        {
            entity.ToTable("CloudMonarchDeletionDiagnostic");

            entity.HasKey(diagnostic => diagnostic.Id);
            entity.Property(diagnostic => diagnostic.Id).ValueGeneratedNever();

            entity.Property(diagnostic => diagnostic.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(diagnostic => diagnostic.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(diagnostic => diagnostic.MonarchCharacterId).IsRequired();
            // A given vault is only ever diagnosed once; an administrator resolves it out of band
            // rather than this row being repeatedly (re)written by later integrity scans.
            entity.HasIndex(diagnostic => new { diagnostic.ShardId, diagnostic.MonarchCharacterId }).IsUnique();

            entity.Property(diagnostic => diagnostic.VaultOwnerId).IsRequired();
            entity.Property(diagnostic => diagnostic.Reason).IsRequired().HasMaxLength(512);

            entity.Property(diagnostic => diagnostic.DetectedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAuthGrantConsumption>(entity =>
        {
            entity.ToTable("CloudAuthGrantConsumption");

            // AUTH-002: the primary key is the grant's own nonce, so a replayed grant cannot insert
            // a second row for the same nonce (the actual one-use enforcement).
            entity.HasKey(consumption => consumption.Nonce);
            entity.Property(consumption => consumption.Nonce).ValueGeneratedNever();

            entity.Property(consumption => consumption.AccountId).IsRequired();

            entity.Property(consumption => consumption.ConsumedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudWebSession>(entity =>
        {
            entity.ToTable("CloudWebSession");

            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).ValueGeneratedNever();

            entity.Property(session => session.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(session => session.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(session => session.AccountId).IsRequired();
            entity.HasIndex(session => session.AccountId);

            entity.Property(session => session.SecretHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(session => session.SecretHash).IsUnique();

            entity.Property(session => session.CsrfToken).IsRequired().HasMaxLength(64);

            // Not a foreign key: the prior session a rotation replaced remains queryable for audit,
            // but nothing requires it to still exist.
            entity.Property(session => session.RotatedFromSessionId);

            entity.Property(session => session.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(session => session.ExpiresAtUtc).IsRequired();
            entity.Property(session => session.LastSeenAtUtc).IsRequired();
            entity.Property(session => session.RevokedAtUtc);
        });

        modelBuilder.Entity<CloudOwnershipGroup>(entity =>
        {
            entity.ToTable("CloudOwnershipGroup");

            entity.HasKey(group => group.Id);
            entity.Property(group => group.Id).ValueGeneratedNever();

            entity.Property(group => group.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(group => group.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            // AUTH-005: exactly one ownership group per Main Account per shard.
            entity.Property(group => group.MainAccountId).IsRequired();
            entity.HasIndex(group => new { group.ShardId, group.MainAccountId }).IsUnique();

            entity.Property(group => group.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAccountLink>(entity =>
        {
            entity.ToTable("CloudAccountLink");

            entity.HasKey(link => link.Id);
            entity.Property(link => link.Id).ValueGeneratedNever();

            entity.Property(link => link.OwnershipGroupId).IsRequired();
            entity.HasIndex(link => link.OwnershipGroupId);
            entity.HasOne<CloudOwnershipGroup>()
                .WithMany()
                .HasForeignKey(link => link.OwnershipGroupId)
                .HasPrincipalKey(group => group.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(link => link.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(link => link.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            // Not unique: this table retains every historical link/unlink cycle for the same
            // account (audit); CloudActiveAccountLinkMarker enforces "at most one active link".
            entity.Property(link => link.LinkedAccountId).IsRequired();
            entity.HasIndex(link => new { link.ShardId, link.LinkedAccountId });

            entity.Property(link => link.Status).IsRequired().HasConversion<string>().HasMaxLength(16);

            entity.Property(link => link.LinkedAtUtc).IsRequired();
            entity.Property(link => link.UnlinkedAtUtc);
        });

        modelBuilder.Entity<CloudActiveAccountLinkMarker>(entity =>
        {
            entity.ToTable("CloudActiveAccountLinkMarker");

            // The primary key itself is AUTH-006's actual enforcement: at most one active link per
            // account per shard (this entity's own doc comment explains why a filtered unique index
            // on CloudAccountLink cannot play this role instead).
            entity.HasKey(marker => new { marker.ShardId, marker.AccountId });

            entity.Property(marker => marker.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(marker => marker.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(marker => marker.AccountId).IsRequired();

            entity.Property(marker => marker.AccountLinkId).IsRequired();
            entity.HasOne<CloudAccountLink>()
                .WithMany()
                .HasForeignKey(marker => marker.AccountLinkId)
                .HasPrincipalKey(link => link.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(marker => marker.OwnershipGroupId).IsRequired();
            entity.HasIndex(marker => marker.OwnershipGroupId);
            entity.HasOne<CloudOwnershipGroup>()
                .WithMany()
                .HasForeignKey(marker => marker.OwnershipGroupId)
                .HasPrincipalKey(group => group.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(marker => marker.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAccountLinkIdempotencyRecord>(entity =>
        {
            entity.ToTable("CloudAccountLinkIdempotencyRecord");

            entity.HasKey(record => record.IdempotencyKey);
            entity.Property(record => record.IdempotencyKey).ValueGeneratedNever();

            entity.Property(record => record.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(record => record.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(record => record.OperationType).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(record => record.MainAccountId).IsRequired();
            entity.Property(record => record.SourceAccountId).IsRequired();
            entity.Property(record => record.IsApproved).IsRequired();
            entity.Property(record => record.RejectionCode).IsRequired().HasConversion<string>().HasMaxLength(32);

            // Not foreign keys: a rejected attempt leaves both null, and CloudAccountLink retains
            // every historical row so this reference always stays resolvable when present anyway.
            entity.Property(record => record.AccountLinkId);
            entity.Property(record => record.OwnershipGroupId);

            entity.Property(record => record.CorrelationId).IsRequired();

            entity.Property(record => record.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAccountLinkLedgerEvent>(entity =>
        {
            entity.ToTable("CloudAccountLinkLedgerEvent");

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

            entity.Property(evt => evt.EventType).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(evt => evt.MainAccountId).IsRequired();
            entity.HasIndex(evt => evt.MainAccountId);
            entity.Property(evt => evt.SourceAccountId).IsRequired();
            entity.Property(evt => evt.Reason).HasMaxLength(512);

            entity.Property(evt => evt.OccurredAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudDisplayCharacterSelection>(entity =>
        {
            entity.ToTable("CloudDisplayCharacterSelection");

            // AUTH-003: exactly one current Display Character pointer per ownership group.
            entity.HasKey(selection => selection.OwnershipGroupId);
            entity.Property(selection => selection.OwnershipGroupId).ValueGeneratedNever();
            entity.HasOne<CloudOwnershipGroup>()
                .WithOne()
                .HasForeignKey<CloudDisplayCharacterSelection>(selection => selection.OwnershipGroupId)
                .HasPrincipalKey<CloudOwnershipGroup>(group => group.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(selection => selection.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(selection => selection.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(selection => selection.CharacterId);
            entity.Property(selection => selection.CharacterName).HasMaxLength(64);
            entity.Property(selection => selection.TotalLogins);
            entity.Property(selection => selection.Version).IsRequired().IsConcurrencyToken();

            entity.Property(selection => selection.SelectedAtUtc).IsRequired();
        });

        modelBuilder.Entity<CloudDisplayCharacterSelectionHistoryEvent>(entity =>
        {
            entity.ToTable("CloudDisplayCharacterSelectionHistoryEvent");

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

            entity.Property(evt => evt.OwnershipGroupId).IsRequired();
            entity.HasIndex(evt => evt.OwnershipGroupId);

            entity.Property(evt => evt.Reason).IsRequired().HasConversion<string>().HasMaxLength(24);
            entity.Property(evt => evt.CharacterId);
            entity.Property(evt => evt.CharacterName).HasMaxLength(64);
            entity.Property(evt => evt.TotalLogins);

            entity.Property(evt => evt.OccurredAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAssetImportSession>(entity =>
        {
            entity.ToTable("CloudAssetImportSession");

            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).ValueGeneratedNever();

            entity.Property(session => session.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(session => session.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(session => session.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(session => session.TotalBytes).IsRequired();
            entity.Property(session => session.ChunkSizeBytes).IsRequired();
            entity.Property(session => session.ChunkCount).IsRequired();
            entity.Property(session => session.ExpectedChecksumHex).IsRequired().HasMaxLength(64);
            entity.Property(session => session.InitiatedByAccountId).IsRequired();
            entity.Property(session => session.State).IsRequired().HasConversion<string>().HasMaxLength(24);
            entity.Property(session => session.ReceivedChunkCount).IsRequired();

            // Not a foreign key: CloudAssetManifest.SourceImportSessionId already references this
            // session in the other direction (this migration's doc comment explains why both
            // directions cannot be enforced FKs simultaneously).
            entity.Property(session => session.ManifestId);

            entity.Property(session => session.ErrorMessage).HasMaxLength(1024);
            entity.Property(session => session.Version).IsRequired();

            entity.HasIndex(session => new { session.ShardId, session.Kind, session.State });

            entity.Property(session => session.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(session => session.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAssetImportChunkRecord>(entity =>
        {
            entity.ToTable("CloudAssetImportChunkRecord");

            entity.HasKey(chunk => new { chunk.SessionId, chunk.ChunkIndex });

            entity.Property(chunk => chunk.SessionId).IsRequired();
            entity.HasOne<CloudAssetImportSession>()
                .WithMany()
                .HasForeignKey(chunk => chunk.SessionId)
                .HasPrincipalKey(session => session.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(chunk => chunk.ChunkIndex).IsRequired();
            entity.Property(chunk => chunk.Sha256Hex).IsRequired().HasMaxLength(64);
            entity.Property(chunk => chunk.ByteLength).IsRequired();

            entity.Property(chunk => chunk.ReceivedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAssetImportCurrentSessionMarker>(entity =>
        {
            entity.ToTable("CloudAssetImportCurrentSessionMarker");

            entity.HasKey(marker => new { marker.ShardId, marker.Kind });

            entity.Property(marker => marker.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(marker => marker.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(marker => marker.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);

            entity.Property(marker => marker.SessionId).IsRequired();
            entity.HasIndex(marker => marker.SessionId);
            entity.HasOne<CloudAssetImportSession>()
                .WithMany()
                .HasForeignKey(marker => marker.SessionId)
                .HasPrincipalKey(session => session.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(marker => marker.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAssetManifest>(entity =>
        {
            entity.ToTable("CloudAssetManifest");

            entity.HasKey(manifest => manifest.Id);
            entity.Property(manifest => manifest.Id).ValueGeneratedNever();

            entity.Property(manifest => manifest.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(manifest => manifest.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(manifest => manifest.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(manifest => manifest.Version).IsRequired();
            entity.HasIndex(manifest => new { manifest.ShardId, manifest.Kind, manifest.Version }).IsUnique();

            entity.Property(manifest => manifest.State).IsRequired().HasConversion<string>().HasMaxLength(16);

            entity.Property(manifest => manifest.SourceImportSessionId).IsRequired();
            entity.HasOne<CloudAssetImportSession>()
                .WithMany()
                .HasForeignKey(manifest => manifest.SourceImportSessionId)
                .HasPrincipalKey(session => session.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(manifest => manifest.EntryCount).IsRequired();

            entity.Property(manifest => manifest.CreatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(manifest => manifest.ActivatedAtUtc);
            entity.Property(manifest => manifest.SupersededAtUtc);
        });

        modelBuilder.Entity<CloudAssetManifestEntryRecord>(entity =>
        {
            entity.ToTable("CloudAssetManifestEntryRecord");

            entity.HasKey(entry => new { entry.ManifestId, entry.Did, entry.FileKind });

            entity.Property(entry => entry.ManifestId).IsRequired();
            entity.HasOne<CloudAssetManifest>()
                .WithMany()
                .HasForeignKey(entry => entry.ManifestId)
                .HasPrincipalKey(manifest => manifest.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(entry => entry.Did).IsRequired();
            entity.Property(entry => entry.FileKind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(entry => entry.RelativePath).IsRequired().HasMaxLength(255);
            entity.Property(entry => entry.ByteLength).IsRequired();
            entity.Property(entry => entry.Sha256Hex).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<CloudActiveAssetManifest>(entity =>
        {
            entity.ToTable("CloudActiveAssetManifest");

            entity.HasKey(pointer => new { pointer.ShardId, pointer.Kind });

            entity.Property(pointer => pointer.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(pointer => pointer.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(pointer => pointer.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);

            entity.Property(pointer => pointer.ManifestId).IsRequired();
            entity.HasOne<CloudAssetManifest>()
                .WithMany()
                .HasForeignKey(pointer => pointer.ManifestId)
                .HasPrincipalKey(manifest => manifest.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(pointer => pointer.ManifestVersion).IsRequired();

            entity.Property(pointer => pointer.UpdatedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudRetainedSourceAsset>(entity =>
        {
            entity.ToTable("CloudRetainedSourceAsset");

            entity.HasKey(retained => new { retained.ShardId, retained.Kind });

            entity.Property(retained => retained.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(retained => retained.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(retained => retained.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(retained => retained.RelativePath).IsRequired().HasMaxLength(255);
            entity.Property(retained => retained.ByteLength).IsRequired();
            entity.Property(retained => retained.Sha256Hex).IsRequired().HasMaxLength(64);

            entity.Property(retained => retained.SourceImportSessionId).IsRequired();
            entity.HasOne<CloudAssetImportSession>()
                .WithMany()
                .HasForeignKey(retained => retained.SourceImportSessionId)
                .HasPrincipalKey(session => session.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(retained => retained.RetainedAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudAssetImportLedgerEvent>(entity =>
        {
            entity.ToTable("CloudAssetImportLedgerEvent");

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

            entity.Property(evt => evt.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(evt => new { evt.ShardId, evt.Kind });

            entity.Property(evt => evt.EventType).IsRequired().HasConversion<string>().HasMaxLength(32);

            entity.Property(evt => evt.SessionId);
            entity.HasOne<CloudAssetImportSession>()
                .WithMany()
                .HasForeignKey(evt => evt.SessionId)
                .HasPrincipalKey(session => session.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(evt => evt.ManifestId);
            entity.HasOne<CloudAssetManifest>()
                .WithMany()
                .HasForeignKey(evt => evt.ManifestId)
                .HasPrincipalKey(manifest => manifest.Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(evt => evt.ManifestVersion);
            entity.Property(evt => evt.AdminAccountId).IsRequired();
            entity.Property(evt => evt.Reason).HasMaxLength(512);

            entity.Property(evt => evt.OccurredAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<CloudIconDiagnostic>(entity =>
        {
            entity.ToTable("CloudIconDiagnostic");

            entity.HasKey(diagnostic => diagnostic.Id);
            entity.Property(diagnostic => diagnostic.Id).ValueGeneratedNever();

            entity.Property(diagnostic => diagnostic.ShardId).IsRequired().HasMaxLength(64);
            entity.HasOne<CloudShardBinding>()
                .WithMany()
                .HasForeignKey(diagnostic => diagnostic.ShardId)
                .HasPrincipalKey(binding => binding.ShardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(diagnostic => diagnostic.DedupeKey).IsRequired().HasMaxLength(64);
            entity.HasIndex(diagnostic => new { diagnostic.ShardId, diagnostic.DedupeKey }).IsUnique();

            entity.Property(diagnostic => diagnostic.LayerKind).IsRequired().HasConversion<string>().HasMaxLength(24);
            entity.Property(diagnostic => diagnostic.Did).IsRequired();
            entity.Property(diagnostic => diagnostic.Reason).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(diagnostic => diagnostic.OccurrenceCount).IsRequired();

            entity.Property(diagnostic => diagnostic.FirstSeenAtUtc)
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(diagnostic => diagnostic.LastSeenAtUtc)
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(diagnostic => diagnostic.LastSeenManifestVersion).IsRequired(false);
        });
    }
}
