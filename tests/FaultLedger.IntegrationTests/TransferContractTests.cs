using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
}
