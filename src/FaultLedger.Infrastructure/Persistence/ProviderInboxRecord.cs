using FaultLedger.Application.Transfers;

namespace FaultLedger.Infrastructure.Persistence;

internal sealed class ProviderInboxRecord
{
    public Guid InboxId { get; set; }
    public required string ProviderEventId { get; set; }
    public Guid ProviderOperationCorrelation { get; set; }
    public required string EventType { get; set; }
    public string? ProviderReference { get; set; }
    public required string Evidence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string NormalizedPayload { get; set; }
    public int PayloadVersion { get; set; }
    public required string ProcessingStatus { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public Guid? TransferId { get; set; }

    public ProviderCallbackEnvelope ToEnvelope() => new(ProviderEventId, ProviderOperationCorrelation,
        Enum.Parse<ProviderCallbackEventType>(EventType), ProviderReference, Evidence, OccurredAt);
}
