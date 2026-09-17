using FaultLedger.Application.IntegrationEvents;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure.Persistence;
using FaultLedger.SimulatedConsumer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FaultLedger.IntegrationTests;

[Trait("Category", "RequiresDocker")]
public sealed class OutboxPostgresTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedTransition_CreatesOneStableOutboxMessage()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid transferId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        await CreateCompletedTransferAsync(connectionString, transferId);

        OutboxRow row = await ReadOutboxAsync(connectionString);
        Assert.Equal(transferId, row.AggregateId);
        Assert.Equal(IntegrationEventTypes.TransferCompleted, row.EventType);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Null(row.PublishedAt);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(row.CreatedAt, row.NextAttemptAt);

        await using var verify = OpenContext(connectionString);
        Assert.Equal(1, await verify.OutboxCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompletionTransactionRollback_LeavesTransferAndOutboxUnchanged()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid transferId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        Transfer accepted = await CreateAcceptedTransferAsync(connectionString, transferId);
        accepted.MarkCompleted("synthetic-completed", Timestamp.AddMinutes(2));

        await using (var context = OpenContext(connectionString))
        {
            var store = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance,
                new ThrowBeforeCommitHook());
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(accepted, 3,
                TestContext.Current.CancellationToken));
        }

        await using var verify = OpenContext(connectionString);
        Transfer? current = await verify.FindTransferAsync(transferId, TestContext.Current.CancellationToken);
        Assert.Equal(TransferState.Accepted, current!.State);
        Assert.Equal(0, await verify.OutboxCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CrashAfterPublishBeforeAck_RedeliversStableEventAndConsumerEffectIsOne()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid transferId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        await CreateCompletedTransferAsync(connectionString, transferId);
        await using NpgsqlDataSource consumerDataSource = NpgsqlDataSource.Create(connectionString);
        var consumer = new SimulatedConsumerStore(consumerDataSource, new MutableTimeProvider(Timestamp));
        var time = new MutableTimeProvider(Timestamp);
        var publisher = new ConsumerPublisher(consumer);

        await using (var context = OpenContext(connectionString))
        {
            var dispatcher = new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
                new ThrowAfterPublishHook(), time);
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchNextAsync("worker-a",
                TestContext.Current.CancellationToken));
        }

        time.Advance(OutboxDispatcher.ClaimLease);
        await using (var context = OpenContext(connectionString))
        {
            var dispatcher = new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
                new NoOpOutboxDispatchHook(), time);
            Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync("worker-b",
                TestContext.Current.CancellationToken));
        }

        ConsumerEventState state = await consumer.FindAsync(publisher.EventId,
            TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        OutboxRow row = await ReadOutboxAsync(connectionString);
        Assert.Equal(2, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
        Assert.Equal(2, row.AttemptCount);
        Assert.NotNull(row.PublishedAt);
    }

    [Fact]
    public async Task CommittedOutbox_NewDispatcherAfterRestart_PublishesPendingMessage()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateCompletedTransferAsync(connectionString,
            Guid.Parse("30000000-0000-0000-0000-000000000008"));
        var publisher = new RecordingPublisher();

        await using (var restartedContext = OpenContext(connectionString))
        {
            var restartedDispatcher = new OutboxDispatcher(new PostgresOutboxStore(restartedContext), publisher,
                new NoOpOutboxDispatchHook(), new MutableTimeProvider(Timestamp));
            Assert.Equal(OutboxDispatchResult.Published,
                await restartedDispatcher.DispatchNextAsync("restarted-worker",
                    TestContext.Current.CancellationToken));
        }

        Assert.Single(publisher.EventIds);
        OutboxRow row = await ReadOutboxAsync(connectionString);
        Assert.Equal(publisher.EventIds[0], row.Id);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.PublishedAt);
    }

    [Fact]
    public async Task ClaimOwnerCrash_AfterLeaseExpiryAnotherDispatcherPublishes()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateCompletedTransferAsync(connectionString,
            Guid.Parse("30000000-0000-0000-0000-000000000009"));
        var time = new MutableTimeProvider(Timestamp);
        Guid eventId;

        await using (var abandonedContext = OpenContext(connectionString))
        {
            ClaimedOutboxMessage claim = Assert.IsType<ClaimedOutboxMessage>(
                await new PostgresOutboxStore(abandonedContext).ClaimNextAsync("crashed-worker",
                    time.GetUtcNow(), time.GetUtcNow().Add(OutboxDispatcher.ClaimLease),
                    TestContext.Current.CancellationToken));
            eventId = claim.Message.EventId;
        }

        time.Advance(OutboxDispatcher.ClaimLease);
        var publisher = new RecordingPublisher();
        await using (var recoveredContext = OpenContext(connectionString))
        {
            var recovered = new OutboxDispatcher(new PostgresOutboxStore(recoveredContext), publisher,
                new NoOpOutboxDispatchHook(), time);
            Assert.Equal(OutboxDispatchResult.Published,
                await recovered.DispatchNextAsync("recovery-worker", TestContext.Current.CancellationToken));
        }

        Assert.Equal([eventId], publisher.EventIds);
        OutboxRow row = await ReadOutboxAsync(connectionString);
        Assert.Equal(2, row.AttemptCount);
        Assert.NotNull(row.PublishedAt);
    }

    [Fact]
    public async Task ConsumerReceivesSameEventTenTimes_AppliesOneDurableLogicalEffect()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid transferId = Guid.Parse("30000000-0000-0000-0000-000000000010");
        await CreateCompletedTransferAsync(connectionString, transferId);
        OutboxRow row = await ReadOutboxAsync(connectionString);
        Transfer transfer;
        await using (var context = OpenContext(connectionString))
        {
            transfer = await context.FindTransferAsync(transferId, TestContext.Current.CancellationToken) ??
                throw new InvalidOperationException();
        }

        IntegrationEventMessage message = TransferCompletedIntegrationEvent.Create(transfer, row.Id);
        using var document = System.Text.Json.JsonDocument.Parse(message.Payload);
        var request = new IntegrationEventRequest(message.EventId, message.EventType, message.SchemaVersion,
            message.AggregateId, message.AggregateVersion, message.OccurredAt, document.RootElement.Clone());
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        var consumer = new SimulatedConsumerStore(dataSource, new MutableTimeProvider(Timestamp));
        for (int delivery = 0; delivery < 10; delivery++)
        {
            Assert.NotNull(await consumer.ReceiveAsync(request, TestContext.Current.CancellationToken));
        }

        ConsumerEventState state = await consumer.FindAsync(message.EventId,
            TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        Assert.Equal(10, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
    }

    [Fact]
    public async Task TwoDispatchers_OnePendingMessage_ProduceOneClaimAndDelivery()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateCompletedTransferAsync(connectionString,
            Guid.Parse("30000000-0000-0000-0000-000000000004"));
        var publisher = new RecordingPublisher();
        var time = new MutableTimeProvider(Timestamp);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OutboxDispatchResult> first = DispatchAsync(connectionString, "worker-a", publisher, time, start.Task);
        Task<OutboxDispatchResult> second = DispatchAsync(connectionString, "worker-b", publisher, time, start.Task);
        start.SetResult();
        OutboxDispatchResult[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result == OutboxDispatchResult.Published));
        Assert.Single(publisher.EventIds);
        OutboxRow row = await ReadOutboxAsync(connectionString);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.PublishedAt);
    }

    [Fact]
    public async Task PoisonMessage_IsDeferredAndDoesNotStarveLaterMessage()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid poisonId = Guid.Parse("30000000-0000-0000-0000-000000000005");
        Guid validId = Guid.Parse("30000000-0000-0000-0000-000000000006");
        await CreateCompletedTransferAsync(connectionString, poisonId);
        await CreateCompletedTransferAsync(connectionString, validId);
        var time = new MutableTimeProvider(Timestamp);
        var publisher = new SelectivePublisher(poisonId);

        await using var context = OpenContext(connectionString);
        var dispatcher = new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
            new NoOpOutboxDispatchHook(), time);
        Assert.Equal(OutboxDispatchResult.Failed, await dispatcher.DispatchNextAsync("worker",
            TestContext.Current.CancellationToken));
        Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync("worker",
            TestContext.Current.CancellationToken));
        Assert.Contains(validId, publisher.PublishedAggregates);
    }

    [Fact]
    public async Task ReconciliationCompletion_IsAtomicAndRepeatIsIdempotent()
    {
        string connectionString = await CreateDatabaseAsync();
        Guid transferId = Guid.Parse("30000000-0000-0000-0000-000000000007");
        await CreateUnknownTransferAsync(connectionString, transferId);

        await using (var context = OpenContext(connectionString))
        {
            var service = new TransferReconciliationService(new PostgresTransferStore(context,
                NullLogger<PostgresTransferStore>.Instance), new CompletedLookup(),
                new MutableTimeProvider(Timestamp));
            Assert.Equal(ReconciliationOutcome.ResolvedCompleted,
                (await service.ReconcileAsync(transferId, TestContext.Current.CancellationToken)).Outcome);
        }

        await using (var context = OpenContext(connectionString))
        {
            var service = new TransferReconciliationService(new PostgresTransferStore(context,
                NullLogger<PostgresTransferStore>.Instance), new CompletedLookup(),
                new MutableTimeProvider(Timestamp));
            Assert.Equal(ReconciliationOutcome.AlreadyResolved,
                (await service.ReconcileAsync(transferId, TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(1, await context.OutboxCountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task OneHundredMessages_TwoDispatchers_DeliverAllWithOneEffectEach()
    {
        string connectionString = await CreateDatabaseAsync();
        for (int index = 0; index < 100; index++)
        {
            await CreateCompletedTransferAsync(connectionString,
                Guid.Parse($"30000000-0000-0000-0001-{index + 1:000000000000}"));
        }

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        var consumer = new SimulatedConsumerStore(dataSource, new MutableTimeProvider(Timestamp));
        var publisher = new TwoWorkerCoordinatedConsumerPublisher(consumer);
        var time = new MutableTimeProvider(Timestamp);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = DrainAsync(connectionString, "batch-worker-a", publisher, time, start.Task);
        Task second = DrainAsync(connectionString, "batch-worker-b", publisher, time, start.Task);
        start.SetResult();

        await Task.WhenAll(first, second);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT count(*) FROM outbox_messages WHERE published_at IS NOT NULL),
                (SELECT count(*) FROM simulated_consumer_events),
                (SELECT sum(receipt_count) FROM simulated_consumer_events),
                (SELECT sum(logical_effect_count) FROM simulated_consumer_events)
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(100L, reader.GetInt64(0));
        Assert.Equal(100L, reader.GetInt64(1));
        Assert.Equal(100, reader.GetInt64(2));
        Assert.Equal(100, reader.GetInt64(3));
        Assert.True(publisher.FirstTwoPublicationsOverlapped);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        string name = $"stage6_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection))
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        string connectionString = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        {
            Database = name
        }.ConnectionString;
        await using var database = OpenContext(connectionString);
        await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static async Task<Transfer> CreateAcceptedTransferAsync(string connectionString, Guid id)
    {
        await using var context = OpenContext(connectionString);
        var store = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance);
        Transfer transfer = Transfer.Create(id, $"order-{id:N}", $"key-{id:N}", new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        await store.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute(transfer.ClientReference, 10,
            "USD"), TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
        transfer.BeginSubmission(Timestamp.AddMinutes(1));
        await store.TryClaimSubmissionAsync(transfer, 1, TestContext.Current.CancellationToken);
        transfer.MarkAccepted($"provider-{id:N}", Timestamp.AddMinutes(2));
        await store.UpdateAsync(transfer, 2, TestContext.Current.CancellationToken);
        return transfer;
    }

    private static async Task CreateCompletedTransferAsync(string connectionString, Guid id)
    {
        Transfer transfer = await CreateAcceptedTransferAsync(connectionString, id);
        transfer.MarkCompleted("provider-completed", Timestamp.AddMinutes(3));
        await using var context = OpenContext(connectionString);
        await new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance).UpdateAsync(transfer, 3,
            TestContext.Current.CancellationToken);
    }

    private static async Task CreateUnknownTransferAsync(string connectionString, Guid id)
    {
        await using var context = OpenContext(connectionString);
        var store = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance);
        Transfer transfer = Transfer.Create(id, $"order-{id:N}", $"key-{id:N}", new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        await store.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute(transfer.ClientReference, 10,
            "USD"), TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
        transfer.BeginSubmission(Timestamp.AddMinutes(1));
        await store.TryClaimSubmissionAsync(transfer, 1, TestContext.Current.CancellationToken);
        transfer.MarkUnknown("response-lost", Timestamp.AddMinutes(2));
        await store.UpdateAsync(transfer, 2, TestContext.Current.CancellationToken);
    }

    private static async Task<OutboxDispatchResult> DispatchAsync(string connectionString, string workerId,
        IIntegrationEventPublisher publisher, TimeProvider timeProvider, Task gate)
    {
        await gate;
        await using var context = OpenContext(connectionString);
        return await new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
            new NoOpOutboxDispatchHook(), timeProvider).DispatchNextAsync(workerId,
                TestContext.Current.CancellationToken);
    }

    private static async Task DrainAsync(string connectionString, string workerId,
        IIntegrationEventPublisher publisher, TimeProvider timeProvider, Task gate)
    {
        await gate;
        await using var context = OpenContext(connectionString);
        var dispatcher = new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
            new NoOpOutboxDispatchHook(), timeProvider);
        while (await dispatcher.DispatchNextAsync(workerId, TestContext.Current.CancellationToken) !=
               OutboxDispatchResult.NoMessage)
        {
        }
    }

    private static FaultLedgerDbContext OpenContext(string connectionString) => new(
        new DbContextOptionsBuilder<FaultLedgerDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<OutboxRow> ReadOutboxAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, aggregate_id, event_type, schema_version, created_at, published_at, attempt_count, next_attempt_at FROM outbox_messages ORDER BY created_at, id LIMIT 1", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return new OutboxRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7));
    }

    private sealed record OutboxRow(Guid Id, Guid AggregateId, string EventType, int SchemaVersion,
        DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, int AttemptCount, DateTimeOffset NextAttemptAt);

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan elapsed) => current = current.Add(elapsed);
    }

    private sealed class ThrowBeforeCommitHook : IOutboxPersistenceHook
    {
        public Task BeforeCommitAsync(Guid aggregateId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic pre-commit failure"));
    }

    private sealed class ThrowAfterPublishHook : IOutboxDispatchHook
    {
        public Task AfterRemotePublishBeforeLocalAckAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic crash after publish"));
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<Guid> EventIds { get; } = [];
        public Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            EventIds.Add(message.EventId);
            return Task.CompletedTask;
        }
    }

    private sealed class ConsumerPublisher(SimulatedConsumerStore consumer) : IIntegrationEventPublisher
    {
        public Guid EventId { get; private set; }
        public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            EventId = message.EventId;
            var payload = System.Text.Json.JsonDocument.Parse(message.Payload).RootElement;
            IntegrationEventRequest request = new(message.EventId, message.EventType, message.SchemaVersion,
                message.AggregateId, message.AggregateVersion, message.OccurredAt, payload);
            Assert.NotNull(await consumer.ReceiveAsync(request, cancellationToken));
        }
    }

    private sealed class TwoWorkerCoordinatedConsumerPublisher(SimulatedConsumerStore consumer) :
        IIntegrationEventPublisher
    {
        private readonly TaskCompletionSource firstTwoArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivalCount;

        public bool FirstTwoPublicationsOverlapped => Volatile.Read(ref arrivalCount) >= 2;

        public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            int arrival = Interlocked.Increment(ref arrivalCount);
            if (arrival == 2)
            {
                firstTwoArrived.TrySetResult();
            }

            if (arrival <= 2)
            {
                await firstTwoArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            using var document = System.Text.Json.JsonDocument.Parse(message.Payload);
            var request = new IntegrationEventRequest(message.EventId, message.EventType, message.SchemaVersion,
                message.AggregateId, message.AggregateVersion, message.OccurredAt, document.RootElement.Clone());
            Assert.NotNull(await consumer.ReceiveAsync(request, cancellationToken));
        }
    }

    private sealed class SelectivePublisher(Guid poisonAggregateId) : IIntegrationEventPublisher
    {
        public List<Guid> PublishedAggregates { get; } = [];
        public Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            if (message.AggregateId == poisonAggregateId)
            {
                throw new IntegrationEventDeliveryException("synthetic poison event");
            }

            PublishedAggregates.Add(message.AggregateId);
            return Task.CompletedTask;
        }
    }

    private sealed class CompletedLookup : ITransferLookup
    {
        public Task<ProviderLookupResult> LookupAsync(ProviderLookupRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                ProviderLookupResult.ConfirmedCompleted("provider-completed"));
    }
}

internal static class OutboxTestDbExtensions
{
}
