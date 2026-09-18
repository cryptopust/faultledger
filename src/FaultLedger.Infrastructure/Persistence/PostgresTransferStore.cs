using FaultLedger.Application.IntegrationEvents;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class PostgresTransferStore(
    FaultLedgerDbContext database,
    ILogger<PostgresTransferStore> logger,
    IOutboxPersistenceHook? persistenceHook = null) : ITransferStore
{
    public async Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint,
        int fingerprintVersion, CancellationToken cancellationToken)
    {
        if (transfer.State != TransferState.ReadyToSubmit || transfer.Version != 1)
        {
            throw new InvalidOperationException("The idempotency registration must atomically insert ReadyToSubmit.");
        }

        var record = TransferRecord.FromTransfer(transfer);
        record.RequestFingerprint = requestFingerprint;
        record.FingerprintVersion = fingerprintVersion;
        database.Transfers.Add(record);
        TransferAuditEventRecord audit = TransferAuditEventRecord.Create(transfer, null,
            "transfer-registered", "idempotency", transfer.Version, transfer.UpdatedAt);
        database.TransferAuditEvents.Add(audit);
        try
        {
            await SaveAsync(cancellationToken, translateIdempotencyViolation: false);
            database.Entry(record).State = EntityState.Detached;
            database.Entry(audit).State = EntityState.Detached;
            return new TransferRegistration(transfer, requestFingerprint, fingerprintVersion, true);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyViolation(exception))
        {
            database.Entry(record).State = EntityState.Detached;
            database.Entry(audit).State = EntityState.Detached;
            TransferRecord? existing = await database.Transfers.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == transfer.IdempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw new TransferStorageUnavailableException();
            }

            return new TransferRegistration(existing.ToTransfer(), existing.RequestFingerprint,
                existing.FingerprintVersion, false);
        }
    }

    public async Task AddAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        if (transfer.State != TransferState.Created || transfer.Version != 0)
        {
            throw new InvalidOperationException("Only a newly created transfer may be inserted.");
        }

        var record = TransferRecord.FromTransfer(transfer);
        database.Transfers.Add(record);
        TransferAuditEventRecord audit = TransferAuditEventRecord.Create(transfer, null,
            "transfer-created", "application", transfer.Version, transfer.UpdatedAt);
        database.TransferAuditEvents.Add(audit);
        try
        {
            await SaveAsync(cancellationToken);
        }
        finally
        {
            database.Entry(record).State = EntityState.Detached;
            database.Entry(audit).State = EntityState.Detached;
        }
    }

    public async Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken,
        TransferAuditMetadata? auditMetadata = null)
    {
        if (transfer.Version != checked(expectedVersion + 1))
        {
            throw new InvalidOperationException("Each durable update must represent exactly one transition.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        TransferRecord? previous = await database.Transfers
            .FromSqlInterpolated($"SELECT * FROM transfers WHERE id = {transfer.Id} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (previous is null)
        {
            throw new TransferStorageUnavailableException();
        }

        if (previous.Version != expectedVersion)
        {
            throw new TransferConcurrencyException();
        }

        var record = TransferRecord.FromTransfer(transfer);
        var entry = database.Attach(record);
        entry.Property(current => current.State).IsModified = true;
        entry.Property(current => current.ProviderReference).IsModified = true;
        entry.Property(current => current.UpdatedAt).IsModified = true;
        entry.Property(current => current.Version).IsModified = true;
        entry.Property(current => current.Version).OriginalValue = expectedVersion;
        OutboxMessageRecord? outbox = null;
        TransferAuditEventRecord? auditRecord = null;
        if (transfer.State == TransferState.Completed)
        {
            outbox = OutboxMessageRecord.FromCompletedTransfer(transfer);
            database.OutboxMessages.Add(outbox);
        }
        TransferState previousState = Enum.Parse<TransferState>(previous.State, ignoreCase: false);
        TransferAuditMetadata metadata = auditMetadata ?? InferAudit(previousState, transfer.State);
        auditRecord = TransferAuditEventRecord.Create(transfer, previousState, metadata.Reason,
            metadata.Source, transfer.Version, transfer.UpdatedAt);
        database.TransferAuditEvents.Add(auditRecord);
        try
        {
            await SaveAsync(cancellationToken);
            if (transfer.State == TransferState.Completed)
            {
                await (persistenceHook ?? new NoOpOutboxPersistenceHook()).BeforeCommitAsync(transfer.Id,
                    cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            entry.State = EntityState.Detached;
            if (outbox is not null)
            {
                database.Entry(outbox).State = EntityState.Detached;
            }
            if (auditRecord is not null)
            {
                database.Entry(auditRecord).State = EntityState.Detached;
            }
        }
    }

    public async Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            TransferRecord? record = await database.Transfers.AsNoTracking()
                .SingleOrDefaultAsync(transfer => transfer.Id == id, cancellationToken);
            return record?.ToTransfer();
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            logger.LogError("Transfer query failed ({FailureType}, {SqlState}).", exception.GetType().Name, exception.SqlState);
            throw new TransferStorageUnavailableException();
        }
    }

    public async Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (transfer.State != TransferState.Submitting || transfer.Version != checked(expectedVersion + 1))
        {
            throw new InvalidOperationException("A submission claim must represent ReadyToSubmit to Submitting.");
        }

        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            TransferRecord? current = await database.Transfers
                .FromSqlInterpolated($"SELECT * FROM transfers WHERE id = {transfer.Id} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (current is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return SubmissionClaimResult.NotFound;
            }

            if (current.State != TransferState.ReadyToSubmit.ToString() || current.Version != expectedVersion)
            {
                await transaction.RollbackAsync(cancellationToken);
                return current.State == TransferState.ReadyToSubmit.ToString()
                    ? SubmissionClaimResult.InvalidState
                    : SubmissionClaimResult.AlreadyClaimedOrSubmitted;
            }

            int changed = await database.Transfers
                .Where(item => item.Id == transfer.Id && item.State == TransferState.ReadyToSubmit.ToString() &&
                               item.Version == expectedVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, transfer.State.ToString())
                    .SetProperty(item => item.UpdatedAt, transfer.UpdatedAt)
                    .SetProperty(item => item.Version, transfer.Version), cancellationToken);
            TransferAuditEventRecord? audit = null;
            if (changed == 1)
            {
                audit = TransferAuditEventRecord.Create(transfer,
                    TransferState.ReadyToSubmit, "submission-claimed", "submission", transfer.Version,
                    transfer.UpdatedAt);
                database.TransferAuditEvents.Add(audit);
                await SaveAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                database.Entry(audit).State = EntityState.Detached;
                return SubmissionClaimResult.ClaimAcquired;
            }

            await transaction.RollbackAsync(cancellationToken);
            return SubmissionClaimResult.AlreadyClaimedOrSubmitted;
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            logger.LogError("Submission claim failed ({FailureType}, {SqlState}).", exception.GetType().Name, exception.SqlState);
            throw new TransferStorageUnavailableException();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken, bool translateIdempotencyViolation = true)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TransferConcurrencyException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_outbox_aggregate_event"
        })
        {
            throw new TransferConcurrencyException();
        }
        catch (DbUpdateException exception) when (translateIdempotencyViolation && exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_transfers_idempotency_key" })
        {
            throw new DuplicateTransferKeyException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException { IsTransient: true })
        {
            logger.LogError("Transfer write failed ({FailureType}).", exception.InnerException.GetType().Name);
            throw new TransferStorageUnavailableException();
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            logger.LogError("Transfer write failed ({FailureType}, {SqlState}).", exception.GetType().Name, exception.SqlState);
            throw new TransferStorageUnavailableException();
        }
    }

    private static bool IsIdempotencyKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_transfers_idempotency_key"
        };

    private static TransferAuditMetadata InferAudit(TransferState previous, TransferState next) =>
        new($"{previous}->{next}", "persistence");
}
