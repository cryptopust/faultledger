using FaultLedger.Domain;

namespace FaultLedger.Infrastructure.Persistence;

internal sealed class TransferRecord
{
    public Guid Id { get; set; }
    public required string ClientReference { get; set; }
    public required string IdempotencyKey { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string State { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }

    public static TransferRecord FromTransfer(Transfer transfer) => new()
    {
        Id = transfer.Id,
        ClientReference = transfer.ClientReference,
        IdempotencyKey = transfer.IdempotencyKey,
        Amount = transfer.Money.Amount,
        Currency = transfer.Money.Currency,
        State = transfer.State.ToString(),
        ProviderReference = transfer.ProviderReference,
        CreatedAt = transfer.CreatedAt,
        UpdatedAt = transfer.UpdatedAt,
        Version = transfer.Version
    };

    public Transfer ToTransfer()
    {
        if (!Enum.TryParse(State, out TransferState state) || !Enum.IsDefined(state) || state.ToString() != State)
        {
            throw new InvalidOperationException("Stored transfer state is invalid.");
        }

        var money = new Money(Amount, Currency);
        if (money.Currency != Currency)
        {
            throw new InvalidOperationException("Stored currency is not canonical.");
        }

        return Transfer.Restore(Id, ClientReference, IdempotencyKey, money, state, ProviderReference,
            CreatedAt, UpdatedAt, Version);
    }
}
