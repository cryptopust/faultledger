using Testcontainers.PostgreSql;

namespace FaultLedger.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public PostgreSqlContainer Container => container ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.6-alpine3.23@sha256:697c180dbf244d3ce4a8f4cbc0156cde840af055c1bf8b76aebe422a4822086f")
            .WithDatabase("faultledger_test")
            .WithUsername("faultledger_test")
            .WithPassword("faultledger_test_only")
            .Build();

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await container.StartAsync(deadline.Token);
    }

    public ValueTask DisposeAsync() => container?.DisposeAsync() ?? ValueTask.CompletedTask;
}
