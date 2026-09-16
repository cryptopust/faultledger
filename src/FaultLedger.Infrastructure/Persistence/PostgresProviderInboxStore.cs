using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class PostgresProviderInboxStore(
    FaultLedgerDbContext database,
    TimeProvider timeProvider,
    IProviderInboxProcessingHook processingHook,
    ILogger<PostgresProviderInboxStore> logger) : IProviderInboxStore
{
    public async Task<InboxReceipt> ReceiveAsync(ProviderCallbackEnvelope callback, string normalizedPayload,
        CancellationToken cancellationToken)
    {
        var record = new ProviderInboxRecord
        {
            InboxId = Guid.NewGuid(),
            ProviderEventId = callback.ProviderEventId,
            ProviderOperationCorrelation = callback.ProviderOperationCorrelation,
            EventType = callback.EventType.ToString(),
            ProviderReference = callback.ProviderReference,
            Evidence = callback.Evidence,
            OccurredAt = callback.OccurredAt.ToUniversalTime(),
            ReceivedAt = timeProvider.GetUtcNow(),
            NormalizedPayload = normalizedPayload,
            PayloadVersion = callback.PayloadVersion,
            ProcessingStatus = InboxProcessingStatus.Pending.ToString(),
            AttemptCount = 0
        };
        database.ProviderInbox.Add(record);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new InboxReceipt(InboxReceiptOutcome.Received, record.InboxId);
        }
        catch (DbUpdateException exception) when (IsEventDuplicate(exception))
        {
            database.Entry(record).State = EntityState.Detached;
            ProviderInboxRecord? existing = await database.ProviderInbox.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ProviderEventId == callback.ProviderEventId, cancellationToken);
            if (existing is null)
            {
                throw new TransferStorageUnavailableException();
            }

            if (existing.ProviderOperationCorrelation != callback.ProviderOperationCorrelation ||
                existing.EventType != callback.EventType.ToString() ||
                existing.ProviderReference != callback.ProviderReference ||
                existing.Evidence != callback.Evidence ||
                existing.NormalizedPayload != normalizedPayload)
            {
                throw new ProviderEventConflictException();
            }

            return new InboxReceipt(InboxReceiptOutcome.Duplicate, existing.InboxId);
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            logger.LogError("Provider callback receipt failed ({FailureType}, {SqlState}).",
                exception.GetType().Name, exception.SqlState);
            throw new TransferStorageUnavailableException();
        }
    }

    public async Task<CallbackProcessingDetails?> ProcessAsync(CancellationToken cancellationToken)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await database.Database.BeginTransactionAsync(cancellationToken);
        ProviderInboxRecord? inbox = await database.ProviderInbox
            .FromSqlRaw("SELECT * FROM provider_inbox WHERE processing_status IN ('Pending', 'Processing') ORDER BY received_at, inbox_id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (inbox is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        inbox.ProcessingStatus = InboxProcessingStatus.Processing.ToString();
        inbox.AttemptCount++;
        await database.SaveChangesAsync(cancellationToken);

        CallbackProcessingDetails details;
        try
        {
            details = await ApplyCallbackAsync(inbox, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidTransferTransitionException or ArgumentException or ProviderCallbackConflictException)
        {
            inbox.ProcessingStatus = InboxProcessingStatus.Failed.ToString();
            inbox.LastError = exception.Message;
            inbox.ProcessedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            details = new CallbackProcessingDetails(inbox.InboxId, CallbackProcessingOutcome.Poison, null,
                "The callback was retained as a failed poison event and will not be retried automatically.");
            return details;
        }

        await processingHook.BeforeCommitAsync(inbox.InboxId, cancellationToken);

        if (inbox.ProcessingStatus == InboxProcessingStatus.Processing.ToString())
        {
            inbox.ProcessingStatus = InboxProcessingStatus.Processed.ToString();
            inbox.ProcessedAt = timeProvider.GetUtcNow();
            inbox.LastError = null;
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return details;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        database.ProviderInbox.CountAsync(cancellationToken);

    private async Task<CallbackProcessingDetails> ApplyCallbackAsync(ProviderInboxRecord inbox,
        CancellationToken cancellationToken)
    {
        ProviderCallbackEnvelope callback = inbox.ToEnvelope();
        TransferRecord? record = await database.Transfers
            .FromSqlInterpolated($"SELECT * FROM transfers WHERE id = {callback.ProviderOperationCorrelation} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            inbox.LastError = "The callback correlation does not identify a durable transfer.";
            inbox.ProcessingStatus = InboxProcessingStatus.Failed.ToString();
            inbox.ProcessedAt = timeProvider.GetUtcNow();
            return new CallbackProcessingDetails(inbox.InboxId, CallbackProcessingOutcome.UnknownCorrelation, null,
                "The callback was durably retained but could not be correlated to a transfer.");
        }

        Transfer transfer = record.ToTransfer();
        CallbackProcessingOutcome outcome = ProviderCallbackStateMachine.Apply(transfer, callback,
            timeProvider.GetUtcNow());
        if (outcome == CallbackProcessingOutcome.Applied)
        {
            record.State = transfer.State.ToString();
            record.ProviderReference = transfer.ProviderReference;
            record.UpdatedAt = transfer.UpdatedAt;
            record.Version = transfer.Version;
            inbox.TransferId = transfer.Id;
        }

        return new CallbackProcessingDetails(inbox.InboxId, outcome, TransferDetails.FromTransfer(transfer),
            outcome switch
            {
                CallbackProcessingOutcome.Applied => "Callback evidence applied to the durable transfer.",
                CallbackProcessingOutcome.AlreadyApplied => "Callback evidence was already reflected in the durable transfer.",
                _ => "The callback was retained as stale evidence; the durable transfer state was not regressed."
            });
    }

    private static bool IsEventDuplicate(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "uq_provider_inbox_provider_event_id"
    };
}
