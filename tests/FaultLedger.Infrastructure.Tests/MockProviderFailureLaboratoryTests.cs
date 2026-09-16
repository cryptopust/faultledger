using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;

namespace FaultLedger.Infrastructure.Tests;

public sealed class MockProviderFailureLaboratoryTests
{
    private static readonly DateTimeOffset AttemptedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TransferId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static TheoryData<MockProviderScenario, ProviderAcceptanceEvidence, ProviderFailureKind, bool, bool, bool>
        ScenarioEvidence => new()
        {
            { MockProviderScenario.Success, ProviderAcceptanceEvidence.ConfirmedAccepted, ProviderFailureKind.None, true, true, true },
            { MockProviderScenario.Rejected, ProviderAcceptanceEvidence.ConfirmedRejected, ProviderFailureKind.Rejected, true, false, false },
            { MockProviderScenario.TimeoutBeforeAccept, ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.Timeout, true, false, false },
            { MockProviderScenario.TimeoutAfterAccept, ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.Timeout, true, true, false },
            { MockProviderScenario.ConnectionFailureBeforeAccept, ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.ConnectionFailure, false, false, false },
            { MockProviderScenario.ConnectionLostAfterAccept, ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.ConnectionFailure, true, true, false },
            { MockProviderScenario.Provider500BeforeAccept, ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.ServerError, true, false, false },
            { MockProviderScenario.Provider500AfterAccept, ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.ServerError, true, true, false },
            { MockProviderScenario.MalformedResponseAfterAccept, ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.MalformedResponse, true, true, false },
        };

    [Fact]
    [Trait("Category", "ProviderFailureLaboratory")]
    public void UnknownScenarioValue_IsRejectedInsteadOfFallingBackToSuccess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MockProviderScenarioCatalog.Get((MockProviderScenario)999));
    }

    [Theory]
    [MemberData(nameof(ScenarioEvidence))]
    [Trait("Category", "ProviderFailureLaboratory")]
    public async Task ExplicitScenario_RecordsAttemptAndSeparatesAcceptanceTruth(
        MockProviderScenario scenario,
        ProviderAcceptanceEvidence expectedEvidence,
        ProviderFailureKind expectedFailureKind,
        bool requestReachesProvider,
        bool providerAccepts,
        bool callerGetsSuccess)
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, scenario, new FixedTimeProvider());

        ProviderSubmissionResult result = await provider.SubmitAsync(CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(providerAccepts ? 1 : 0, ledger.AcceptedOperationCount);
        Assert.Equal(expectedEvidence, result.AcceptanceEvidence);
        Assert.Equal(expectedFailureKind, result.FailureKind);
        Assert.Equal(callerGetsSuccess, result.IsConfirmedAccepted);
        Assert.Equal(requestReachesProvider, ledger.SubmissionHistory.Single().RequestReachedProvider);
        if (providerAccepts)
        {
            MockProviderOperation operation = Assert.Single(ledger.FindAcceptedOperations(TransferId));
            Assert.Equal(TransferId, operation.TransferId);
            Assert.Equal(scenario, operation.Scenario);
            Assert.Equal(expectedEvidence == ProviderAcceptanceEvidence.ConfirmedAccepted
                ? operation.ProviderReference
                : null, result.ProviderReference);
            Assert.True(ledger.TryGetAcceptedOperation(operation.ProviderReference, out MockProviderOperation? lookedUp));
            Assert.Equal(operation, lookedUp);
        }
        else
        {
            Assert.Empty(ledger.AcceptedOperations);
            Assert.Null(result.ProviderReference);
        }
    }

    [Fact]
    [Trait("Category", "ProviderFailureLaboratory")]
    public async Task TimeoutAfterProviderAcceptance_ProviderOperationExistsDespiteCallerFailure()
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.TimeoutAfterAccept,
            new FixedTimeProvider());

        ProviderSubmissionResult result = await provider.SubmitAsync(CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsConfirmedAccepted);
        Assert.Equal(ProviderAcceptanceEvidence.AcceptanceAmbiguous, result.AcceptanceEvidence);
        Assert.Equal(1, ledger.SubmissionAttempts);
        MockProviderOperation operation = Assert.Single(ledger.AcceptedOperations);
        Assert.Equal(operation.ProviderReference, ledger.FindAcceptedOperations(TransferId).Single().ProviderReference);
    }

    [Fact]
    [Trait("Category", "ProviderFailureLaboratory")]
    public async Task SlowResponse_UsesControlledGateBeforeAcceptanceAndCompletesAfterRelease()
    {
        var ledger = new MockProviderLedger();
        var gate = new MockProviderSlowResponseGate();
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.SlowResponse,
            new FixedTimeProvider(), gate);

        Task<ProviderSubmissionResult> submission = provider.SubmitAsync(CreateRequest(),
            TestContext.Current.CancellationToken);
        await gate.WaitUntilReachedAsync(TestContext.Current.CancellationToken);

        Assert.False(submission.IsCompleted);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(0, ledger.AcceptedOperationCount);

        gate.Release();
        ProviderSubmissionResult result = await submission;
        Assert.True(result.IsConfirmedAccepted);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    [Trait("Category", "ProviderFailureLaboratory")]
    public async Task CancellationBeforeAcceptance_IsNotRewrittenAsProviderRejection()
    {
        var ledger = new MockProviderLedger();
        var gate = new MockProviderSlowResponseGate();
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.SlowResponse,
            new FixedTimeProvider(), gate);
        using var cancellation = new CancellationTokenSource();

        Task<ProviderSubmissionResult> submission = provider.SubmitAsync(CreateRequest(), cancellation.Token);
        await gate.WaitUntilReachedAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Empty(ledger.AcceptedOperations);
    }

    [Fact]
    [Trait("Category", "ProviderFailureLaboratory")]
    public async Task ConcurrentMockProviderCalls_KeepAttemptAndAcceptanceLedgerConsistent()
    {
        const int submissionCount = 64;
        var ledger = new MockProviderLedger();
        var gate = new MockProviderSlowResponseGate(submissionCount);
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.SlowResponse,
            new FixedTimeProvider(), gate);
        Task<ProviderSubmissionResult>[] submissions = Enumerable.Range(0, submissionCount)
            .Select(index => provider.SubmitAsync(CreateRequest(index), TestContext.Current.CancellationToken))
            .ToArray();

        await gate.WaitUntilReachedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(submissionCount, ledger.SubmissionAttempts);
        Assert.Equal(0, ledger.AcceptedOperationCount);
        gate.Release();

        ProviderSubmissionResult[] results = await Task.WhenAll(submissions);
        Assert.All(results, result => Assert.True(result.IsConfirmedAccepted));
        Assert.Equal(submissionCount, ledger.AcceptedOperationCount);
        Assert.Equal(submissionCount, ledger.AcceptedOperations.Select(operation => operation.ProviderReference).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, submissionCount), ledger.SubmissionHistory.Select(attempt => (int)attempt.AttemptNumber));
    }

    private static ProviderTransferRequest CreateRequest(int index = 0) => new(
        index == 0 ? TransferId : Guid.Parse($"22222222-2222-2222-2222-{index:D12}"),
        $"order-{index:D4}", new Money(10.25m, "USD"));

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => AttemptedAt;
    }
}
