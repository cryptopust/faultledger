using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FaultLedger.Infrastructure;

internal sealed class PostgresReadinessHealthCheck : IHealthCheck, IAsyncDisposable
{
    private readonly NpgsqlDataSource? dataSource;
    private readonly ILogger<PostgresReadinessHealthCheck> logger;

    public PostgresReadinessHealthCheck(
        IConfiguration configuration,
        ILogger<PostgresReadinessHealthCheck> logger)
    {
        this.logger = logger;
        string? connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 2,
                CommandTimeout = 2,
                CancellationTimeout = 1000,
                IncludeErrorDetail = false,
                PersistSecurityInfo = false
            };

            if (string.IsNullOrWhiteSpace(settings.Host) ||
                string.IsNullOrWhiteSpace(settings.Username) ||
                string.IsNullOrWhiteSpace(settings.Password) ||
                settings.Password == "CHANGE_ME_LOCAL_ONLY")
            {
                return;
            }

            dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning("Invalid PostgreSQL readiness configuration ({FailureType}).",
                exception.GetType().Name);
        }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataSource is null)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection is not configured safely.");
        }

        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return result is 1
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL returned an unexpected readiness result.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogWarning("PostgreSQL readiness failed ({FailureType}).", exception.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }

    public ValueTask DisposeAsync() => dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;
}
