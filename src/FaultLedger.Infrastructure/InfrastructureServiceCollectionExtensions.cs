using FaultLedger.Application.Transfers;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FaultLedger.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTransferInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<FaultLedgerDbContext>((provider, options) =>
            options.UseNpgsql(PostgresTransferConfiguration.GetConnectionString(provider.GetRequiredService<IConfiguration>())));
        services.AddScoped<ITransferStore, PostgresTransferStore>();
        services.AddSingleton<MockProviderLedger>();
        services.AddScoped<ITransferProvider>(provider => new SyntheticTransferProvider(
            provider.GetRequiredService<MockProviderLedger>(), MockProviderScenario.Success,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ITransferLookup>(provider => new SyntheticTransferProvider(
            provider.GetRequiredService<MockProviderLedger>(), MockProviderScenario.Success,
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }

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
