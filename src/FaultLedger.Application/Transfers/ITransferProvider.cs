using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public interface ITransferProvider
{
    Task<ProviderSubmissionResult> SubmitAsync(ProviderTransferRequest request, CancellationToken cancellationToken);
}

public sealed record ProviderTransferRequest(Guid TransferId, string ClientReference, Money Money);

public enum ProviderAcceptanceEvidence
{
    ConfirmedAccepted,
    ConfirmedRejected,
    DefinitelyNotAccepted,
    AcceptanceAmbiguous
}

public enum ProviderFailureKind
{
    None,
    Rejected,
    Timeout,
    ConnectionFailure,
    ServerError,
    MalformedResponse
}

public sealed record ProviderSubmissionResult
{
    public ProviderSubmissionResult(ProviderAcceptanceEvidence acceptanceEvidence, string? providerReference,
        ProviderFailureKind failureKind, string safeMessage)
    {
        if (acceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedAccepted
            && string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("A confirmed acceptance requires a provider reference.", nameof(providerReference));
        }

        if (acceptanceEvidence != ProviderAcceptanceEvidence.ConfirmedAccepted && providerReference is not null)
        {
            throw new ArgumentException("Only a confirmed acceptance may carry a provider reference.", nameof(providerReference));
        }

        if (acceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedAccepted
            && failureKind != ProviderFailureKind.None)
        {
            throw new ArgumentException("Confirmed acceptance cannot carry a failure kind.", nameof(failureKind));
        }

        if (acceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedRejected
            && failureKind != ProviderFailureKind.Rejected)
        {
            throw new ArgumentException("Confirmed rejection requires the rejection failure kind.", nameof(failureKind));
        }

        if ((acceptanceEvidence is ProviderAcceptanceEvidence.DefinitelyNotAccepted
                or ProviderAcceptanceEvidence.AcceptanceAmbiguous)
            && failureKind == ProviderFailureKind.None)
        {
            throw new ArgumentException("A transport or response failure kind is required for non-success evidence.", nameof(failureKind));
        }

        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            throw new ArgumentException("A safe provider message is required.", nameof(safeMessage));
        }

        AcceptanceEvidence = acceptanceEvidence;
        ProviderReference = providerReference;
        FailureKind = failureKind;
        SafeMessage = safeMessage;
    }

    public ProviderSubmissionResult(string providerReference)
        : this(ProviderAcceptanceEvidence.ConfirmedAccepted, providerReference, ProviderFailureKind.None,
            "The synthetic provider accepted the operation.")
    {
    }

    public ProviderAcceptanceEvidence AcceptanceEvidence { get; }

    public string? ProviderReference { get; }

    public ProviderFailureKind FailureKind { get; }

    public string SafeMessage { get; }

    public bool IsConfirmedAccepted => AcceptanceEvidence == ProviderAcceptanceEvidence.ConfirmedAccepted;

    public static ProviderSubmissionResult ConfirmedAccepted(string providerReference) =>
        new(ProviderAcceptanceEvidence.ConfirmedAccepted, providerReference, ProviderFailureKind.None,
            "The synthetic provider accepted the operation.");

    public static ProviderSubmissionResult ConfirmedRejected(string message) =>
        new(ProviderAcceptanceEvidence.ConfirmedRejected, null, ProviderFailureKind.Rejected, message);

    public static ProviderSubmissionResult DefinitelyNotAccepted(ProviderFailureKind failureKind, string message) =>
        new(ProviderAcceptanceEvidence.DefinitelyNotAccepted, null, failureKind, message);

    public static ProviderSubmissionResult AcceptanceAmbiguous(ProviderFailureKind failureKind, string message) =>
        new(ProviderAcceptanceEvidence.AcceptanceAmbiguous, null, failureKind, message);
}

public sealed class ProviderSubmissionException(ProviderSubmissionResult result)
    : InvalidOperationException((result ?? throw new ArgumentNullException(nameof(result))).SafeMessage)
{
    public ProviderSubmissionResult Result { get; } = result ?? throw new ArgumentNullException(nameof(result));
}
