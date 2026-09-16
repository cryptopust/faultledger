using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public enum ProviderCallbackEventType
{
    Accepted,
    Completed,
    Rejected,
    Failed
}

public sealed record ProviderCallbackEnvelope(
    string ProviderEventId,
    Guid ProviderOperationCorrelation,
    ProviderCallbackEventType EventType,
    string? ProviderReference,
    string Evidence,
    DateTimeOffset OccurredAt,
    int PayloadVersion = 1)
{
    public void Validate()
    {
        ValidateReference(ProviderEventId, 128, nameof(ProviderEventId));
        if (ProviderOperationCorrelation == Guid.Empty)
        {
            throw new ArgumentException("Provider operation correlation is required.", nameof(ProviderOperationCorrelation));
        }

        if (EventType is ProviderCallbackEventType.Accepted or ProviderCallbackEventType.Completed)
        {
            if (ProviderReference is null)
            {
                throw new ArgumentException("Accepted or completed callbacks require a provider reference.", nameof(ProviderReference));
            }
            ValidateReference(ProviderReference, Transfer.ReferenceMaximumLength, nameof(ProviderReference));
        }
        else if (ProviderReference is not null)
        {
            ValidateReference(ProviderReference, Transfer.ReferenceMaximumLength, nameof(ProviderReference));
        }
        if (string.IsNullOrWhiteSpace(Evidence) || Evidence.Length > 200)
        {
            throw new ArgumentException("Callback evidence is required and must be bounded.", nameof(Evidence));
        }

        if (PayloadVersion != 1)
        {
            throw new ArgumentException("Unsupported callback payload version.", nameof(PayloadVersion));
        }

        if (!Enum.IsDefined(EventType))
        {
            throw new ArgumentException("Unsupported callback event type.", nameof(EventType));
        }
    }

    private static void ValidateReference(string value, int maximumLength, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
        {
            throw new ArgumentException($"Callback reference must contain 1-{maximumLength} safe ASCII characters.", name);
        }
    }
}

public enum InboxReceiptOutcome
{
    Received,
    Duplicate
}

public enum InboxProcessingStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}

public enum CallbackProcessingOutcome
{
    Applied,
    AlreadyApplied,
    Stale,
    UnknownCorrelation,
    InvalidTransition,
    Poison
}

public sealed record InboxReceipt(InboxReceiptOutcome Outcome, Guid InboxId);

public sealed record CallbackProcessingDetails(
    Guid InboxId,
    CallbackProcessingOutcome Outcome,
    TransferDetails? Transfer,
    string Message);

public interface IProviderInboxStore
{
    Task<InboxReceipt> ReceiveAsync(ProviderCallbackEnvelope callback, string normalizedPayload,
        CancellationToken cancellationToken);

    Task<CallbackProcessingDetails?> ProcessAsync(CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed class ProviderCallbackValidationException(string message) : ArgumentException(message);

public sealed class CallbackAuthenticationException() : UnauthorizedAccessException("Callback authentication failed.");

public sealed class ProviderEventConflictException()
    : InvalidOperationException("The provider event identifier was previously received with a different envelope.");

public sealed class ProviderCallbackConflictException()
    : InvalidOperationException("Callback evidence conflicts with durable provider evidence.");
