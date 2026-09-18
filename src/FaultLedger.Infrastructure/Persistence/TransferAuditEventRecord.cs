using FaultLedger.Domain;

namespace FaultLedger.Infrastructure.Persistence;

internal sealed class TransferAuditEventRecord
{
    public Guid EventId { get; set; }
    public Guid TransferId { get; set; }
    public string? PreviousState { get; set; }
    public required string NewState { get; set; }
    public required string Reason { get; set; }
    public required string Source { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public long TransitionVersion { get; set; }

    public static TransferAuditEventRecord Create(
        Transfer transfer,
        TransferState? previousState,
        string reason,
        string source,
        long transitionVersion,
        DateTimeOffset occurredAt,
        TransferState? newState = null) => new()
        {
            EventId = Guid.NewGuid(),
            TransferId = transfer.Id,
            PreviousState = previousState?.ToString(),
            NewState = (newState ?? transfer.State).ToString(),
            Reason = Bounded(reason, 128, nameof(reason)),
            Source = Bounded(source, 64, nameof(source)),
            ProviderReference = transfer.ProviderReference,
            OccurredAt = occurredAt,
            TransitionVersion = transitionVersion
        };

    private static string Bounded(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"Audit {name} must contain 1-{maximumLength} characters.", name);
        }

        return value;
    }
}
