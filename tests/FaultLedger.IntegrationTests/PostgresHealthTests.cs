using System.Net;
using Npgsql;

namespace FaultLedger.IntegrationTests;

[Trait("Category", "RequiresDocker")]
public sealed class PostgresHealthTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task PostgreSqlReachable_LiveAndReadyAreHealthy()
    {
        await using var factory = new FaultLedgerApiFactory(fixture.Container.GetConnectionString());
        using HttpClient client = factory.CreateClient();

        await HealthEndpointTests.AssertHealthAsync(client, "/health/live", HttpStatusCode.OK, "Healthy");
        await HealthEndpointTests.AssertHealthAsync(client, "/health/ready", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task FreshPostgreSql_NoApplicationTablesExist()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.Container.GetConnectionString());
        await using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");

        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0L, Assert.IsType<long>(count));
    }

    [Fact]
    public async Task PostgreSqlStopsAndRestarts_RecreatedHostRecoversWhileLivenessStaysHealthy()
    {
        await using var factory = new FaultLedgerApiFactory(fixture.Container.GetConnectionString());
        using HttpClient client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));

        await HealthEndpointTests.AssertHealthAsync(client, "/health/ready", HttpStatusCode.OK, "Healthy");
        await fixture.Container.StopAsync(deadline.Token);
        try
        {
            await HealthEndpointTests.AssertHealthAsync(client, "/health/live", HttpStatusCode.OK, "Healthy");
            await HealthEndpointTests.AssertHealthAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        }
        finally
        {
            using var recoveryDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await fixture.Container.StartAsync(recoveryDeadline.Token);
        }

        await using var recoveredFactory = new FaultLedgerApiFactory(fixture.Container.GetConnectionString());
        using HttpClient recoveredClient = recoveredFactory.CreateClient();
        await HealthEndpointTests.AssertHealthAsync(recoveredClient, "/health/ready", HttpStatusCode.OK, "Healthy");
    }
}
