using System.Text.Json;
using FaultLedger.Domain;

namespace FaultLedger.Application.IntegrationEvents;

public static class IntegrationEventTypes
{
    public const string TransferCompleted = nameof(TransferCompleted);
}

public sealed record IntegrationEventMessage(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    Guid AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAt,
    string Payload);

public sealed record TransferCompletedPayload(
    Guid TransferId,
    string ClientReference,
    decimal Amount,
    string Currency,
    DateTimeOffset CompletedAt);

public static class TransferCompletedIntegrationEvent
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IntegrationEventMessage Create(Transfer transfer, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Integration event identity cannot be empty.", nameof(eventId));
        }

        if (transfer.State != TransferState.Completed)
        {
            throw new InvalidOperationException("Only a completed transfer can produce TransferCompleted.");
        }

        var payload = new TransferCompletedPayload(transfer.Id, transfer.ClientReference, transfer.Money.Amount,
            transfer.Money.Currency, transfer.UpdatedAt);
        return new IntegrationEventMessage(eventId, IntegrationEventTypes.TransferCompleted, SchemaVersion,
            transfer.Id, transfer.Version, transfer.UpdatedAt, JsonSerializer.Serialize(payload, SerializerOptions));
    }
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    Task<ClaimedOutboxMessage?> ClaimNextAsync(string workerId, DateTimeOffset claimedAt,
        DateTimeOffset claimedUntil, CancellationToken cancellationToken);

    Task<bool> MarkPublishedAsync(Guid eventId, Guid claimId, DateTimeOffset publishedAt,
        CancellationToken cancellationToken);

    Task<bool> RecordFailureAsync(Guid eventId, Guid claimId, DateTimeOffset nextAttemptAt,
        string error, CancellationToken cancellationToken);
}

public sealed record ClaimedOutboxMessage(IntegrationEventMessage Message, int AttemptCount, string WorkerId,
    Guid ClaimId, DateTimeOffset ClaimedUntil);

public interface IOutboxDispatchHook
{
    Task AfterRemotePublishBeforeLocalAckAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed class NoOpOutboxDispatchHook : IOutboxDispatchHook
{
    public Task AfterRemotePublishBeforeLocalAckAsync(Guid eventId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public interface IOutboxPersistenceHook
{
    Task BeforeCommitAsync(Guid aggregateId, CancellationToken cancellationToken);
}

public sealed class NoOpOutboxPersistenceHook : IOutboxPersistenceHook
{
    public Task BeforeCommitAsync(Guid aggregateId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class IntegrationEventDeliveryException(string message) : InvalidOperationException(message);
