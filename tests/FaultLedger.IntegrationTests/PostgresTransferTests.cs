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

        Assert.Equal(new[] { "id", "client_reference", "idempotency_key", "amount", "currency", "state", "provider_reference", "created_at", "updated_at", "version" }, actual.Keys);
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

        string[] expectedConstraints = ["pk_transfers", "ck_transfers_id", "ck_transfers_amount", "ck_transfers_currency", "ck_transfers_client_reference", "ck_transfers_idempotency_key", "ck_transfers_state_version", "ck_transfers_provider_reference", "ck_transfers_timestamps"];
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
        await using var index = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'ux_transfers_idempotency_key'", connection);
        Assert.Contains("UNIQUE", Assert.IsType<string>(await index.ExecuteScalarAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        await using var database = OpenContext(connectionString);
        string migration = Assert.Single(await database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.EndsWith("_InitialTransferPersistence", migration, StringComparison.Ordinal);
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
    public async Task ApiPostAndFreshHostGet_PreserveAcceptedStateAndRejectDuplicateKeysWithoutResubmission()
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
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using HttpResponseMessage different = await client.PostAsJsonAsync("/api/transfers", Request(20m), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(1, submissions);
        }

        await using var restarted = new FaultLedgerApiFactory(connectionString);
        using HttpClient restartedClient = restarted.CreateClient();
        TransferResponse? reloaded = await restartedClient.GetFromJsonAsync<TransferResponse>($"/api/transfers/{created.Id:D}", TestContext.Current.CancellationToken);
        Assert.Equal(created, reloaded);
        using HttpResponseMessage replay = await restartedClient.PostAsJsonAsync("/api/transfers", Request(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
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
