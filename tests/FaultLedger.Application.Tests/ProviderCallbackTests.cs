using FaultLedger.Application.Transfers;
using FaultLedger.Domain;

namespace FaultLedger.Application.Tests;

public sealed class ProviderCallbackTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TransferId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Receive_ValidatesThenDurablyDelegatesWithoutProcessing()
    {
        var store = new RecordingInboxStore();
        var service = new ProviderCallbackService(store);

        InboxReceipt result = await service.ReceiveAsync(Callback(ProviderCallbackEventType.Completed), "{}",
            TestContext.Current.CancellationToken);

        Assert.Equal(InboxReceiptOutcome.Received, result.Outcome);
        Assert.Equal(1, store.Receipts);
        Assert.Equal(0, store.ProcessingAttempts);
    }

    [Fact]
    public async Task Receive_InvalidEnvelope_IsRejectedBeforeDurableReceipt()
    {
        var store = new RecordingInboxStore();
        var service = new ProviderCallbackService(store);
        ProviderCallbackEnvelope callback = Callback(ProviderCallbackEventType.Completed) with
        {
            ProviderEventId = string.Empty
        };

        await Assert.ThrowsAsync<ProviderCallbackValidationException>(() =>
            service.ReceiveAsync(callback, "{}", TestContext.Current.CancellationToken));

        Assert.Equal(0, store.Receipts);
    }

    [Fact]
    public void CallbackStateMachine_CompletedThenAccepted_RemainsCompleted()
    {
        Transfer transfer = UnknownTransfer();
        CallbackProcessingOutcome completed = ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Completed), Timestamp.AddMinutes(2));
        CallbackProcessingOutcome accepted = ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Accepted, "evt-2"), Timestamp.AddMinutes(3));

        Assert.Equal(CallbackProcessingOutcome.Applied, completed);
        Assert.Equal(CallbackProcessingOutcome.Stale, accepted);
        Assert.Equal(TransferState.Completed, transfer.State);
    }

    [Fact]
    public void CallbackStateMachine_AcceptedThenCompleted_AdvancesNormally()
    {
        Transfer transfer = UnknownTransfer();

        Assert.Equal(CallbackProcessingOutcome.Applied, ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Accepted), Timestamp.AddMinutes(2)));
        Assert.Equal(CallbackProcessingOutcome.Applied, ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Completed, "evt-2"), Timestamp.AddMinutes(3)));
        Assert.Equal(TransferState.Completed, transfer.State);
    }

    [Fact]
    public void CallbackStateMachine_SameBusinessStateDifferentEventIds_IsStateIdempotent()
    {
        Transfer transfer = UnknownTransfer();
        Assert.Equal(CallbackProcessingOutcome.Applied, ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Completed, "evt-1"), Timestamp.AddMinutes(2)));

        Assert.Equal(CallbackProcessingOutcome.AlreadyApplied, ProviderCallbackStateMachine.Apply(transfer,
            Callback(ProviderCallbackEventType.Completed, "evt-2"), Timestamp.AddMinutes(3)));
    }

    [Fact]
    public void CallbackStateMachine_ConflictingProviderReference_IsPoisonEvidence()
    {
        Transfer transfer = UnknownTransfer();
        ProviderCallbackStateMachine.Apply(transfer, Callback(ProviderCallbackEventType.Accepted),
            Timestamp.AddMinutes(2));

        ProviderCallbackEnvelope conflict = Callback(ProviderCallbackEventType.Completed, "evt-2") with
        {
            ProviderReference = "provider-conflict"
        };
        Assert.Throws<ProviderCallbackConflictException>(() => ProviderCallbackStateMachine.Apply(transfer, conflict,
            Timestamp.AddMinutes(3)));
        Assert.Equal(TransferState.Accepted, transfer.State);
    }

    private static ProviderCallbackEnvelope Callback(ProviderCallbackEventType type, string eventId = "evt-1") =>
        new(eventId, TransferId, type,
            type is ProviderCallbackEventType.Accepted or ProviderCallbackEventType.Completed ? "provider-1" : null,
            $"provider-{type.ToString().ToLowerInvariant()}", Timestamp);

    private static Transfer UnknownTransfer()
    {
        Transfer transfer = Transfer.Create(TransferId, "order-1", "key-1", new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        transfer.BeginSubmission(Timestamp);
        transfer.MarkUnknown("response-lost", Timestamp.AddMinutes(1));
        return transfer;
    }

    private sealed class RecordingInboxStore : IProviderInboxStore
    {
        public int Receipts { get; private set; }
        public int ProcessingAttempts { get; private set; }

        public Task<InboxReceipt> ReceiveAsync(ProviderCallbackEnvelope callback, string normalizedPayload,
            CancellationToken cancellationToken)
        {
            Receipts++;
            return Task.FromResult(new InboxReceipt(InboxReceiptOutcome.Received,
                Guid.Parse("22222222-2222-2222-2222-222222222222")));
        }

        public Task<CallbackProcessingDetails?> ProcessAsync(CancellationToken cancellationToken)
        {
            ProcessingAttempts++;
            return Task.FromResult<CallbackProcessingDetails?>(null);
        }

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Receipts);
    }
}
