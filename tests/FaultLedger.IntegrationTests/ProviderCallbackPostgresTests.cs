using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FaultLedger.IntegrationTests;

[Trait("Category", "RequiresDocker")]
public sealed class ProviderCallbackPostgresTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TransferId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ConcurrentDuplicateReceipt_IsArbitratedByPostgres()
    {
        string connectionString = await CreateDatabaseAsync();
        ProviderCallbackEnvelope callback = Callback("evt-concurrent");
        Task<InboxReceipt>[] receipts = Enumerable.Range(0, 50).Select(_ => ReceiveInIndependentContextAsync(
            connectionString, callback)).ToArray();

        InboxReceipt[] results = await Task.WhenAll(receipts);

        Assert.Single(results.Select(result => result.InboxId).Distinct());
        Assert.Contains(results, result => result.Outcome == InboxReceiptOutcome.Received);
        Assert.All(results, result => Assert.Contains(result.Outcome,
            new[] { InboxReceiptOutcome.Received, InboxReceiptOutcome.Duplicate }));
        await using var context = OpenContext(connectionString);
        Assert.Equal(1, await context.ProviderInboxCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownCompletedCallback_UpdatesTransferAndInboxAtomically()
    {
        string connectionString = await CreateDatabaseAsync();
        await using (var context = OpenContext(connectionString))
        {
            var transferStore = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance);
            Transfer transfer = Transfer.Create(TransferId, "order-callback", "key-callback",
                new Money(10, "USD"), Timestamp);
            transfer.MarkReadyToSubmit(Timestamp);
            await transferStore.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute("order-callback", 10,
                "USD"), TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
            transfer.BeginSubmission(Timestamp.AddMinutes(1));
            await transferStore.TryClaimSubmissionAsync(transfer, 1, TestContext.Current.CancellationToken);
            transfer.MarkUnknown("response-lost", Timestamp.AddMinutes(2));
            await transferStore.UpdateAsync(transfer, 2, TestContext.Current.CancellationToken);
        }

        await using (var context = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            await inbox.ReceiveAsync(Callback("evt-completed"), "{}", TestContext.Current.CancellationToken);
            CallbackProcessingDetails? details = await inbox.ProcessAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(details);
            Assert.Equal(CallbackProcessingOutcome.Applied, details.Outcome);
            Assert.Equal(nameof(TransferState.Completed), details.Transfer!.State);
        }

        await using var verify = OpenContext(connectionString);
        Transfer? current = await verify.FindTransferAsync(TransferId, TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        Assert.Equal(TransferState.Completed, current.State);
        Assert.Equal(1, await verify.ProviderInboxCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessingFailureBeforeCommit_LeavesInboxRecoverableAndEffectIsAppliedOnce()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateUnknownTransferAsync(connectionString);
        await using (var context = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new ThrowOnceProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            await inbox.ReceiveAsync(Callback("evt-crash"), "{}", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                inbox.ProcessAsync(TestContext.Current.CancellationToken));
        }

        await using (var context = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            CallbackProcessingDetails? details = await inbox.ProcessAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(details);
            Assert.Equal(CallbackProcessingOutcome.Applied, details.Outcome);
        }

        await using var verify = OpenContext(connectionString);
        Transfer? current = await verify.FindTransferAsync(TransferId, TestContext.Current.CancellationToken);
        Assert.Equal(TransferState.Completed, current!.State);
        Assert.Equal(1, await verify.ProviderInboxCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentAcceptedAndCompletedCallbacks_ConvergeToCompleted()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateUnknownTransferAsync(connectionString);
        await using (var context = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            await inbox.ReceiveAsync(Callback("evt-accepted") with
            {
                EventType = ProviderCallbackEventType.Accepted,
                Evidence = "provider-accepted"
            }, "accepted", TestContext.Current.CancellationToken);
            await inbox.ReceiveAsync(Callback("evt-completed"), "completed", TestContext.Current.CancellationToken);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CallbackProcessingDetails?>[] workers = Enumerable.Range(0, 2).Select(async _ =>
        {
            await start.Task;
            await using var context = OpenContext(connectionString);
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            return await inbox.ProcessAsync(TestContext.Current.CancellationToken);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(workers);

        await using var verify = OpenContext(connectionString);
        Transfer? current = await verify.FindTransferAsync(TransferId, TestContext.Current.CancellationToken);
        Assert.Equal(TransferState.Completed, current!.State);
    }

    [Fact]
    public async Task ProcessedInboxEvent_IsNotReappliedAfterWorkerRestart()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateUnknownTransferAsync(connectionString);
        await using (var firstContext = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(firstContext, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            await inbox.ReceiveAsync(Callback("evt-restart"), "completed", TestContext.Current.CancellationToken);
            Assert.NotNull(await inbox.ProcessAsync(TestContext.Current.CancellationToken));
        }

        await using var restartedContext = OpenContext(connectionString);
        var restarted = new PostgresProviderInboxStore(restartedContext, TimeProvider.System,
            new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
        Assert.Null(await restarted.ProcessAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownCorrelation_IsRetainedAsPoisonEvidence()
    {
        string connectionString = await CreateDatabaseAsync();
        await using var context = OpenContext(connectionString);
        var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
            new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
        ProviderCallbackEnvelope callback = Callback("evt-unknown-correlation") with
        {
            ProviderOperationCorrelation = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };
        await inbox.ReceiveAsync(callback, "unknown", TestContext.Current.CancellationToken);

        CallbackProcessingDetails? details = await inbox.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallbackProcessingOutcome.UnknownCorrelation, details!.Outcome);
        Assert.Null(details.Transfer);
    }

    [Fact]
    public async Task CallbackAndReconciliationRace_ConvergesToCompletedWithoutSubmission()
    {
        string connectionString = await CreateDatabaseAsync();
        await CreateUnknownTransferAsync(connectionString);
        await using (var context = OpenContext(connectionString))
        {
            var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
                new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
            await inbox.ReceiveAsync(Callback("evt-race"), "completed", TestContext.Current.CancellationToken);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CallbackProcessingDetails?> process = ProcessAfterGateAsync(connectionString, start.Task);
        Task<ReconciliationDetails> reconcile = ReconcileAfterGateAsync(connectionString, start.Task);
        start.SetResult();
        await Task.WhenAll(process, reconcile);

        await using var verify = OpenContext(connectionString);
        Transfer? current = await verify.FindTransferAsync(TransferId, TestContext.Current.CancellationToken);
        Assert.Equal(TransferState.Completed, current!.State);
    }

    private async Task<InboxReceipt> ReceiveInIndependentContextAsync(string connectionString,
        ProviderCallbackEnvelope callback)
    {
        await using var context = OpenContext(connectionString);
        var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
            new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
        return await inbox.ReceiveAsync(callback, "{}", TestContext.Current.CancellationToken);
    }

    private static ProviderCallbackEnvelope Callback(string eventId) => new(eventId, TransferId,
        ProviderCallbackEventType.Completed, "provider-callback", "provider-completed", Timestamp);

    private static FaultLedgerDbContext OpenContext(string connectionString) => new(
        new DbContextOptionsBuilder<FaultLedgerDbContext>().UseNpgsql(connectionString).Options);

    private async Task<string> CreateDatabaseAsync()
    {
        string name = $"stage5_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        string connectionString = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        {
            Database = name
        }.ConnectionString;
        await using var database = OpenContext(connectionString);
        await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static async Task CreateUnknownTransferAsync(string connectionString)
    {
        await using var context = OpenContext(connectionString);
        var transferStore = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance);
        Transfer transfer = Transfer.Create(TransferId, "order-callback", "key-callback",
            new Money(10, "USD"), Timestamp);
        transfer.MarkReadyToSubmit(Timestamp);
        await transferStore.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute("order-callback", 10,
            "USD"), TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
        transfer.BeginSubmission(Timestamp.AddMinutes(1));
        await transferStore.TryClaimSubmissionAsync(transfer, 1, TestContext.Current.CancellationToken);
        transfer.MarkUnknown("response-lost", Timestamp.AddMinutes(2));
        await transferStore.UpdateAsync(transfer, 2, TestContext.Current.CancellationToken);
    }

    private async Task<CallbackProcessingDetails?> ProcessAfterGateAsync(string connectionString,
        Task gate)
    {
        await gate;
        await using var context = OpenContext(connectionString);
        var inbox = new PostgresProviderInboxStore(context, TimeProvider.System,
            new NoOpProviderInboxProcessingHook(), NullLogger<PostgresProviderInboxStore>.Instance);
        return await inbox.ProcessAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ReconciliationDetails> ReconcileAfterGateAsync(string connectionString, Task gate)
    {
        await gate;
        await using var context = OpenContext(connectionString);
        var service = new TransferReconciliationService(new PostgresTransferStore(context,
            NullLogger<PostgresTransferStore>.Instance), new ConfirmedAcceptedLookup(), TimeProvider.System);
        return await service.ReconcileAsync(TransferId, TestContext.Current.CancellationToken);
    }

    private sealed class ThrowOnceProcessingHook : IProviderInboxProcessingHook
    {
        public Task BeforeCommitAsync(Guid inboxId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic crash before processing commit"));
    }

    private sealed class ConfirmedAcceptedLookup : ITransferLookup
    {
        public Task<ProviderLookupResult> LookupAsync(ProviderLookupRequest request,
            CancellationToken cancellationToken) => Task.FromResult(ProviderLookupResult.ConfirmedAccepted("provider-callback"));
    }
}

internal static class ProviderCallbackTestDbExtensions
{
    public static Task<int> ProviderInboxCountAsync(this FaultLedgerDbContext context,
        CancellationToken cancellationToken) => context.Database.SqlQuery<int>($"SELECT count(*) FROM provider_inbox")
        .SingleAsync(cancellationToken);

    public static async Task<Transfer?> FindTransferAsync(this FaultLedgerDbContext context, Guid transferId,
        CancellationToken cancellationToken)
    {
        PostgresTransferStore store = new(context, NullLogger<PostgresTransferStore>.Instance);
        return await store.FindAsync(transferId, cancellationToken);
    }
}
