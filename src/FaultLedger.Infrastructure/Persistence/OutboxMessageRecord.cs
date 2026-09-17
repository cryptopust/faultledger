using FaultLedger.Application.IntegrationEvents;
using FaultLedger.Domain;

namespace FaultLedger.Infrastructure.Persistence;

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? ClaimId { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessageRecord FromCompletedTransfer(Transfer transfer)
    {
        IntegrationEventMessage message = TransferCompletedIntegrationEvent.Create(transfer, Guid.NewGuid());
        return new OutboxMessageRecord
        {
            Id = message.EventId,
            AggregateId = message.AggregateId,
            AggregateVersion = message.AggregateVersion,
            EventType = message.EventType,
            SchemaVersion = message.SchemaVersion,
            Payload = message.Payload,
            CreatedAt = message.OccurredAt,
            NextAttemptAt = message.OccurredAt
        };
    }

    public IntegrationEventMessage ToMessage() => new(Id, EventType, SchemaVersion, AggregateId,
        AggregateVersion, CreatedAt, Payload);
}
