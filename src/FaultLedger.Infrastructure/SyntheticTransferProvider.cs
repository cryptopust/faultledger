using FaultLedger.Application.Transfers;

namespace FaultLedger.Infrastructure;

public sealed class SyntheticTransferProvider(
    MockProviderLedger ledger,
    MockProviderScenario scenario,
    TimeProvider timeProvider,
    MockProviderSlowResponseGate? slowResponseGate = null) : ITransferProvider
{
    private readonly MockProviderScenarioDefinition definition = MockProviderScenarioCatalog.Get(scenario);

    public async Task<ProviderSubmissionResult> SubmitAsync(ProviderTransferRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (scenario == MockProviderScenario.SlowResponse && slowResponseGate is null)
        {
            throw new ArgumentException("SlowResponse requires a controlled response gate.", nameof(slowResponseGate));
        }

        long attemptNumber = ledger.RecordSubmissionAttempt(request, scenario, definition.RequestReachesProvider,
            timeProvider.GetUtcNow());
        if (scenario == MockProviderScenario.SlowResponse)
        {
            await slowResponseGate!.WaitForReleaseAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!definition.ProviderAccepts)
        {
            return definition.AcceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedRejected
                ? ProviderSubmissionResult.ConfirmedRejected(definition.SafeMessage)
                : ProviderSubmissionResult.DefinitelyNotAccepted(definition.FailureKind, definition.SafeMessage);
        }

        // Beyond this point the simulated provider has accepted the operation.
        // Any lost response must be treated as an ambiguous external outcome.
        string providerReference = ledger.RecordAcceptance(request, scenario, timeProvider.GetUtcNow(), attemptNumber);
        return definition.AcceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedAccepted
            ? ProviderSubmissionResult.ConfirmedAccepted(providerReference)
            : ProviderSubmissionResult.AcceptanceAmbiguous(definition.FailureKind, definition.SafeMessage);
    }
}
