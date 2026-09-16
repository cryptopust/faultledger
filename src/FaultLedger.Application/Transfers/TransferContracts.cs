using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public sealed record CreateTransferCommand(string ClientReference, string IdempotencyKey, decimal Amount, string Currency);

public sealed record TransferDetails(Guid Id, string ClientReference, string IdempotencyKey, decimal Amount,
    string Currency, string State, string? ProviderReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version)
{
    public static TransferDetails FromTransfer(Transfer transfer) => new(transfer.Id, transfer.ClientReference,
        transfer.IdempotencyKey, transfer.Money.Amount, transfer.Money.Currency, transfer.State.ToString(),
        transfer.ProviderReference, transfer.CreatedAt, transfer.UpdatedAt, transfer.Version);
}

public interface ITransferStore
{
    Task AddAsync(Transfer transfer, CancellationToken cancellationToken);
    Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken);
    Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DuplicateTransferKeyException() : InvalidOperationException("The idempotency key already belongs to a durable transfer. Replay recovery is not implemented; do not resubmit with a new key.");

public sealed class TransferConcurrencyException() : InvalidOperationException("The durable transfer changed concurrently. No retry was performed.");

public sealed class TransferValidationException(string message) : ArgumentException(message);

public sealed class TransferStorageUnavailableException() : InvalidOperationException("Transfer persistence is unavailable. An earlier commit or submission may exist; do not resubmit with a new key.");
