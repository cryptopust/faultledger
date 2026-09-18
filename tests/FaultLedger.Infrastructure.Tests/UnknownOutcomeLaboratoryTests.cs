using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;

namespace FaultLedger.Infrastructure.Tests;

public sealed class UnknownOutcomeLaboratoryTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly CreateTransferCommand Command = new("order-unknown", "key-unknown", 42.50m, "USD");

    [Theory]
    [InlineData(MockProviderScenario.TimeoutAfterAccept)]
    [InlineData(MockProviderScenario.ConnectionLostAfterAccept)]
    [InlineData(MockProviderScenario.Provider500AfterAccept)]
    [InlineData(MockProviderScenario.MalformedResponseAfterAccept)]
    [Trait("Category", "UnknownOutcomeLaboratory")]
    public async Task AmbiguousAcceptance_ReplayAndReconciliationNeverRepost(MockProviderScenario scenario)
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, scenario, new FixedTimeProvider());
        var store = new LaboratoryStore();
        var transferService = new TransferService(store, provider, new FixedTimeProvider());
        var reconciliationService = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        TransferCreateOutcome first = await transferService.CreateWithOutcomeAsync(Command, CancellationToken.None);
        Assert.Equal(nameof(TransferState.Unknown), first.Details.State);
        Assert.Equal(RetryAdvice.DoNotRepost, first.Details.RetryAdvice);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);

        TransferCreateOutcome replay = await transferService.CreateWithOutcomeAsync(Command, CancellationToken.None);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Details.Id, replay.Details.Id);
        Assert.Equal(nameof(TransferState.Unknown), replay.Details.State);
        Assert.Equal(1, ledger.SubmissionAttempts);

        ReconciliationDetails reconciliation = await reconciliationService.ReconcileAsync(first.Details.Id,
            CancellationToken.None);
        Assert.Equal(ReconciliationOutcome.ResolvedAccepted, reconciliation.Outcome);
        Assert.Equal(nameof(TransferState.Accepted), reconciliation.Transfer.State);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
        Assert.Equal(1, ledger.LookupAttempts);
    }

    [Theory]
    [InlineData(MockProviderScenario.TimeoutBeforeAccept)]
    [InlineData(MockProviderScenario.ConnectionFailureBeforeAccept)]
    [InlineData(MockProviderScenario.Provider500BeforeAccept)]
    [InlineData(MockProviderScenario.Rejected)]
    [Trait("Category", "UnknownOutcomeLaboratory")]
    public async Task KnownNonAcceptanceOrRejection_BecomesFailedInsteadOfUnknown(MockProviderScenario scenario)
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, scenario, new FixedTimeProvider());
        var store = new LaboratoryStore();
        var service = new TransferService(store, provider, new FixedTimeProvider());

        await Assert.ThrowsAsync<ProviderSubmissionException>(() =>
            service.CreateAsync(Command, CancellationToken.None));

        Transfer current = Assert.IsType<Transfer>(await store.FindCurrentAsync());
        Assert.Equal(TransferState.Failed, current.State);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(0, ledger.AcceptedOperationCount);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private sealed class LaboratoryStore : ITransferStore
    {
        private Transfer? current;
        private string? fingerprint;
        private int? fingerprintVersion;

        public Task<Transfer?> FindCurrentAsync() => Task.FromResult(current is null ? null : Clone(current));

        public Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint,
            int requestFingerprintVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not null)
            {
                return Task.FromResult(new TransferRegistration(Clone(current), fingerprint,
                    fingerprintVersion, false));
            }

            current = Clone(transfer);
            fingerprint = requestFingerprint;
            fingerprintVersion = requestFingerprintVersion;
            return Task.FromResult(new TransferRegistration(Clone(current), fingerprint,
                fingerprintVersion, true));
        }

        public Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null)
            {
                return Task.FromResult(SubmissionClaimResult.NotFound);
            }

            if (current.State != TransferState.ReadyToSubmit || current.Version != expectedVersion)
            {
                return Task.FromResult(SubmissionClaimResult.AlreadyClaimedOrSubmitted);
            }

            current = Clone(transfer);
            return Task.FromResult(SubmissionClaimResult.ClaimAcquired);
        }

        public Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(current?.Id == id ? Clone(current) : null);
        }

        public Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken,
            TransferAuditMetadata? audit = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || current.Id != transfer.Id || current.Version != expectedVersion)
            {
                throw new TransferConcurrencyException();
            }

            current = Clone(transfer);
            return Task.CompletedTask;
        }

        public Task AddAsync(Transfer transfer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static Transfer Clone(Transfer source) => Transfer.Restore(source.Id, source.ClientReference,
            source.IdempotencyKey, source.Money, source.State, source.ProviderReference,
            source.CreatedAt, source.UpdatedAt, source.Version);
    }
}
