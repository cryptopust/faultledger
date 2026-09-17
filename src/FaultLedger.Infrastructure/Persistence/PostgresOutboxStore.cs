using FaultLedger.Application.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class PostgresOutboxStore(FaultLedgerDbContext database) : IOutboxStore
{
    public async Task<ClaimedOutboxMessage?> ClaimNextAsync(string workerId, DateTimeOffset claimedAt,
        DateTimeOffset claimedUntil, CancellationToken cancellationToken)
    {
        if (claimedUntil <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(claimedUntil), "A claim must expire after it starts.");
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await database.Database.BeginTransactionAsync(cancellationToken);
        OutboxMessageRecord? record = await database.OutboxMessages
            .FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE published_at IS NULL AND next_attempt_at <= {claimedAt} AND (claimed_until IS NULL OR claimed_until <= {claimedAt}) ORDER BY next_attempt_at, created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        record.ClaimId = Guid.NewGuid();
        record.ClaimedBy = workerId;
        record.ClaimedUntil = claimedUntil;
        record.AttemptCount++;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new ClaimedOutboxMessage(record.ToMessage(), record.AttemptCount, workerId,
            record.ClaimId.Value, claimedUntil);
        database.Entry(record).State = EntityState.Detached;
        return result;
    }

    public async Task<bool> MarkPublishedAsync(Guid eventId, Guid claimId, DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        int changed = await database.OutboxMessages
            .Where(message => message.Id == eventId && message.PublishedAt == null &&
                              message.ClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.PublishedAt, publishedAt)
                .SetProperty(message => message.ClaimId, (Guid?)null)
                .SetProperty(message => message.ClaimedBy, (string?)null)
                .SetProperty(message => message.ClaimedUntil, (DateTimeOffset?)null)
                .SetProperty(message => message.LastError, (string?)null), cancellationToken);
        return changed == 1;
    }

    public async Task<bool> RecordFailureAsync(Guid eventId, Guid claimId, DateTimeOffset nextAttemptAt,
        string error, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        string boundedError = error.Length <= 500 ? error : error[..500];
        int changed = await database.OutboxMessages
            .Where(message => message.Id == eventId && message.PublishedAt == null &&
                              message.ClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt)
                .SetProperty(message => message.ClaimId, (Guid?)null)
                .SetProperty(message => message.ClaimedBy, (string?)null)
                .SetProperty(message => message.ClaimedUntil, (DateTimeOffset?)null)
                .SetProperty(message => message.LastError, boundedError), cancellationToken);
        return changed == 1;
    }
}
