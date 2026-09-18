using System.Diagnostics;
using FaultLedger.Application.Diagnostics;
using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public sealed class TransferService(ITransferStore store, ITransferProvider provider, TimeProvider timeProvider)
{
    public async Task<TransferDetails> CreateAsync(CreateTransferCommand command, CancellationToken cancellationToken) =>
        (await CreateWithOutcomeAsync(command, cancellationToken)).Details;

    public async Task<TransferCreateOutcome> CreateWithOutcomeAsync(CreateTransferCommand command,
        CancellationToken cancellationToken)
    {
        using Activity? activity = FaultLedgerTelemetry.ActivitySource.StartActivity("faultledger.transfer.create");
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);
        Transfer transfer;
        string requestFingerprint;
        try
        {
            requestFingerprint = TransferRequestFingerprint.Compute(command);
            transfer = Transfer.Create(Guid.NewGuid(), command.ClientReference, command.IdempotencyKey,
                new Money(command.Amount, command.Currency), timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            throw new TransferValidationException(exception.Message);
        }

        transfer.MarkReadyToSubmit(timeProvider.GetUtcNow());
        TransferRegistration registration = await store.CreateOrGetAsync(transfer, requestFingerprint,
            TransferRequestFingerprint.CurrentVersion, cancellationToken);
        if (!registration.Created)
        {
            string storedFingerprint = registration.RequestFingerprint ??
                TransferRequestFingerprint.Compute(registration.Transfer.ClientReference,
                    registration.Transfer.Money.Amount, registration.Transfer.Money.Currency);
            if (!string.Equals(storedFingerprint, requestFingerprint, StringComparison.Ordinal) ||
                (registration.FingerprintVersion is not null &&
                 registration.FingerprintVersion != TransferRequestFingerprint.CurrentVersion))
            {
                FaultLedgerTelemetry.IdempotencyConflicts.Add(1);
                throw new IdempotencyConflictException();
            }

            transfer = registration.Transfer;
            activity?.SetTag("faultledger.transfer.id", transfer.Id);
            activity?.SetTag("faultledger.transfer.state", transfer.State.ToString());
            FaultLedgerTelemetry.IdempotencyReplays.Add(1);
            return new TransferCreateOutcome(TransferDetails.FromTransfer(transfer), true);
        }

        activity?.SetTag("faultledger.transfer.id", transfer.Id);
        FaultLedgerTelemetry.TransfersCreated.Add(1);

        long expectedVersion = transfer.Version;
        transfer.BeginSubmission(timeProvider.GetUtcNow());
        SubmissionClaimResult claim = await store.TryClaimSubmissionAsync(transfer, expectedVersion,
            cancellationToken);
        if (claim != SubmissionClaimResult.ClaimAcquired)
        {
            Transfer? current = await store.FindAsync(transfer.Id, cancellationToken);
            return new TransferCreateOutcome(current is null
                ? throw new TransferStorageUnavailableException()
                : TransferDetails.FromTransfer(current), true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ProviderSubmissionResult result;
        using Activity? providerActivity = FaultLedgerTelemetry.ActivitySource.StartActivity(
            "faultledger.provider.submit");
        providerActivity?.SetTag("faultledger.transfer.id", transfer.Id);
        try
        {
            FaultLedgerTelemetry.ProviderSubmissions.Add(1);
            result = await provider.SubmitAsync(
                new ProviderTransferRequest(transfer.Id, transfer.ClientReference, transfer.Money), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            transfer.MarkUnknown("The provider call was cancelled after dispatch authority was acquired.",
                timeProvider.GetUtcNow());
            FaultLedgerTelemetry.UnknownOutcomes.Add(1);
            await store.UpdateAsync(transfer, transfer.Version - 1, CancellationToken.None,
                new TransferAuditMetadata("provider-call-cancelled-after-dispatch", "provider"));
            throw;
        }

        providerActivity?.SetTag("faultledger.provider.outcome", result.AcceptanceEvidence.ToString());

        if (!result.IsConfirmedAccepted || result.ProviderReference is null)
        {
            long submissionVersion = transfer.Version;
            if (result.AcceptanceEvidence == ProviderAcceptanceEvidence.AcceptanceAmbiguous)
            {
                transfer.MarkUnknown(result.SafeMessage, timeProvider.GetUtcNow());
                FaultLedgerTelemetry.UnknownOutcomes.Add(1);
                await store.UpdateAsync(transfer, submissionVersion, CancellationToken.None,
                    new TransferAuditMetadata("provider-acceptance-ambiguous", "provider"));
                return new TransferCreateOutcome(TransferDetails.FromTransfer(transfer), false);
            }

            transfer.MarkFailed(result.SafeMessage, timeProvider.GetUtcNow());
            await store.UpdateAsync(transfer, submissionVersion, CancellationToken.None,
                new TransferAuditMetadata("provider-rejected-before-acceptance", "provider"));
            throw new ProviderSubmissionException(result);
        }

        expectedVersion = transfer.Version;
        transfer.MarkAccepted(result.ProviderReference, timeProvider.GetUtcNow());
        await store.UpdateAsync(transfer, expectedVersion, cancellationToken,
            new TransferAuditMetadata("provider-accepted", "provider"));
        return new TransferCreateOutcome(TransferDetails.FromTransfer(transfer), !registration.Created);
    }

    public async Task<TransferDetails?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Transfer? transfer = await store.FindAsync(id, cancellationToken);
        return transfer is null ? null : TransferDetails.FromTransfer(transfer);
    }
}

public sealed record TransferCreateOutcome(TransferDetails Details, bool IsReplay);
