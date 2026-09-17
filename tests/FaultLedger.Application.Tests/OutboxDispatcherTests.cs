using FaultLedger.Application.IntegrationEvents;

namespace FaultLedger.Application.Tests;

public sealed class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly IntegrationEventMessage Message = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        IntegrationEventTypes.TransferCompleted,
        1,
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        4,
        Timestamp,
        "{\"transferId\":\"20000000-0000-0000-0000-000000000001\"}");

    [Fact]
    public async Task SuccessfulPublish_MarksTheClaimedMessagePublished()
    {
        var time = new MutableTimeProvider(Timestamp);
        var store = new InMemoryOutboxStore(Message);
        var publisher = new RecordingPublisher();
        var dispatcher = new OutboxDispatcher(store, publisher, new NoOpOutboxDispatchHook(), time);

        OutboxDispatchResult result = await dispatcher.DispatchNextAsync("worker-a",
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboxDispatchResult.Published, result);
        Assert.True(store.Published);
        Assert.Equal(1, store.AttemptCount);
        Assert.Equal([Message.EventId], publisher.EventIds);
    }

    [Fact]
    public async Task FailedPublish_IsDeferredAndLaterSuccessUsesTheSameEventId()
    {
        var time = new MutableTimeProvider(Timestamp);
        var store = new InMemoryOutboxStore(Message);
        var publisher = new RecordingPublisher(failuresBeforeSuccess: 1);
        var dispatcher = new OutboxDispatcher(store, publisher, new NoOpOutboxDispatchHook(), time);

        Assert.Equal(OutboxDispatchResult.Failed,
            await dispatcher.DispatchNextAsync("worker-a", TestContext.Current.CancellationToken));
        Assert.False(store.Published);
        Assert.Equal(Timestamp.Add(OutboxDispatcher.RetryDelay), store.NextAttemptAt);
        Assert.Equal(OutboxDispatchResult.NoMessage,
            await dispatcher.DispatchNextAsync("worker-a", TestContext.Current.CancellationToken));

        time.Advance(OutboxDispatcher.RetryDelay);
        Assert.Equal(OutboxDispatchResult.Published,
            await dispatcher.DispatchNextAsync("worker-b", TestContext.Current.CancellationToken));

        Assert.Equal(2, store.AttemptCount);
        Assert.Equal([Message.EventId, Message.EventId], publisher.EventIds);
    }

    [Fact]
    public async Task CrashAfterRemotePublish_LeavesClaimRecoverableAndRedeliversTheSameEvent()
    {
        var time = new MutableTimeProvider(Timestamp);
        var store = new InMemoryOutboxStore(Message);
        var publisher = new DeduplicatingRecordingPublisher();
        var dispatcher = new OutboxDispatcher(store, publisher, new ThrowAfterPublishHook(), time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchNextAsync("worker-a",
            TestContext.Current.CancellationToken));
        Assert.False(store.Published);
        Assert.Equal(1, publisher.ReceiptCount);
        Assert.Equal(1, publisher.LogicalEffectCount);
        Assert.Equal(OutboxDispatchResult.NoMessage,
            await dispatcher.DispatchNextAsync("worker-b", TestContext.Current.CancellationToken));

        time.Advance(OutboxDispatcher.ClaimLease);
        var restarted = new OutboxDispatcher(store, publisher, new NoOpOutboxDispatchHook(), time);
        Assert.Equal(OutboxDispatchResult.Published,
            await restarted.DispatchNextAsync("worker-b", TestContext.Current.CancellationToken));

        Assert.True(store.Published);
        Assert.Equal(2, store.AttemptCount);
        Assert.Equal(2, publisher.ReceiptCount);
        Assert.Equal(1, publisher.LogicalEffectCount);
        Assert.All(publisher.EventIds, id => Assert.Equal(Message.EventId, id));
    }

    [Fact]
    public async Task StaleClaimCannotAcknowledgeANewerClaim()
    {
        var time = new MutableTimeProvider(Timestamp);
        var store = new InMemoryOutboxStore(Message);
        ClaimedOutboxMessage first = Assert.IsType<ClaimedOutboxMessage>(await store.ClaimNextAsync("worker-a",
            Timestamp, Timestamp.Add(OutboxDispatcher.ClaimLease), TestContext.Current.CancellationToken));
        time.Advance(OutboxDispatcher.ClaimLease);
        ClaimedOutboxMessage second = Assert.IsType<ClaimedOutboxMessage>(await store.ClaimNextAsync("worker-b",
            time.GetUtcNow(), time.GetUtcNow().Add(OutboxDispatcher.ClaimLease),
            TestContext.Current.CancellationToken));

        Assert.False(await store.MarkPublishedAsync(Message.EventId, first.ClaimId, time.GetUtcNow(),
            TestContext.Current.CancellationToken));
        Assert.True(await store.MarkPublishedAsync(Message.EventId, second.ClaimId, time.GetUtcNow(),
            TestContext.Current.CancellationToken));
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan elapsed) => current = current.Add(elapsed);
    }

    private sealed class RecordingPublisher(int failuresBeforeSuccess = 0) : IIntegrationEventPublisher
    {
        private int failuresRemaining = failuresBeforeSuccess;
        public List<Guid> EventIds { get; } = [];

        public Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventIds.Add(message.EventId);
            if (failuresRemaining-- > 0)
            {
                throw new IntegrationEventDeliveryException("synthetic delivery failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DeduplicatingRecordingPublisher : IIntegrationEventPublisher
    {
        private readonly HashSet<Guid> effects = [];
        public List<Guid> EventIds { get; } = [];
        public int ReceiptCount => EventIds.Count;
        public int LogicalEffectCount => effects.Count;

        public Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventIds.Add(message.EventId);
            effects.Add(message.EventId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowAfterPublishHook : IOutboxDispatchHook
    {
        public Task AfterRemotePublishBeforeLocalAckAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic crash after remote publish"));
    }

    private sealed class InMemoryOutboxStore(IntegrationEventMessage message) : IOutboxStore
    {
        private Guid? claimId;
        private DateTimeOffset? claimedUntil;
        public bool Published { get; private set; }
        public int AttemptCount { get; private set; }
        public DateTimeOffset NextAttemptAt { get; private set; } = message.OccurredAt;

        public Task<ClaimedOutboxMessage?> ClaimNextAsync(string workerId, DateTimeOffset claimedAt,
            DateTimeOffset leaseUntil, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Published || NextAttemptAt > claimedAt || claimedUntil > claimedAt)
            {
                return Task.FromResult<ClaimedOutboxMessage?>(null);
            }

            claimId = Guid.NewGuid();
            claimedUntil = leaseUntil;
            AttemptCount++;
            return Task.FromResult<ClaimedOutboxMessage?>(new ClaimedOutboxMessage(message, AttemptCount,
                workerId, claimId.Value, leaseUntil));
        }

        public Task<bool> MarkPublishedAsync(Guid eventId, Guid suppliedClaimId, DateTimeOffset publishedAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsClaim = !Published && eventId == message.EventId && claimId == suppliedClaimId;
            if (ownsClaim)
            {
                Published = true;
                claimId = null;
                claimedUntil = null;
            }
            return Task.FromResult(ownsClaim);
        }

        public Task<bool> RecordFailureAsync(Guid eventId, Guid suppliedClaimId, DateTimeOffset nextAttemptAt,
            string error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsClaim = !Published && eventId == message.EventId && claimId == suppliedClaimId;
            if (ownsClaim)
            {
                NextAttemptAt = nextAttemptAt;
                claimId = null;
                claimedUntil = null;
            }
            return Task.FromResult(ownsClaim);
        }
    }
}
