using FaultLedger.Application.Transfers;
using FaultLedger.Domain;

namespace FaultLedger.Application.Tests;

public sealed class TransferServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly CreateTransferCommand ValidCommand = new("order-1001", "order-1001-attempt", 125.50m, "usd");

    [Theory]
    [InlineData("10", "10.0")]
    [InlineData("10.0", "10.00")]
    public void Fingerprint_EquivalentDecimalScalesProduceSameVersionedHash(string first, string second)
    {
        var left = ValidCommand with { Amount = decimal.Parse(first, System.Globalization.CultureInfo.InvariantCulture) };
        var right = ValidCommand with { Amount = decimal.Parse(second, System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Equal(TransferRequestFingerprint.Compute(left), TransferRequestFingerprint.Compute(right));
        Assert.Equal(64, TransferRequestFingerprint.Compute(left).Length);
        Assert.Equal(1, TransferRequestFingerprint.CurrentVersion);
    }

    [Fact]
    public void Fingerprint_CanonicalizesCurrencyButPreservesLogicalBusinessFields()
    {
        string fingerprint = TransferRequestFingerprint.Compute(ValidCommand);
        Assert.Equal(fingerprint, TransferRequestFingerprint.Compute(ValidCommand with { Currency = "USD" }));
        Assert.NotEqual(fingerprint, TransferRequestFingerprint.Compute(ValidCommand with { Amount = 125.51m }));
        Assert.NotEqual(fingerprint, TransferRequestFingerprint.Compute(ValidCommand with { ClientReference = "order-1002" }));
    }

    [Fact]
    public async Task ValidCommand_PersistsSubmittingBeforeProviderThenAcceptedWithReference()
    {
        var store = new RecordingStore();
        var provider = new RecordingProvider((request, _) =>
        {
            Assert.Equal(new[] { "ReadyToSubmit", "Submitting" }, store.Saved.Select(item => item.State));
            Assert.Equal(request.TransferId, store.Saved[^1].Id);
            Assert.Equal(request.Money.Amount, store.Saved[^1].Amount);
            return Task.FromResult(new ProviderSubmissionResult("synthetic-accepted"));
        });
        var service = new TransferService(store, provider, new FixedTimeProvider());

        TransferDetails result = await service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken);

        Assert.Equal("Accepted", result.State);
        Assert.Equal("USD", result.Currency);
        Assert.Equal("synthetic-accepted", store.Saved[^1].ProviderReference);
        Assert.Equal(new long[] { 1, 2, 3 }, store.Saved.Select(item => item.Version));
        Assert.All(store.Saved, item => Assert.Equal(Timestamp, item.UpdatedAt));
        Assert.Equal(1, provider.Calls);
        Assert.All(store.Tokens, token => Assert.Equal(TestContext.Current.CancellationToken, token));
        Assert.Equal(TestContext.Current.CancellationToken, provider.LastToken);
    }

    [Theory]
    [InlineData("0", "USD")]
    [InlineData("-1", "USD")]
    [InlineData("1", "12A")]
    [InlineData("1.000000001", "USD")]
    public async Task InvalidCommand_IsRejectedBeforePersistenceAndProvider(string amount, string currency)
    {
        var store = new RecordingStore();
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());
        var command = ValidCommand with { Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency = currency };

        await Assert.ThrowsAsync<TransferValidationException>(() => service.CreateAsync(command, TestContext.Current.CancellationToken));

        Assert.Empty(store.Saved);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AlreadyCancelledCommand_PerformsNoPersistenceOrSubmission()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingStore();
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(ValidCommand, cancellation.Token));
        Assert.Empty(store.Saved);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task CancellationDuringProvider_PersistsUnknownAndNeverRecordsFailedOrRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore();
        var provider = new RecordingProvider((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation must propagate.");
        });
        var service = new TransferService(store, provider, new FixedTimeProvider());
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(ValidCommand, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal("Unknown", store.Saved[^1].State);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PersistenceFailureBeforeDispatch_PreventsProviderCall(int failingWrite)
    {
        var store = new RecordingStore { FailingWrite = failingWrite };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());
        await Assert.ThrowsAsync<TransferStorageUnavailableException>(() => service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AcceptedSaveFailure_LeavesDurableIntentWithoutAutomaticRepost()
    {
        var store = new RecordingStore { FailingWrite = 2 };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());
        await Assert.ThrowsAsync<TransferStorageUnavailableException>(() => service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken));
        Assert.Equal("Submitting", store.Saved[^1].State);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(ProviderAcceptanceEvidence.ConfirmedRejected, ProviderFailureKind.Rejected)]
    [InlineData(ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.Timeout)]
    [InlineData(ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.ServerError)]
    public async Task NonAcceptedProviderEvidence_MapsKnownAndAmbiguousEvidenceToSafeStates(
        ProviderAcceptanceEvidence evidence, ProviderFailureKind failureKind)
    {
        var store = new RecordingStore();
        var provider = new RecordingProvider((_, _) => Task.FromResult(new ProviderSubmissionResult(
            evidence, null, failureKind, "synthetic provider outcome")));
        var service = new TransferService(store, provider, new FixedTimeProvider());

        if (evidence == ProviderAcceptanceEvidence.AcceptanceAmbiguous)
        {
            TransferDetails details = await service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken);
            Assert.Equal("Unknown", details.State);
            Assert.False(details.IsFinal);
            Assert.Equal(RetryAdvice.DoNotRepost, details.RetryAdvice);
        }
        else
        {
            ProviderSubmissionException exception = await Assert.ThrowsAsync<ProviderSubmissionException>(() =>
                service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken));
            Assert.Equal(evidence, exception.Result.AcceptanceEvidence);
            Assert.Equal(failureKind, exception.Result.FailureKind);
            Assert.Equal("Failed", store.Saved[^1].State);
            Assert.True(store.Saved[^1].IsFinal);
        }
        Assert.Equal(1, provider.Calls);
        Assert.DoesNotContain(store.Saved, item => item.State == "Accepted");
    }

    [Fact]
    public async Task ExistingKeyConflict_IsNotReinterpretedAsAnotherSubmission()
    {
        var store = new RecordingStore { DuplicateKey = true };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());
        await Assert.ThrowsAsync<DuplicateTransferKeyException>(() => service.CreateAsync(ValidCommand, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExistingSameRequest_ReplaysDurableTransferWithoutSubmission()
    {
        var existing = Transfer.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ValidCommand.ClientReference, ValidCommand.IdempotencyKey,
            new Money(ValidCommand.Amount, ValidCommand.Currency), Timestamp);
        existing.MarkReadyToSubmit(Timestamp);
        existing.BeginSubmission(Timestamp);
        var store = new RecordingStore { Existing = existing };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());

        TransferCreateOutcome result = await service.CreateWithOutcomeAsync(ValidCommand,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsReplay);
        Assert.Equal(existing.Id, result.Details.Id);
        Assert.Equal("Submitting", result.Details.State);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExistingUnknownRequest_ReplaysDoNotRepostWithoutSubmission()
    {
        var existing = Transfer.Create(Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ValidCommand.ClientReference, ValidCommand.IdempotencyKey,
            new Money(ValidCommand.Amount, ValidCommand.Currency), Timestamp);
        existing.MarkReadyToSubmit(Timestamp);
        existing.BeginSubmission(Timestamp);
        existing.MarkUnknown("response-lost-after-accept", Timestamp);
        var store = new RecordingStore { Existing = existing };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());

        TransferCreateOutcome result = await service.CreateWithOutcomeAsync(ValidCommand,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsReplay);
        Assert.Equal(existing.Id, result.Details.Id);
        Assert.Equal("Unknown", result.Details.State);
        Assert.False(result.Details.IsFinal);
        Assert.Equal(RetryAdvice.DoNotRepost, result.Details.RetryAdvice);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExistingReadyToSubmitRequest_ReplaysWithoutStealingSubmissionClaim()
    {
        var existing = Transfer.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ValidCommand.ClientReference, ValidCommand.IdempotencyKey,
            new Money(ValidCommand.Amount, ValidCommand.Currency), Timestamp);
        existing.MarkReadyToSubmit(Timestamp);
        var store = new RecordingStore { Existing = existing };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());

        TransferCreateOutcome result = await service.CreateWithOutcomeAsync(ValidCommand,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsReplay);
        Assert.Equal("ReadyToSubmit", result.Details.State);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, store.ClaimCalls);
    }

    [Fact]
    public async Task ExistingDifferentRequest_ConflictsBeforeSubmission()
    {
        var existing = Transfer.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ValidCommand.ClientReference, ValidCommand.IdempotencyKey,
            new Money(ValidCommand.Amount, ValidCommand.Currency), Timestamp);
        existing.MarkReadyToSubmit(Timestamp);
        var store = new RecordingStore { Existing = existing };
        var provider = new RecordingProvider();
        var service = new TransferService(store, provider, new FixedTimeProvider());

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            service.CreateAsync(ValidCommand with { Amount = 200m }, TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.Calls);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private sealed class RecordingStore : ITransferStore
    {
        public List<TransferDetails> Saved { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public int? FailingWrite { get; init; }
        public bool DuplicateKey { get; init; }
        public Transfer? Existing { get; init; }
        public int ClaimCalls { get; private set; }

        public Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint,
            int fingerprintVersion, CancellationToken cancellationToken)
        {
            if (DuplicateKey)
            {
                throw new DuplicateTransferKeyException();
            }

            if (Existing is not null)
            {
                return Task.FromResult(new TransferRegistration(Existing,
                    TransferRequestFingerprint.Compute(Existing.ClientReference, Existing.Money.Amount,
                        Existing.Money.Currency), fingerprintVersion, false));
            }

            RecordAsync(transfer, cancellationToken);
            return Task.FromResult(new TransferRegistration(transfer, requestFingerprint, fingerprintVersion, true));
        }

        public async Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
            CancellationToken cancellationToken)
        {
            ClaimCalls++;
            await UpdateAsync(transfer, expectedVersion, cancellationToken);
            return SubmissionClaimResult.ClaimAcquired;
        }

        public Task AddAsync(Transfer transfer, CancellationToken cancellationToken)
        {
            if (DuplicateKey)
            {
                throw new DuplicateTransferKeyException();
            }

            return RecordAsync(transfer, cancellationToken);
        }

        public Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken)
        {
            Assert.Equal(Saved[^1].Version, expectedVersion);
            return RecordAsync(transfer, cancellationToken);
        }

        public Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double proves orchestration ordering, not persistence.");

        private Task RecordAsync(Transfer transfer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Tokens.Add(cancellationToken);
            if (FailingWrite == Saved.Count)
            {
                throw new TransferStorageUnavailableException();
            }

            Saved.Add(TransferDetails.FromTransfer(transfer));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProvider(
        Func<ProviderTransferRequest, CancellationToken, Task<ProviderSubmissionResult>>? submit = null) : ITransferProvider
    {
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<ProviderSubmissionResult> SubmitAsync(ProviderTransferRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = cancellationToken;
            return submit?.Invoke(request, cancellationToken) ?? Task.FromResult(new ProviderSubmissionResult("synthetic-accepted"));
        }
    }
}
