using FaultLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class FaultLedgerDbContext(DbContextOptions<FaultLedgerDbContext> options) : DbContext(options)
{
    internal DbSet<TransferRecord> Transfers => Set<TransferRecord>();
    internal DbSet<ProviderInboxRecord> ProviderInbox => Set<ProviderInboxRecord>();
    internal DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

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
            table.HasCheckConstraint("ck_transfers_state_version", "(state = 'Created' AND version = 0) OR (state = 'ReadyToSubmit' AND version = 1) OR (state = 'Submitting' AND version = 2) OR (state IN ('Accepted', 'Failed') AND version IN (3, 4)) OR (state = 'Unknown' AND version = 3) OR (state = 'Completed' AND version IN (4, 5)) OR (state = 'ManualReview' AND version = 4)");
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

        var inbox = modelBuilder.Entity<ProviderInboxRecord>();
        inbox.ToTable("provider_inbox", table =>
        {
            table.HasCheckConstraint("ck_provider_inbox_event_type", "event_type IN ('Accepted', 'Completed', 'Rejected', 'Failed')");
            table.HasCheckConstraint("ck_provider_inbox_processing_status", "processing_status IN ('Pending', 'Processing', 'Processed', 'Failed')");
            table.HasCheckConstraint("ck_provider_inbox_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_provider_inbox_payload_version", "payload_version = 1");
        });
        inbox.HasKey(record => record.InboxId).HasName("pk_provider_inbox");
        inbox.Property(record => record.InboxId).HasColumnName("inbox_id").ValueGeneratedNever();
        inbox.Property(record => record.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(128).UseCollation("C").IsRequired();
        inbox.HasIndex(record => record.ProviderEventId).IsUnique().HasDatabaseName("uq_provider_inbox_provider_event_id");
        inbox.Property(record => record.ProviderOperationCorrelation).HasColumnName("provider_operation_correlation");
        inbox.HasIndex(record => record.ProviderOperationCorrelation).HasDatabaseName("ix_provider_inbox_operation_correlation");
        inbox.Property(record => record.EventType).HasColumnName("event_type").HasMaxLength(32).IsRequired();
        inbox.Property(record => record.ProviderReference).HasColumnName("provider_reference").HasMaxLength(Transfer.ReferenceMaximumLength);
        inbox.Property(record => record.Evidence).HasColumnName("evidence").HasMaxLength(200).IsRequired();
        inbox.Property(record => record.OccurredAt).HasColumnName("occurred_at");
        inbox.Property(record => record.ReceivedAt).HasColumnName("received_at");
        inbox.Property(record => record.NormalizedPayload).HasColumnName("normalized_payload").HasMaxLength(2048).IsRequired();
        inbox.Property(record => record.PayloadVersion).HasColumnName("payload_version");
        inbox.Property(record => record.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(16).IsRequired();
        inbox.Property(record => record.ProcessedAt).HasColumnName("processed_at");
        inbox.Property(record => record.AttemptCount).HasColumnName("attempt_count");
        inbox.Property(record => record.LastError).HasColumnName("last_error").HasMaxLength(500);
        inbox.Property(record => record.TransferId).HasColumnName("transfer_id");
        inbox.HasIndex(record => new { record.ProcessingStatus, record.ReceivedAt }).HasDatabaseName("ix_provider_inbox_processing");

        var outbox = modelBuilder.Entity<OutboxMessageRecord>();
        outbox.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint("ck_outbox_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_outbox_event_type", "event_type = 'TransferCompleted'");
            table.HasCheckConstraint("ck_outbox_schema_version", "schema_version = 1");
            table.HasCheckConstraint("ck_outbox_lease_consistency", "(claimed_until IS NULL AND claimed_by IS NULL AND claim_id IS NULL) OR (claimed_until IS NOT NULL AND claimed_by IS NOT NULL AND claim_id IS NOT NULL)");
        });
        outbox.HasKey(record => record.Id).HasName("pk_outbox_messages");
        outbox.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        outbox.Property(record => record.AggregateId).HasColumnName("aggregate_id");
        outbox.Property(record => record.AggregateVersion).HasColumnName("aggregate_version");
        outbox.Property(record => record.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        outbox.Property(record => record.SchemaVersion).HasColumnName("schema_version");
        outbox.Property(record => record.Payload).HasColumnName("payload").HasMaxLength(2048).IsRequired();
        outbox.Property(record => record.CreatedAt).HasColumnName("created_at");
        outbox.Property(record => record.PublishedAt).HasColumnName("published_at");
        outbox.Property(record => record.AttemptCount).HasColumnName("attempt_count");
        outbox.Property(record => record.NextAttemptAt).HasColumnName("next_attempt_at");
        outbox.Property(record => record.ClaimId).HasColumnName("claim_id");
        outbox.Property(record => record.ClaimedBy).HasColumnName("claimed_by").HasMaxLength(128);
        outbox.Property(record => record.ClaimedUntil).HasColumnName("claimed_until");
        outbox.Property(record => record.LastError).HasColumnName("last_error").HasMaxLength(500);
        outbox.HasIndex(record => new
        { record.PublishedAt, record.NextAttemptAt, record.ClaimedUntil, record.CreatedAt })
            .HasDatabaseName("ix_outbox_due");
        outbox.HasIndex(record => new { record.AggregateId, record.EventType })
            .IsUnique().HasDatabaseName("uq_outbox_aggregate_event");
    }
}
