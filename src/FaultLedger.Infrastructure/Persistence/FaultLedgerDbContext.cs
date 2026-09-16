using FaultLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class FaultLedgerDbContext(DbContextOptions<FaultLedgerDbContext> options) : DbContext(options)
{
    internal DbSet<TransferRecord> Transfers => Set<TransferRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transfer = modelBuilder.Entity<TransferRecord>();
        transfer.ToTable("transfers", table =>
        {
            table.HasCheckConstraint("ck_transfers_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_transfers_amount", "amount > 0 AND amount <= 99999999999999999999.99999999");
            table.HasCheckConstraint("ck_transfers_currency", "currency COLLATE \"C\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_transfers_client_reference", "client_reference COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,100}$'");
            table.HasCheckConstraint("ck_transfers_idempotency_key", "idempotency_key COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,128}$'");
            table.HasCheckConstraint("ck_transfers_request_fingerprint", "(request_fingerprint IS NULL AND fingerprint_version IS NULL) OR (request_fingerprint COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND fingerprint_version = 1)");
            table.HasCheckConstraint("ck_transfers_state_version", "(state = 'Created' AND version = 0) OR (state = 'ReadyToSubmit' AND version = 1) OR (state = 'Submitting' AND version = 2) OR (state IN ('Accepted', 'Failed', 'Unknown') AND version = 3) OR (state IN ('Completed', 'ManualReview') AND version = 4)");
            table.HasCheckConstraint("ck_transfers_provider_reference", "(state IN ('Accepted', 'Completed') AND provider_reference IS NOT NULL AND provider_reference COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,100}$') OR (state NOT IN ('Accepted', 'Completed') AND provider_reference IS NULL)");
            table.HasCheckConstraint("ck_transfers_timestamps", "updated_at >= created_at AND isfinite(created_at) AND isfinite(updated_at)");
        });
        transfer.HasKey(record => record.Id).HasName("pk_transfers");
        transfer.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        transfer.Property(record => record.ClientReference).HasColumnName("client_reference").HasMaxLength(Transfer.ReferenceMaximumLength).IsRequired();
        transfer.Property(record => record.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(Transfer.IdempotencyKeyMaximumLength).UseCollation("C").IsRequired();
        transfer.HasIndex(record => record.IdempotencyKey).IsUnique().HasDatabaseName("uq_transfers_idempotency_key");
        transfer.Property(record => record.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).UseCollation("C");
        transfer.Property(record => record.FingerprintVersion).HasColumnName("fingerprint_version");
        transfer.Property(record => record.Amount).HasColumnName("amount").HasPrecision(Money.Precision, Money.Scale);
        transfer.Property(record => record.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        transfer.Property(record => record.State).HasColumnName("state").HasMaxLength(32).IsRequired();
        transfer.Property(record => record.ProviderReference).HasColumnName("provider_reference").HasMaxLength(Transfer.ReferenceMaximumLength);
        transfer.Property(record => record.CreatedAt).HasColumnName("created_at");
        transfer.Property(record => record.UpdatedAt).HasColumnName("updated_at");
        transfer.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedNever();
    }
}
