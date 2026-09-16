using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FaultLedger.Api.Transfers;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FaultLedger.IntegrationTests;

public sealed class TransferContractTests
{
    [Theory]
    [InlineData("1.000000001")]
    [InlineData("1.23456789012345678901234567899")]
    [InlineData("100000000000000000000")]
    [InlineData("1e2")]
    [InlineData("\"1.25\"")]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task InvalidAmountToken_Returns400WithoutDatabaseOrProvider(string amount)
    {
        await using var factory = new FaultLedgerApiFactory("Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only");
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent($$"""{"clientReference":"order-1","idempotencyKey":"key-1","amount":{{amount}},"currency":"USD"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/transfers", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("stackTrace", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestCannotSupplyItsOwnTransferState()
    {
        await using var factory = new FaultLedgerApiFactory("Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only");
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent("""{"clientReference":"order-1","idempotencyKey":"key-1","amount":1,"currency":"USD","state":"Completed"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/transfers", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HeaderAndBodyIdempotencyKeys_MustAgreeBeforePersistence()
    {
        await using var factory = new FaultLedgerApiFactory("Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "header-key");
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/transfers", new
        {
            clientReference = "order-1",
            idempotencyKey = "body-key",
            amount = 10m,
            currency = "USD"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid transfer request", problem.Title);
    }

    [Fact]
    public void EfModelAndMigrationSnapshot_AgreeOnExactColumnsAndConcurrencyWithoutClaimingDatabaseExecution()
    {
        using var database = new FaultLedgerDbContext(new DbContextOptionsBuilder<FaultLedgerDbContext>()
            .UseNpgsql("Host=localhost;Database=model_inspection_only").Options);
        var entity = Assert.Single(database.Model.GetEntityTypes());
        Assert.Equal("transfers", entity.GetTableName());
        var amount = entity.FindProperty("Amount");
        Assert.NotNull(amount);
        Assert.Equal(28, amount.GetPrecision());
        Assert.Equal(8, amount.GetScale());
        var version = entity.FindProperty("Version");
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.False(database.Database.HasPendingModelChanges());
        string script = database.GetService<IMigrator>().GenerateScript();
        Assert.Contains("numeric(28,8)", script, StringComparison.Ordinal);
        Assert.Contains("uq_transfers_idempotency_key", script, StringComparison.Ordinal);
        Assert.Contains("request_fingerprint", script, StringComparison.Ordinal);
        Assert.Contains("fingerprint_version", script, StringComparison.Ordinal);
        Assert.Contains("ck_transfers_state_version", script, StringComparison.Ordinal);
        Assert.DoesNotContain("double precision", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyntheticProvider_SuccessUsesIdentityAndAttemptDeterministically()
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.Success,
            TimeProvider.System);
        var request = new ProviderTransferRequest(Guid.Parse("11111111-1111-1111-1111-111111111111"), "order-1", new Money(1, "USD"));
        ProviderSubmissionResult first = await provider.SubmitAsync(request, TestContext.Current.CancellationToken);
        ProviderSubmissionResult second = await provider.SubmitAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("mock-11111111111111111111111111111111-00000001", first.ProviderReference);
        Assert.Equal("mock-11111111111111111111111111111111-00000002", second.ProviderReference);
        Assert.Equal(2, ledger.SubmissionAttempts);
        Assert.Equal(2, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task SyntheticProvider_AlreadyCancelledRequestDoesNotCreateAnAttempt()
    {
        var ledger = new MockProviderLedger();
        var provider = new SyntheticTransferProvider(ledger, MockProviderScenario.Success, TimeProvider.System);
        var request = new ProviderTransferRequest(Guid.Parse("11111111-1111-1111-1111-111111111111"), "order-1", new Money(1, "USD"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SubmitAsync(request, cancellation.Token));
        Assert.Equal(0, ledger.SubmissionAttempts);
        Assert.Equal(0, ledger.AcceptedOperationCount);
    }

    [Fact]
    public async Task AmbiguousHttpContract_ExposesDoNotRepostAndExplicitReconciliation()
    {
        var ledger = new MockProviderLedger();
        var store = new ContractStore();
        await using var root = new FaultLedgerApiFactory(
            "Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only");
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransferStore>();
            services.RemoveAll<ITransferProvider>();
            services.RemoveAll<ITransferLookup>();
            services.AddSingleton<ITransferStore>(store);
            services.AddScoped<ITransferProvider>(_ => new SyntheticTransferProvider(ledger,
                MockProviderScenario.TimeoutAfterAccept, TimeProvider.System));
            services.AddScoped<ITransferLookup>(_ => new SyntheticTransferProvider(ledger,
                MockProviderScenario.Success, TimeProvider.System));
        }));
        using HttpClient client = factory.CreateClient();
        var request = new
        {
            clientReference = "order-unknown",
            idempotencyKey = "key-unknown",
            amount = 10m,
            currency = "USD"
        };

        using HttpResponseMessage initial = await client.PostAsJsonAsync("/api/transfers", request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, initial.StatusCode);
        TransferResponse created = Assert.IsType<TransferResponse>(
            await initial.Content.ReadFromJsonAsync<TransferResponse>(TestContext.Current.CancellationToken));
        Assert.Equal("Unknown", created.State);
        Assert.False(created.IsFinal);
        Assert.Equal(nameof(RetryAdvice.DoNotRepost), created.RetryAdvice);

        using HttpResponseMessage replay = await client.PostAsJsonAsync("/api/transfers", request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        TransferResponse replayed = Assert.IsType<TransferResponse>(
            await replay.Content.ReadFromJsonAsync<TransferResponse>(TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, replayed.Id);
        Assert.Equal("Unknown", replayed.State);

        TransferResponse loaded = Assert.IsType<TransferResponse>(await client.GetFromJsonAsync<TransferResponse>(
            $"/api/transfers/{created.Id:D}", TestContext.Current.CancellationToken));
        Assert.Equal(nameof(RetryAdvice.DoNotRepost), loaded.RetryAdvice);

        using HttpResponseMessage reconciliation = await client.PostAsync(
            $"/api/transfers/{created.Id:D}/reconcile", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reconciliation.StatusCode);
        ReconciliationResponse resolved = Assert.IsType<ReconciliationResponse>(
            await reconciliation.Content.ReadFromJsonAsync<ReconciliationResponse>(
                TestContext.Current.CancellationToken));
        Assert.Equal(nameof(ReconciliationOutcome.ResolvedAccepted), resolved.Outcome);
        Assert.Equal("Accepted", resolved.Transfer.State);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
        Assert.Equal(1, ledger.LookupAttempts);
    }

    [Fact]
    public async Task LookupTransportFailure_Returns503AndLeavesUnknownWithoutRepost()
    {
        var ledger = new MockProviderLedger();
        var store = new ContractStore();
        await using var root = new FaultLedgerApiFactory(
            "Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only");
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransferStore>();
            services.RemoveAll<ITransferProvider>();
            services.RemoveAll<ITransferLookup>();
            services.AddSingleton<ITransferStore>(store);
            services.AddScoped<ITransferProvider>(_ => new SyntheticTransferProvider(ledger,
                MockProviderScenario.TimeoutAfterAccept, TimeProvider.System));
            services.AddScoped<ITransferLookup, UnavailableLookup>();
        }));
        using HttpClient client = factory.CreateClient();
        var request = new
        {
            clientReference = "order-lookup-failure",
            idempotencyKey = "key-lookup-failure",
            amount = 10m,
            currency = "USD"
        };

        TransferResponse created = Assert.IsType<TransferResponse>(await (await client.PostAsJsonAsync(
            "/api/transfers", request, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<TransferResponse>(
                TestContext.Current.CancellationToken));
        using HttpResponseMessage reconciliation = await client.PostAsync(
            $"/api/transfers/{created.Id:D}/reconcile", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconciliation.StatusCode);
        Assert.Equal(1, ledger.SubmissionAttempts);
        Assert.Equal(1, ledger.AcceptedOperationCount);
        TransferResponse current = Assert.IsType<TransferResponse>(await client.GetFromJsonAsync<TransferResponse>(
            $"/api/transfers/{created.Id:D}", TestContext.Current.CancellationToken));
        Assert.Equal("Unknown", current.State);
        Assert.Equal(nameof(RetryAdvice.DoNotRepost), current.RetryAdvice);
    }

    private sealed class ContractStore : ITransferStore
    {
        private Transfer? current;
        private string? fingerprint;
        private int? fingerprintVersion;

        public Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint,
            int requestFingerprintVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not null)
            {
                return Task.FromResult(new TransferRegistration(Clone(current), fingerprint,
                    fingerprintVersion, false));
            }

            current = Clone(transfer);
            fingerprint = requestFingerprint;
            fingerprintVersion = requestFingerprintVersion;
            return Task.FromResult(new TransferRegistration(Clone(current), fingerprint,
                fingerprintVersion, true));
        }

        public Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null)
            {
                return Task.FromResult(SubmissionClaimResult.NotFound);
            }

            if (current.State != TransferState.ReadyToSubmit || current.Version != expectedVersion)
            {
                return Task.FromResult(SubmissionClaimResult.AlreadyClaimedOrSubmitted);
            }

            current = Clone(transfer);
            return Task.FromResult(SubmissionClaimResult.ClaimAcquired);
        }

        public Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(current?.Id == id ? Clone(current) : null);
        }

        public Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || current.Id != transfer.Id || current.Version != expectedVersion)
            {
                throw new TransferConcurrencyException();
            }

            current = Clone(transfer);
            return Task.CompletedTask;
        }

        public Task AddAsync(Transfer transfer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static Transfer Clone(Transfer source) => Transfer.Restore(source.Id, source.ClientReference,
            source.IdempotencyKey, source.Money, source.State, source.ProviderReference,
            source.CreatedAt, source.UpdatedAt, source.Version);
    }

    private sealed class UnavailableLookup : ITransferLookup
    {
        public Task<ProviderLookupResult> LookupAsync(ProviderLookupRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<ProviderLookupResult>(new TimeoutException("synthetic lookup timeout"));
    }
}
