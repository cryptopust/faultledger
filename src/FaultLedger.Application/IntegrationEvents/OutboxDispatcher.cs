namespace FaultLedger.Application.IntegrationEvents;

public sealed class OutboxDispatcher(
    IOutboxStore store,
    IIntegrationEventPublisher publisher,
    IOutboxDispatchHook dispatchHook,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public async Task<OutboxDispatchResult> DispatchNextAsync(string workerId,
        CancellationToken cancellationToken)
    {
        ValidateWorkerId(workerId);
        DateTimeOffset claimedAt = timeProvider.GetUtcNow();
        ClaimedOutboxMessage? claimed = await store.ClaimNextAsync(workerId, claimedAt,
            claimedAt.Add(ClaimLease), cancellationToken);
        if (claimed is null)
        {
            return OutboxDispatchResult.NoMessage;
        }

        try
        {
            await publisher.PublishAsync(claimed.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await RecordFailureAsync(claimed, "The outbound HTTP delivery timed out.", cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or
                                          IntegrationEventDeliveryException)
        {
            return await RecordFailureAsync(claimed,
                $"{exception.GetType().Name}: outbound integration-event delivery failed.", cancellationToken);
        }

        await dispatchHook.AfterRemotePublishBeforeLocalAckAsync(claimed.Message.EventId, cancellationToken);
        bool marked = await store.MarkPublishedAsync(claimed.Message.EventId, claimed.ClaimId,
            timeProvider.GetUtcNow(), cancellationToken);
        return marked ? OutboxDispatchResult.Published : OutboxDispatchResult.PublishedButClaimLost;
    }

    private async Task<OutboxDispatchResult> RecordFailureAsync(ClaimedOutboxMessage claimed, string error,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nextAttemptAt = timeProvider.GetUtcNow().Add(RetryDelay);
        bool recorded = await store.RecordFailureAsync(claimed.Message.EventId, claimed.ClaimId,
            nextAttemptAt, error, cancellationToken);
        return recorded ? OutboxDispatchResult.Failed : OutboxDispatchResult.FailedButClaimLost;
    }

    private static void ValidateWorkerId(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128 ||
            workerId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("Worker identity must contain 1-128 safe ASCII characters.", nameof(workerId));
        }
    }
}

public enum OutboxDispatchResult
{
    NoMessage,
    Published,
    Failed,
    PublishedButClaimLost,
    FailedButClaimLost
}
