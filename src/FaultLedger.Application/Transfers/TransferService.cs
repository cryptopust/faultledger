using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public sealed class TransferService(ITransferStore store, ITransferProvider provider, TimeProvider timeProvider)
{
    public async Task<TransferDetails> CreateAsync(CreateTransferCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);
        Transfer transfer;
        try
        {
            transfer = Transfer.Create(Guid.NewGuid(), command.ClientReference, command.IdempotencyKey,
                new Money(command.Amount, command.Currency), timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            throw new TransferValidationException(exception.Message);
        }

        await store.AddAsync(transfer, cancellationToken);
        long expectedVersion = transfer.Version;
        transfer.MarkReadyToSubmit(timeProvider.GetUtcNow());
        await store.UpdateAsync(transfer, expectedVersion, cancellationToken);
        expectedVersion = transfer.Version;
        transfer.BeginSubmission(timeProvider.GetUtcNow());
        await store.UpdateAsync(transfer, expectedVersion, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ProviderSubmissionResult result = await provider.SubmitAsync(
            new ProviderTransferRequest(transfer.Id, transfer.ClientReference, transfer.Money), cancellationToken);
        if (!result.IsConfirmedAccepted || result.ProviderReference is null)
        {
            throw new ProviderSubmissionException(result);
        }

        expectedVersion = transfer.Version;
        transfer.MarkAccepted(result.ProviderReference, timeProvider.GetUtcNow());
        await store.UpdateAsync(transfer, expectedVersion, cancellationToken);
        return TransferDetails.FromTransfer(transfer);
    }

    public async Task<TransferDetails?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Transfer? transfer = await store.FindAsync(id, cancellationToken);
        return transfer is null ? null : TransferDetails.FromTransfer(transfer);
    }
}
