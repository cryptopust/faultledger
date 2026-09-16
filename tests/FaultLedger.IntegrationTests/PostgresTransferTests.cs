using System.Net;
using System.Net.Http.Json;
using FaultLedger.Api.Transfers;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FaultLedger.IntegrationTests;

[Trait("Category", "RequiresDocker")]
public sealed class PostgresTransferTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Identity = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task SameIdempotencyKey_OneHundredConcurrentRequests_SubmitsProviderExactlyOnce()
    {
        string connectionString = await CreateDatabaseAsync();
        var ledger = new MockProviderLedger();
        await using var factory = CreateFactory(connectionString, ledger, MockProviderScenario.Success);
        using HttpClient client = factory.CreateClient();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(HttpStatusCode Status, TransferResponse? Body)>[] requests = Enumerable.Range(0, 100)
            .Select(async _ =>
            {
                await start.Task;
                using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", Request(),
                    TestContext.Current.CancellationToken);
                TransferResponse? body = await response.Content.ReadFromJsonAsync<TransferResponse>(
                    TestContext.Current.CancellationToken);
                return (response.StatusCode, body);
            }).ToArray();

        start.SetResult();
        (HttpStatusCode Status, TransferResponse? Body)[] results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.True(result.Status is HttpStatusCode.Created or HttpStatusCode.OK));
        Guid id = Assert.Single(results.Select(result => Assert.IsType<TransferResponse>(result.Body).Id).Distinct());
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(1L, await TransferCountAsync(connectionString));
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task SameKey_MixedCanonicalRequests_OneFingerprintWinsAndOnlyWinnerSubmits()
    {
        string connectionString = await CreateDatabaseAsync();
        var ledger = new MockProviderLedger();
        await using var factory = CreateFactory(connectionString, ledger, MockProviderScenario.Success);
        using HttpClient client = factory.CreateClient();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(decimal Amount, HttpStatusCode Status)>[] requests = Enumerable.Range(0, 100)
            .Select(async index =>
            {
                decimal amount = index < 50 ? 100m : 200m;
                await start.Task;
                using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", Request(amount),
                    TestContext.Current.CancellationToken);
                return (amount, response.StatusCode);
            }).ToArray();

        start.SetResult();
        (decimal Amount, HttpStatusCode Status)[] results = await Task.WhenAll(requests);
        Assert.Contains(results, result => result.Status == HttpStatusCode.Conflict);
        Assert.Contains(results, result => result.Status is HttpStatusCode.Created or HttpStatusCode.OK);
        decimal winner = await StoredAmountAsync(connectionString);
        Assert.All(results.Where(result => result.Amount == winner),
            result => Assert.True(result.Status is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.All(results.Where(result => result.Amount != winner),
            result => Assert.Equal(HttpStatusCode.Conflict, result.Status));
        Assert.Equal(1L, await TransferCountAsync(connectionString));
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task TwoIndependentHosts_SameKeyContention_HasOneTransferAndProviderAttempt()
    {
        string connectionString = await CreateDatabaseAsync();
        var ledger = new MockProviderLedger();
        await using var first = CreateFactory(connectionString, ledger, MockProviderScenario.Success);
        await using var second = CreateFactory(connectionString, ledger, MockProviderScenario.Success);
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpStatusCode>[] requests = Enumerable.Range(0, 100).Select(async index =>
        {
            await start.Task;
            HttpClient client = index % 2 == 0 ? firstClient : secondClient;
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", Request(),
                TestContext.Current.CancellationToken);
            return response.StatusCode;
        }).ToArray();

        start.SetResult();
        HttpStatusCode[] statuses = await Task.WhenAll(requests);
        Assert.All(statuses, status => Assert.True(status is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Equal(1L, await TransferCountAsync(connectionString));
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task TimeoutAfterAcceptance_ReplayDoesNotPostAgain()
    {
        string connectionString = await CreateDatabaseAsync();
        var ledger = new MockProviderLedger();
        await using var factory = CreateFactory(connectionString, ledger, MockProviderScenario.TimeoutAfterAccept);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await client.PostAsJsonAsync("/api/transfers", Request(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        using HttpResponseMessage replay = await client.PostAsJsonAsync("/api/transfers", Request(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        TransferResponse? body = await replay.Content.ReadFromJsonAsync<TransferResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Submitting", body.State);
        Assert.Equal(1L, await TransferCountAsync(connectionString));
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task RestartReplayAndConflict_AreDerivedFromPostgresWithoutResubmission()
    {
        string connectionString = await CreateDatabaseAsync();
        var ledger = new MockProviderLedger();
        TransferResponse created;
        await using (var first = CreateFactory(connectionString, ledger, MockProviderScenario.Success))
        {
            using HttpClient client = first.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", Request(),
                TestContext.Current.CancellationToken);
            created = Assert.IsType<TransferResponse>(await response.Content.ReadFromJsonAsync<TransferResponse>(
                TestContext.Current.CancellationToken));
        }

        await using (var restarted = CreateFactory(connectionString, ledger, MockProviderScenario.Success))
        {
            using HttpClient client = restarted.CreateClient();
            using HttpResponseMessage replay = await client.PostAsJsonAsync("/api/transfers", Request(),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            TransferResponse? replayed = await replay.Content.ReadFromJsonAsync<TransferResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal(created, replayed);
            using HttpResponseMessage conflict = await client.PostAsJsonAsync("/api/transfers", Request(200m),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }

        Assert.Equal(1L, await TransferCountAsync(connectionString));
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task IndependentStores_SameKeyRace_UsesNamedUniqueConstraintAndRecoversWinner()
    {
        string connectionString = await CreateDatabaseAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<TransferRegistration> RegisterAsync(Guid id)
        {
            await using FaultLedgerDbContext database = OpenContext(connectionString);
            PostgresTransferStore store = Store(database);
            Transfer transfer = Transfer.Create(id, "order-1", "key-1", new Money(10.999m, "USD"), Timestamp);
            transfer.MarkReadyToSubmit(Timestamp);
            await start.Task;
            return await store.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute(
                    transfer.ClientReference, transfer.Money.Amount, transfer.Money.Currency),
                TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
        }

        Task<TransferRegistration> first = RegisterAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Task<TransferRegistration> second = RegisterAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        start.SetResult();
        TransferRegistration[] results = await Task.WhenAll(first, second);
        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Single(results.Select(result => result.Transfer.Id).Distinct());
        Assert.Equal(1L, await TransferCountAsync(connectionString));
    }

    [Fact]
    public async Task EmptyPostgres_AppliesActualMigrationWithExpectedColumnsConstraintsAndIndexes()
    {
        string connectionString = await CreateDatabaseAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var columns = new NpgsqlCommand("SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'transfers' ORDER BY ordinal_position", connection);
        var actual = new Dictionary<string, (string Type, string Nullable)>();
        await using (var reader = await columns.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                actual.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2)));
            }
        }

        Assert.Equal(new[] { "id", "client_reference", "idempotency_key", "request_fingerprint", "fingerprint_version", "amount", "currency", "state", "provider_reference", "created_at", "updated_at", "version" }, actual.Keys);
        Assert.Equal(("numeric", "NO"), actual["amount"]);
        Assert.Equal(("uuid", "NO"), actual["id"]);
        Assert.Equal(("bigint", "NO"), actual["version"]);
        Assert.Equal(("timestamp with time zone", "NO"), actual["created_at"]);
        Assert.Equal("YES", actual["provider_reference"].Nullable);
        Assert.All(actual.Where(column => column.Key != "provider_reference"), column => Assert.Equal("NO", column.Value.Nullable));

        await using var precision = new NpgsqlCommand("SELECT numeric_precision::integer, numeric_scale::integer FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'transfers' AND column_name = 'amount'", connection);
        await using (var reader = await precision.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(28, reader.GetInt32(0));
            Assert.Equal(8, reader.GetInt32(1));
        }

        string[] expectedConstraints = ["pk_transfers", "ck_transfers_id", "ck_transfers_amount", "ck_transfers_currency", "ck_transfers_client_reference", "ck_transfers_idempotency_key", "ck_transfers_request_fingerprint", "ck_transfers_state_version", "ck_transfers_provider_reference", "ck_transfers_timestamps"];
        await using var constraints = new NpgsqlCommand("SELECT conname FROM pg_constraint WHERE conrelid = 'public.transfers'::regclass", connection);
        var names = new List<string>();
        await using (var reader = await constraints.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.Equal(expectedConstraints.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        await using var index = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_transfers_idempotency_key'", connection);
        Assert.Contains("UNIQUE", Assert.IsType<string>(await index.ExecuteScalarAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        await using var database = OpenContext(connectionString);
        string[] migrations = (await database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_DurableIdempotencyAndSubmissionClaim", migrations[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.999")]
    [InlineData("0.00000001")]
    [InlineData("99999999999999999999.99999999")]
    public async Task TransferRoundTrip_AfterContextDisposal_PreservesExactMoneyAndAcceptedEvidence(string amount)
    {
        string connectionString = await CreateDatabaseAsync();
        Transfer transfer = Transfer.Create(Identity, "order-1", "key-1", new Money(Money.ParseAmount(amount), "usd"), Timestamp);
        await using (var database = OpenContext(connectionString))
        {
            PostgresTransferStore store = Store(database);
            await store.AddAsync(transfer, TestContext.Current.CancellationToken);
            await AcceptAsync(store, transfer);
        }

        await using var freshDatabase = OpenContext(connectionString);
        Transfer? reloaded = await Store(freshDatabase).FindAsync(Identity, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(transfer.Id, reloaded.Id);
        Assert.Equal(transfer.ClientReference, reloaded.ClientReference);
        Assert.Equal(transfer.IdempotencyKey, reloaded.IdempotencyKey);
        Assert.Equal(transfer.Money, reloaded.Money);
        Assert.Equal(TransferState.Accepted, reloaded.State);
        Assert.Equal("synthetic-accepted", reloaded.ProviderReference);
        Assert.Equal(transfer.CreatedAt, reloaded.CreatedAt);
        Assert.Equal(transfer.UpdatedAt, reloaded.UpdatedAt);
        Assert.Equal(3, reloaded.Version);
    }

    [Fact]
    public async Task TwoIndependentContexts_StaleVersionCannotOverwriteFirstLegalTransition()
    {
        string connectionString = await CreateDatabaseAsync();
        await using (var setup = OpenContext(connectionString))
        {
            await Store(setup).AddAsync(NewTransfer(), TestContext.Current.CancellationToken);
        }

        await using var firstDatabase = OpenContext(connectionString);
        await using var secondDatabase = OpenContext(connectionString);
        PostgresTransferStore firstStore = Store(firstDatabase);
        PostgresTransferStore secondStore = Store(secondDatabase);
        Transfer? first = await firstStore.FindAsync(Identity, TestContext.Current.CancellationToken);
        Transfer? second = await secondStore.FindAsync(Identity, TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        Assert.NotNull(second);
        first.MarkReadyToSubmit(Timestamp.AddMinutes(1));
        second.MarkReadyToSubmit(Timestamp.AddMinutes(2));
        await firstStore.UpdateAsync(first, 0, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TransferConcurrencyException>(() =>
            secondStore.UpdateAsync(second, 0, TestContext.Current.CancellationToken));

        await using var verification = OpenContext(connectionString);
        Transfer? persisted = await Store(verification).FindAsync(Identity, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Version);
        Assert.Equal(Timestamp.AddMinutes(1), persisted.UpdatedAt);
    }

    [Fact]
    public async Task TwoIndependentStores_ReadyToSubmitClaim_HasExactlyOneWinner()
    {
        string connectionString = await CreateDatabaseAsync();
        var initial = Transfer.Create(Identity, "order-1", "key-1", new Money(10.999m, "USD"), Timestamp);
        initial.MarkReadyToSubmit(Timestamp);
        await using (var setup = OpenContext(connectionString))
        {
            await Store(setup).CreateOrGetAsync(initial,
                TransferRequestFingerprint.Compute(initial.ClientReference, initial.Money.Amount, initial.Money.Currency),
                TransferRequestFingerprint.CurrentVersion, TestContext.Current.CancellationToken);
        }

        await using var firstDatabase = OpenContext(connectionString);
        await using var secondDatabase = OpenContext(connectionString);
        Transfer first = (await Store(firstDatabase).FindAsync(Identity, TestContext.Current.CancellationToken))!;
        Transfer second = (await Store(secondDatabase).FindAsync(Identity, TestContext.Current.CancellationToken))!;
        first.BeginSubmission(Timestamp.AddMinutes(1));
        second.BeginSubmission(Timestamp.AddMinutes(2));
        Task<SubmissionClaimResult> firstClaim = Store(firstDatabase).TryClaimSubmissionAsync(first, 1,
            TestContext.Current.CancellationToken);
        Task<SubmissionClaimResult> secondClaim = Store(secondDatabase).TryClaimSubmissionAsync(second, 1,
            TestContext.Current.CancellationToken);
        SubmissionClaimResult[] claims = await Task.WhenAll(firstClaim, secondClaim);

        Assert.Equal(1, claims.Count(claim => claim == SubmissionClaimResult.ClaimAcquired));
        Assert.Equal(1, claims.Count(claim => claim == SubmissionClaimResult.AlreadyClaimedOrSubmitted));
        await using var verification = OpenContext(connectionString);
        Transfer persisted = (await Store(verification).FindAsync(Identity, TestContext.Current.CancellationToken))!;
        Assert.Equal(TransferState.Submitting, persisted.State);
        Assert.Equal(2, persisted.Version);
    }

    [Fact]
    public async Task ApiPostAndFreshHostGet_ReplaySameRequestAndConflictChangedRequestWithoutResubmission()
    {
        string connectionString = await CreateDatabaseAsync();
        int submissions = 0;
        TransferResponse created;
        await using (var rootFactory = new FaultLedgerApiFactory(connectionString))
        await using (var factory = rootFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransferProvider>();
            services.AddScoped<ITransferProvider>(provider => new InspectingProvider(async (request, cancellationToken) =>
            {
                Assert.Null(provider.GetRequiredService<FaultLedgerDbContext>().Database.CurrentTransaction);
                await using var independent = OpenContext(connectionString);
                Transfer? durable = await Store(independent).FindAsync(request.TransferId, cancellationToken);
                Assert.NotNull(durable);
                Assert.Equal(TransferState.Submitting, durable.State);
                Assert.Equal(2, durable.Version);
                Interlocked.Increment(ref submissions);
            }));
        })))
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", Request(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            TransferResponse? body = await response.Content.ReadFromJsonAsync<TransferResponse>(TestContext.Current.CancellationToken);
            Assert.NotNull(body);
            created = body;
            Assert.Equal("Accepted", created.State);
            Assert.Equal("USD", created.Currency);
            Assert.Equal(10.999m, created.Amount);
            Assert.Equal($"/api/transfers/{created.Id:D}", response.Headers.Location?.OriginalString);
            Assert.Equal($"mock-{created.Id:N}-00000001", created.ProviderReference);

            using HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/transfers", Request(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            TransferResponse? replayed = await duplicate.Content.ReadFromJsonAsync<TransferResponse>(TestContext.Current.CancellationToken);
            Assert.Equal(created, replayed);
            using HttpResponseMessage different = await client.PostAsJsonAsync("/api/transfers", Request(20m), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(1, submissions);
        }

        await using var restarted = new FaultLedgerApiFactory(connectionString);
        using HttpClient restartedClient = restarted.CreateClient();
        TransferResponse? reloaded = await restartedClient.GetFromJsonAsync<TransferResponse>($"/api/transfers/{created.Id:D}", TestContext.Current.CancellationToken);
        Assert.Equal(created, reloaded);
        using HttpResponseMessage replay = await restartedClient.PostAsJsonAsync("/api/transfers", Request(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        TransferResponse? restartedReplay = await replay.Content.ReadFromJsonAsync<TransferResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(created, restartedReplay);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM transfers", connection);
        Assert.Equal(1L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingTransferGet_ReturnsTyped404WithoutProviderSubmission()
    {
        string connectionString = await CreateDatabaseAsync();
        await using var factory = new FaultLedgerApiFactory(connectionString);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync($"/api/transfers/{Identity:D}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Transfer not found", problem.Title);
    }

    [Theory]
    [InlineData("amount = 0", "ck_transfers_amount")]
    [InlineData("currency = 'usd'", "ck_transfers_currency")]
    [InlineData("state = 'unrecognized'", "ck_transfers_state_version")]
    [InlineData("version = 10", "ck_transfers_state_version")]
    [InlineData("provider_reference = 'unearned'", "ck_transfers_provider_reference")]
    public async Task InvalidPersistedRepresentations_AreRejectedByPostgres(string assignment, string constraint)
    {
        string connectionString = await CreateDatabaseAsync();
        await using (var database = OpenContext(connectionString))
        {
            await Store(database).AddAsync(NewTransfer(), TestContext.Current.CancellationToken);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"UPDATE transfers SET {assignment} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", Identity);
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(constraint, failure.ConstraintName);
    }

    private static object Request(decimal amount = 10.999m) => new
    {
        clientReference = "order-1",
        idempotencyKey = "key-1",
        amount,
        currency = "usd"
    };

    private static Transfer NewTransfer() => Transfer.Create(Identity, "order-1", "key-1", new Money(10.999m, "USD"), Timestamp);

    private static FaultLedgerDbContext OpenContext(string connectionString) => new(
        new DbContextOptionsBuilder<FaultLedgerDbContext>().UseNpgsql(connectionString).Options);

    private static PostgresTransferStore Store(FaultLedgerDbContext context) =>
        new(context, NullLogger<PostgresTransferStore>.Instance);

    private static FaultLedgerApiFactory CreateFactory(string connectionString, MockProviderLedger ledger,
        MockProviderScenario scenario) => new FaultLedgerApiFactory(connectionString).WithServices(services =>
        {
            services.RemoveAll<MockProviderLedger>();
            services.RemoveAll<ITransferProvider>();
            services.AddSingleton(ledger);
            services.AddScoped<ITransferProvider>(_ => new SyntheticTransferProvider(ledger, scenario,
                TimeProvider.System));
        });

    private static async Task<long> TransferCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM transfers", connection);
        return Assert.IsType<long>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<decimal> StoredAmountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT amount FROM transfers", connection);
        return Assert.IsType<decimal>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task AcceptAsync(PostgresTransferStore store, Transfer transfer)
    {
        transfer.MarkReadyToSubmit(Timestamp.AddMinutes(1));
        await store.UpdateAsync(transfer, 0, TestContext.Current.CancellationToken);
        transfer.BeginSubmission(Timestamp.AddMinutes(2));
        await store.UpdateAsync(transfer, 1, TestContext.Current.CancellationToken);
        transfer.MarkAccepted("synthetic-accepted", Timestamp.AddMinutes(3));
        await store.UpdateAsync(transfer, 2, TestContext.Current.CancellationToken);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        string name = $"stage1_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        string connectionString = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString()) { Database = name }.ConnectionString;
        await using var database = OpenContext(connectionString);
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private sealed class InspectingProvider(Func<ProviderTransferRequest, CancellationToken, Task> inspect) : ITransferProvider
    {
        public async Task<ProviderSubmissionResult> SubmitAsync(ProviderTransferRequest request, CancellationToken cancellationToken)
        {
            await inspect(request, cancellationToken);
            return await new SyntheticTransferProvider(new MockProviderLedger(), MockProviderScenario.Success,
                TimeProvider.System).SubmitAsync(request, cancellationToken);
        }
    }
}
