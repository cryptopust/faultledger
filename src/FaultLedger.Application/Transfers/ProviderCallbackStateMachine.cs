using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public static class ProviderCallbackStateMachine
{
    public static CallbackProcessingOutcome Apply(Transfer transfer, ProviderCallbackEnvelope callback,
        DateTimeOffset processingTime)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(callback);
        callback.Validate();

        CallbackTransition transition;
        switch (callback.EventType)
        {
            case ProviderCallbackEventType.Accepted:
                EnsureReferenceCompatible(transfer, callback.ProviderReference!);
                transition = transfer.ApplyAcceptedCallback(callback.ProviderReference!, processingTime);
                break;
            case ProviderCallbackEventType.Completed:
                EnsureReferenceCompatible(transfer, callback.ProviderReference!);
                transition = transfer.ApplyCompletedCallback(callback.ProviderReference!, callback.Evidence,
                    processingTime);
                break;
            case ProviderCallbackEventType.Rejected:
            case ProviderCallbackEventType.Failed:
                transition = transfer.ApplyRejectedCallback(callback.Evidence, processingTime);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(callback), "Unsupported callback event type.");
        }

        return transition switch
        {
            CallbackTransition.Applied => CallbackProcessingOutcome.Applied,
            CallbackTransition.AlreadyApplied => CallbackProcessingOutcome.AlreadyApplied,
            CallbackTransition.Stale => CallbackProcessingOutcome.Stale,
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };
    }

    private static void EnsureReferenceCompatible(Transfer transfer, string providerReference)
    {
        if (transfer.ProviderReference is not null &&
            !string.Equals(transfer.ProviderReference, providerReference, StringComparison.Ordinal))
        {
            throw new ProviderCallbackConflictException();
        }
    }
}

public interface IProviderInboxProcessingHook
{
    Task BeforeCommitAsync(Guid inboxId, CancellationToken cancellationToken);
}

public sealed class NoOpProviderInboxProcessingHook : IProviderInboxProcessingHook
{
    public Task BeforeCommitAsync(Guid inboxId, CancellationToken cancellationToken) => Task.CompletedTask;
}
