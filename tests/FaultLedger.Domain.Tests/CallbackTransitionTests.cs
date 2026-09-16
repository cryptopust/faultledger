namespace FaultLedger.Domain.Tests;

public sealed class CallbackTransitionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unknown_CompletedCallback_ResolvesDirectlyWithoutSubmissionState()
    {
        Transfer transfer = CreateSubmitting();
        transfer.MarkUnknown("response-lost", Timestamp.AddMinutes(1));

        CallbackTransition result = transfer.ApplyCompletedCallback("provider-1", "provider-completed",
            Timestamp.AddMinutes(2));

        Assert.Equal(CallbackTransition.Applied, result);
        Assert.Equal(TransferState.Completed, transfer.State);
        Assert.Equal("provider-1", transfer.ProviderReference);
    }

    [Fact]
    public void Submitting_AcceptedCallback_UsesCallbackEvidence()
    {
        Transfer transfer = CreateSubmitting();

        CallbackTransition result = transfer.ApplyAcceptedCallback("provider-1", Timestamp.AddMinutes(1));

        Assert.Equal(CallbackTransition.Applied, result);
        Assert.Equal(TransferState.Accepted, transfer.State);
    }

    [Fact]
    public void Completed_LateAcceptedCallback_IsStaleAndNonRegressive()
    {
        Transfer transfer = CreateSubmitting();
        transfer.MarkAccepted("provider-1", Timestamp.AddMinutes(1));
        transfer.MarkCompleted("provider-completed", Timestamp.AddMinutes(2));
        long version = transfer.Version;

        CallbackTransition result = transfer.ApplyAcceptedCallback("provider-1", Timestamp.AddMinutes(3));

        Assert.Equal(CallbackTransition.Stale, result);
        Assert.Equal(TransferState.Completed, transfer.State);
        Assert.Equal(version, transfer.Version);
    }

    [Fact]
    public void Completed_RepeatedCompletedCallback_IsIdempotent()
    {
        Transfer transfer = CreateSubmitting();
        transfer.MarkAccepted("provider-1", Timestamp.AddMinutes(1));
        transfer.MarkCompleted("provider-completed", Timestamp.AddMinutes(2));

        CallbackTransition result = transfer.ApplyCompletedCallback("provider-1", "provider-completed",
            Timestamp.AddMinutes(3));

        Assert.Equal(CallbackTransition.AlreadyApplied, result);
        Assert.Equal(TransferState.Completed, transfer.State);
    }

    [Fact]
    public void Completed_LateRejectedCallback_IsStaleAndNonRegressive()
    {
        Transfer transfer = CreateSubmitting();
        transfer.MarkAccepted("provider-1", Timestamp.AddMinutes(1));
        transfer.MarkCompleted("provider-completed", Timestamp.AddMinutes(2));

        CallbackTransition result = transfer.ApplyRejectedCallback("late-rejection", Timestamp.AddMinutes(3));

        Assert.Equal(CallbackTransition.Stale, result);
        Assert.Equal(TransferState.Completed, transfer.State);
    }

    private static Transfer CreateSubmitting()
    {
        Transfer transfer = Transfer.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), "order-1",
            "key-1", new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        transfer.BeginSubmission(Timestamp);
        return transfer;
    }
}
