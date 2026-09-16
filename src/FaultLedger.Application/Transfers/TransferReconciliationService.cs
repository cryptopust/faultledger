using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public sealed class TransferReconciliationService(
    ITransferStore store,
    ITransferLookup provider,
    TimeProvider timeProvider)
{
    public async Task<ReconciliationDetails> ReconcileAsync(Guid transferId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Transfer? transfer = await store.FindAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            throw new TransferNotFoundException();
        }

        if (transfer.State != TransferState.Unknown)
        {
            return new ReconciliationDetails(TransferDetails.FromTransfer(transfer),
                transfer.State is TransferState.Accepted or TransferState.Completed or TransferState.Failed
                    ? ReconciliationOutcome.AlreadyResolved
                    : ReconciliationOutcome.NotEligible,
                transfer.State is TransferState.Submitting
                    ? "The dispatch is unresolved; reconciliation is not authorized for this state."
                    : "The transfer has already reached a known state.");
        }

        ProviderLookupResult result;
        try
        {
            result = await provider.LookupAsync(new ProviderLookupRequest(transfer.Id), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            throw new ReconciliationUnavailableException("Provider reconciliation is temporarily unavailable.");
        }

        switch (result.Evidence)
        {
            case ProviderLookupEvidence.ConfirmedAccepted:
                return await ResolveAsync(transfer, ReconciliationOutcome.ResolvedAccepted,
                    result.SafeMessage, (item, timestamp) => item.ResolveAccepted(RequireProviderReference(result), timestamp),
                    cancellationToken);
            case ProviderLookupEvidence.ConfirmedCompleted:
                return await ResolveAsync(transfer, ReconciliationOutcome.ResolvedCompleted,
                    result.SafeMessage, (item, timestamp) => item.ResolveCompleted(RequireProviderReference(result), result.SafeMessage, timestamp),
                    cancellationToken);
            case ProviderLookupEvidence.ConfirmedRejected:
                return await ResolveAsync(transfer, ReconciliationOutcome.ResolvedRejected,
                    result.SafeMessage, (item, timestamp) => item.ResolveFailed(result.SafeMessage, timestamp),
                    cancellationToken);
            case ProviderLookupEvidence.NotFound:
                return await PreserveUnknownAsync(transfer, ReconciliationOutcome.NotFound, result.SafeMessage,
                    cancellationToken);
            case ProviderLookupEvidence.StillUnknown:
                return await PreserveUnknownAsync(transfer, ReconciliationOutcome.StillUnknown, result.SafeMessage,
                    cancellationToken);
            case ProviderLookupEvidence.TemporaryFailure:
                throw new ReconciliationUnavailableException(result.SafeMessage);
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Evidence), result.Evidence,
                    "Unknown provider lookup evidence cannot be interpreted.");
        }
    }

    public async Task<TransferDetails> SendToManualReviewAsync(Guid transferId, string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Transfer? transfer = await store.FindAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            throw new TransferNotFoundException();
        }

        long expectedVersion = transfer.Version;
        transfer.SendToManualReview(reason, timeProvider.GetUtcNow());
        await store.UpdateAsync(transfer, expectedVersion, cancellationToken);
        return TransferDetails.FromTransfer(transfer);
    }

    private async Task<ReconciliationDetails> ResolveAsync(
        Transfer transfer,
        ReconciliationOutcome outcome,
        string message,
        Action<Transfer, DateTimeOffset> transition,
        CancellationToken cancellationToken)
    {
        long expectedVersion = transfer.Version;
        transition(transfer, timeProvider.GetUtcNow());
        try
        {
            await store.UpdateAsync(transfer, expectedVersion, cancellationToken);
            return new ReconciliationDetails(TransferDetails.FromTransfer(transfer), outcome, message);
        }
        catch (TransferConcurrencyException)
        {
            Transfer? current = await store.FindAsync(transfer.Id, cancellationToken);
            if (current is null)
            {
                throw new TransferStorageUnavailableException();
            }

            return new ReconciliationDetails(TransferDetails.FromTransfer(current),
                current.State is TransferState.Accepted or TransferState.Completed or TransferState.Failed
                    ? ReconciliationOutcome.AlreadyResolved
                    : ReconciliationOutcome.NotEligible,
                "A concurrent reconciliation resolved the transfer; the durable current state is returned.");
        }
    }

    private async Task<ReconciliationDetails> PreserveUnknownAsync(
        Transfer transfer,
        ReconciliationOutcome outcome,
        string message,
        CancellationToken cancellationToken)
    {
        Transfer? current = await store.FindAsync(transfer.Id, cancellationToken);
        if (current is null)
        {
            throw new TransferStorageUnavailableException();
        }

        return new ReconciliationDetails(TransferDetails.FromTransfer(current),
            current.State == TransferState.Unknown ? outcome : ReconciliationOutcome.AlreadyResolved,
            current.State == TransferState.Unknown
                ? message
                : "A concurrent reconciliation resolved the transfer; the durable current state is returned.");
    }

    private static string RequireProviderReference(ProviderLookupResult result) =>
        result.ProviderReference ?? throw new InvalidOperationException(
            "Confirmed provider lookup evidence must include a provider reference.");
}

public sealed class TransferNotFoundException() : InvalidOperationException("The transfer was not found.");
