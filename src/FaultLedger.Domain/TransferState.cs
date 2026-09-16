namespace FaultLedger.Domain;

public enum TransferState
{
    Created,
    ReadyToSubmit,
    Submitting,
    Accepted,
    Completed,
    Failed,
    Unknown,
    ManualReview
}
