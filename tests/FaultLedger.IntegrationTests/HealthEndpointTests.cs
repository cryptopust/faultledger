using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace FaultLedger.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-connection-string")]
    [InlineData("Host=localhost;Username=example;Password=CHANGE_ME_LOCAL_ONLY")]
    public async Task MissingOrInvalidPostgresConfiguration_LiveHealthyAndReadyUnhealthy(string? connectionString)
    {
        await using var factory = new FaultLedgerApiFactory(connectionString);
        using HttpClient client = factory.CreateClient();
        await AssertHealthAsync(client, "/health/live", HttpStatusCode.OK, "Healthy");
        await AssertHealthAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
    }

    [Fact]
    public async Task PostgresEndpointRefusesConnections_LiveHealthyAndReadyUnhealthy()
    {
        using var reservedEndpoint = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservedEndpoint.ExclusiveAddressUse = true;
        reservedEndpoint.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var address = Assert.IsType<IPEndPoint>(reservedEndpoint.LocalEndPoint);
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = address.Port,
            Database = "faultledger_test",
            Username = "faultledger_test",
            Password = "faultledger_test_only",
            Pooling = false
        };

        await using var factory = new FaultLedgerApiFactory(settings.ConnectionString);
        using HttpClient client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        await AssertHealthAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await AssertHealthAsync(client, "/health/live", HttpStatusCode.OK, "Healthy");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoint_PostRequest_IsNotAllowed(string path)
    {
        await using var factory = new FaultLedgerApiFactory(null);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsync(path, null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task TemplateEndpoint_DoesNotExist()
    {
        await using var factory = new FaultLedgerApiFactory(null);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static async Task AssertHealthAsync(HttpClient client, string path, HttpStatusCode status, string body)
    {
        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }
}
