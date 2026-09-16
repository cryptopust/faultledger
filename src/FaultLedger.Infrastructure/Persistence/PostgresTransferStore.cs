using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class PostgresTransferStore(FaultLedgerDbContext database, ILogger<PostgresTransferStore> logger) : ITransferStore
{
    public async Task AddAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        if (transfer.State != TransferState.Created || transfer.Version != 0)
        {
            throw new InvalidOperationException("Only a newly created transfer may be inserted.");
        }

        var record = TransferRecord.FromTransfer(transfer);
        database.Transfers.Add(record);
        try
        {
            await SaveAsync(cancellationToken);
        }
        finally
        {
            database.Entry(record).State = EntityState.Detached;
        }
    }

    public async Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken)
    {
        if (transfer.Version != checked(expectedVersion + 1))
        {
            throw new InvalidOperationException("Each durable update must represent exactly one transition.");
        }

        var record = TransferRecord.FromTransfer(transfer);
        var entry = database.Attach(record);
        entry.Property(current => current.State).IsModified = true;
        entry.Property(current => current.ProviderReference).IsModified = true;
        entry.Property(current => current.UpdatedAt).IsModified = true;
        entry.Property(current => current.Version).IsModified = true;
        entry.Property(current => current.Version).OriginalValue = expectedVersion;
        try
        {
            await SaveAsync(cancellationToken);
        }
        finally
        {
            entry.State = EntityState.Detached;
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

    private async Task SaveAsync(CancellationToken cancellationToken)
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
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_transfers_idempotency_key" })
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
}
