using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FaultLedger.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresReadiness(this IServiceCollection services)
    {
        services.AddSingleton<PostgresReadinessHealthCheck>();
        services.AddHealthChecks().AddCheck<PostgresReadinessHealthCheck>(
            "postgres",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
