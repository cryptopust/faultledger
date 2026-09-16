using System.Collections.Concurrent;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;

namespace FaultLedger.Application.Tests;

public sealed class TransferReconciliationServiceTests
{
    private static readonly Guid TransferId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ProviderLookupEvidence.ConfirmedAccepted, TransferState.Accepted, ReconciliationOutcome.ResolvedAccepted)]
    [InlineData(ProviderLookupEvidence.ConfirmedCompleted, TransferState.Completed, ReconciliationOutcome.ResolvedCompleted)]
    [InlineData(ProviderLookupEvidence.ConfirmedRejected, TransferState.Failed, ReconciliationOutcome.ResolvedRejected)]
    public async Task EvidenceBackedLookup_ResolvesUnknownWithoutSubmission(
        ProviderLookupEvidence evidence, TransferState expectedState, ReconciliationOutcome expectedOutcome)
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(For(evidence));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        ReconciliationDetails result = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(expectedState, Enum.Parse<TransferState>(result.Transfer.State));
        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, provider.SubmissionCalls);
        Assert.Equal(expectedState is TransferState.Completed or TransferState.Failed,
            result.Transfer.IsFinal);
    }

    [Theory]
    [InlineData(ProviderLookupEvidence.NotFound)]
    [InlineData(ProviderLookupEvidence.StillUnknown)]
    public async Task NonFinalLookupEvidence_PreservesUnknownAndDoNotRepost(ProviderLookupEvidence evidence)
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(For(evidence));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        ReconciliationDetails result = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(nameof(TransferState.Unknown), result.Transfer.State);
        Assert.False(result.Transfer.IsFinal);
        Assert.Equal(RetryAdvice.DoNotRepost, result.Transfer.RetryAdvice);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task NotFoundThenAccepted_ResolvesOnLaterExplicitReconciliationWithoutSubmission()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(
            ProviderLookupResult.NotFound(), ProviderLookupResult.ConfirmedAccepted("provider-accepted"));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        ReconciliationDetails first = await service.ReconcileAsync(TransferId, CancellationToken.None);
        ReconciliationDetails second = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(ReconciliationOutcome.NotFound, first.Outcome);
        Assert.Equal(ReconciliationOutcome.ResolvedAccepted, second.Outcome);
        Assert.Equal(nameof(TransferState.Accepted), second.Transfer.State);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task StillUnknownThenAccepted_ResolvesOnlyAfterConfirmedEvidenceWithoutSubmission()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(
            ProviderLookupResult.StillUnknown(), ProviderLookupResult.ConfirmedAccepted("provider-accepted"));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        ReconciliationDetails first = await service.ReconcileAsync(TransferId, CancellationToken.None);
        ReconciliationDetails second = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(ReconciliationOutcome.StillUnknown, first.Outcome);
        Assert.Equal(nameof(TransferState.Unknown), first.Transfer.State);
        Assert.Equal(ReconciliationOutcome.ResolvedAccepted, second.Outcome);
        Assert.Equal(nameof(TransferState.Accepted), second.Transfer.State);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task LookupTemporaryFailure_LeavesUnknownAndDoesNotInventRejection()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(ProviderLookupResult.TemporaryFailure("lookup unavailable"));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        await Assert.ThrowsAsync<ReconciliationUnavailableException>(() =>
            service.ReconcileAsync(TransferId, CancellationToken.None));

        Transfer current = Assert.IsType<Transfer>(await store.FindAsync(TransferId, CancellationToken.None));
        Assert.Equal(TransferState.Unknown, current.State);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task LookupFailureThenAccepted_LaterExplicitReconciliationCanResolveWithoutSubmission()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(ProviderLookupResult.TemporaryFailure("lookup unavailable"),
            ProviderLookupResult.ConfirmedAccepted("provider-accepted"));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        await Assert.ThrowsAsync<ReconciliationUnavailableException>(() =>
            service.ReconcileAsync(TransferId, CancellationToken.None));
        ReconciliationDetails later = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(ReconciliationOutcome.ResolvedAccepted, later.Outcome);
        Assert.Equal(nameof(TransferState.Accepted), later.Transfer.State);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task RepeatedReconciliationAfterResolution_IsIdempotentAndNeverSubmits()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(ProviderLookupResult.ConfirmedAccepted("provider-accepted"));
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        ReconciliationDetails first = await service.ReconcileAsync(TransferId, CancellationToken.None);
        ReconciliationDetails second = await service.ReconcileAsync(TransferId, CancellationToken.None);

        Assert.Equal(ReconciliationOutcome.ResolvedAccepted, first.Outcome);
        Assert.Equal(ReconciliationOutcome.AlreadyResolved, second.Outcome);
        Assert.Equal(nameof(TransferState.Accepted), second.Transfer.State);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task ConcurrentReconciliation_ReturnsDurableWinnerWithoutStateRegression()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new GateLookup(ProviderLookupResult.ConfirmedAccepted("provider-accepted"), 2);
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        Task<ReconciliationDetails> first = service.ReconcileAsync(TransferId, CancellationToken.None);
        Task<ReconciliationDetails> second = service.ReconcileAsync(TransferId, CancellationToken.None);
        await provider.WaitUntilReadyAsync();
        provider.Release();
        ReconciliationDetails[] results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(nameof(TransferState.Accepted), result.Transfer.State));
        Assert.Contains(results, result => result.Outcome == ReconciliationOutcome.ResolvedAccepted);
        Assert.Contains(results, result => result.Outcome == ReconciliationOutcome.AlreadyResolved);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    [Fact]
    public async Task CancellationDuringLookup_DoesNotMutateUnknown()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(ProviderLookupResult.ConfirmedAccepted("provider-accepted"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReconcileAsync(TransferId, cancellation.Token));

        Transfer current = Assert.IsType<Transfer>(await store.FindAsync(TransferId, CancellationToken.None));
        Assert.Equal(TransferState.Unknown, current.State);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ManualReviewEscape_IsExplicitAndDoesNotSubmit()
    {
        var store = new InMemoryTransferStore(CreateUnknown());
        var provider = new RecordingLookup(ProviderLookupResult.NotFound());
        var service = new TransferReconciliationService(store, provider, new FixedTimeProvider());

        TransferDetails result = await service.SendToManualReviewAsync(TransferId, "provider evidence requires review",
            CancellationToken.None);

        Assert.Equal(nameof(TransferState.ManualReview), result.State);
        Assert.Equal(RetryAdvice.DoNotRepost, result.RetryAdvice);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, provider.SubmissionCalls);
    }

    private static Transfer CreateUnknown()
    {
        Transfer transfer = Transfer.Create(TransferId, "order-unknown", "key-unknown", new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        transfer.BeginSubmission(Timestamp);
        transfer.MarkUnknown("response-lost-after-accept", Timestamp);
        return transfer;
    }

    private static ProviderLookupResult For(ProviderLookupEvidence evidence) => evidence switch
    {
        ProviderLookupEvidence.ConfirmedAccepted => ProviderLookupResult.ConfirmedAccepted("provider-accepted"),
        ProviderLookupEvidence.ConfirmedCompleted => ProviderLookupResult.ConfirmedCompleted("provider-completed"),
        ProviderLookupEvidence.ConfirmedRejected => ProviderLookupResult.ConfirmedRejected("provider-rejected"),
        ProviderLookupEvidence.NotFound => ProviderLookupResult.NotFound(),
        ProviderLookupEvidence.StillUnknown => ProviderLookupResult.StillUnknown(),
        _ => ProviderLookupResult.TemporaryFailure("lookup unavailable")
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private class RecordingLookup(params ProviderLookupResult[] results) : ITransferLookup
    {
        private readonly ConcurrentQueue<ProviderLookupResult> sequence = new(results);
        public int Calls { get; private set; }
        public int SubmissionCalls { get; private set; }

        public virtual Task<ProviderLookupResult> LookupAsync(ProviderLookupRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            ProviderLookupResult result = sequence.TryDequeue(out ProviderLookupResult? configured)
                ? configured
                : results[^1];
            return Task.FromResult(result);
        }
    }

    private sealed class GateLookup(ProviderLookupResult result, int expectedCalls) : RecordingLookup(result)
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public async Task WaitUntilReadyAsync()
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Release() => release.SetResult();

        public override async Task<ProviderLookupResult> LookupAsync(ProviderLookupRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            if (calls == expectedCalls)
            {
                ready.SetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            return await base.LookupAsync(request, cancellationToken);
        }
    }

    private sealed class InMemoryTransferStore(Transfer initial) : ITransferStore
    {
        private Transfer current = Clone(initial);
        private long currentVersion = initial.Version;

        public Task AddAsync(Transfer transfer, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint,
            int fingerprintVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Transfer?>(id == current.Id ? Clone(current) : null);
        }

        public Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transfer.Id != current.Id || Interlocked.CompareExchange(ref currentVersion,
                    transfer.Version, expectedVersion) != expectedVersion)
            {
                throw new TransferConcurrencyException();
            }

            current = Clone(transfer);
            return Task.CompletedTask;
        }

        private static Transfer Clone(Transfer source) => Transfer.Restore(source.Id, source.ClientReference,
            source.IdempotencyKey, source.Money, source.State, source.ProviderReference,
            source.CreatedAt, source.UpdatedAt, source.Version);
    }
}
