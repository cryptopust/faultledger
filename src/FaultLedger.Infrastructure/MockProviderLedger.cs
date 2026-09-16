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
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ProviderLookupResult>> lookupSequences = new();
    private readonly ConcurrentDictionary<Guid, ProviderLookupResult> lookupFallbacks = new();
    private long submissionAttempts;
    private long lookupAttempts;

    public long SubmissionAttempts => Interlocked.Read(ref submissionAttempts);

    public long LookupAttempts => Interlocked.Read(ref lookupAttempts);

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

    internal long RecordLookupAttempt() => Interlocked.Increment(ref lookupAttempts);

    public void ConfigureLookupSequence(Guid transferId, params ProviderLookupResult[] results)
    {
        if (transferId == Guid.Empty)
        {
            throw new ArgumentException("Provider lookup correlation cannot be empty.", nameof(transferId));
        }

        ArgumentNullException.ThrowIfNull(results);
        if (results.Length == 0 || results.Any(result => result is null))
        {
            throw new ArgumentException("At least one deterministic lookup result is required.", nameof(results));
        }

        lookupSequences[transferId] = new ConcurrentQueue<ProviderLookupResult>(results);
        lookupFallbacks[transferId] = results[^1];
    }

    internal ProviderLookupResult Lookup(Guid transferId)
    {
        RecordLookupAttempt();
        if (lookupSequences.TryGetValue(transferId, out ConcurrentQueue<ProviderLookupResult>? sequence) &&
            sequence.TryDequeue(out ProviderLookupResult? configured))
        {
            return configured;
        }

        if (lookupFallbacks.TryGetValue(transferId, out ProviderLookupResult? fallback))
        {
            return fallback;
        }

        MockProviderOperation? operation = acceptedOperations.Values
            .Where(candidate => candidate.TransferId == transferId)
            .OrderBy(candidate => candidate.SubmissionAttempt)
            .SingleOrDefault();
        return operation is null
            ? ProviderLookupResult.NotFound()
            : ProviderLookupResult.ConfirmedAccepted(operation.ProviderReference);
    }

    public bool TryGetAcceptedOperation(string providerReference, out MockProviderOperation? operation) =>
        acceptedOperations.TryGetValue(providerReference, out operation);

    public IReadOnlyList<MockProviderOperation> FindAcceptedOperations(Guid transferId) => acceptedOperations.Values
        .Where(operation => operation.TransferId == transferId)
        .OrderBy(operation => operation.SubmissionAttempt)
        .ToArray();
}
