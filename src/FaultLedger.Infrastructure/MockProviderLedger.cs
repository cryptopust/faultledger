using System.Collections.Concurrent;
using FaultLedger.Application.Transfers;

namespace FaultLedger.Infrastructure;

public sealed record MockProviderSubmissionAttempt(
    long AttemptNumber,
    Guid TransferId,
    string ClientReference,
    decimal Amount,
    string Currency,
    MockProviderScenario Scenario,
    bool RequestReachedProvider,
    DateTimeOffset AttemptedAt);

public sealed record MockProviderOperation(
    string ProviderReference,
    Guid TransferId,
    string ClientReference,
    decimal Amount,
    string Currency,
    DateTimeOffset AcceptedAt,
    string Status,
    MockProviderScenario Scenario,
    long SubmissionAttempt);

/// <summary>
/// Simulated external-provider truth for the failure laboratory.
/// It is deliberately process-local fake-provider state, never FaultLedger durable correctness state.
/// </summary>
public sealed class MockProviderLedger
{
    private readonly ConcurrentQueue<MockProviderSubmissionAttempt> submissionHistory = new();
    private readonly ConcurrentDictionary<string, MockProviderOperation> acceptedOperations = new(StringComparer.Ordinal);
    private long submissionAttempts;

    public long SubmissionAttempts => Interlocked.Read(ref submissionAttempts);

    public int AcceptedOperationCount => acceptedOperations.Count;

    public IReadOnlyList<MockProviderSubmissionAttempt> SubmissionHistory => submissionHistory
        .OrderBy(attempt => attempt.AttemptNumber)
        .ToArray();

    public IReadOnlyList<MockProviderOperation> AcceptedOperations => acceptedOperations.Values
        .OrderBy(operation => operation.SubmissionAttempt)
        .ToArray();

    internal long RecordSubmissionAttempt(ProviderTransferRequest request, MockProviderScenario scenario,
        bool requestReachedProvider, DateTimeOffset attemptedAt)
    {
        long attemptNumber = Interlocked.Increment(ref submissionAttempts);
        submissionHistory.Enqueue(new MockProviderSubmissionAttempt(attemptNumber, request.TransferId,
            request.ClientReference, request.Money.Amount, request.Money.Currency, scenario, requestReachedProvider,
            attemptedAt));
        return attemptNumber;
    }

    internal string RecordAcceptance(ProviderTransferRequest request, MockProviderScenario scenario,
        DateTimeOffset acceptedAt, long submissionAttempt)
    {
        string providerReference = $"mock-{request.TransferId:N}-{submissionAttempt:D8}";
        var operation = new MockProviderOperation(providerReference, request.TransferId, request.ClientReference,
            request.Money.Amount, request.Money.Currency, acceptedAt, "Accepted", scenario, submissionAttempt);
        if (!acceptedOperations.TryAdd(providerReference, operation))
        {
            throw new InvalidOperationException("The synthetic provider generated a duplicate reference.");
        }

        return providerReference;
    }

    public bool TryGetAcceptedOperation(string providerReference, out MockProviderOperation? operation) =>
        acceptedOperations.TryGetValue(providerReference, out operation);

    public IReadOnlyList<MockProviderOperation> FindAcceptedOperations(Guid transferId) => acceptedOperations.Values
        .Where(operation => operation.TransferId == transferId)
        .OrderBy(operation => operation.SubmissionAttempt)
        .ToArray();
}
