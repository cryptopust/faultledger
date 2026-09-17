using FaultLedger.Application.IntegrationEvents;
using FaultLedger.Application.Transfers;
using FaultLedger.Infrastructure.IntegrationEvents;
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
        services.AddScoped<IProviderInboxStore, PostgresProviderInboxStore>();
        services.AddScoped<IOutboxStore, PostgresOutboxStore>();
        services.AddScoped<OutboxDispatcher>();
        services.AddHttpClient(nameof(HttpIntegrationEventPublisher), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddOptions<IntegrationEventPublisherOptions>()
            .BindConfiguration("IntegrationEvents:Publisher");
        services.AddOptions<OutboxDispatcherWorkerOptions>()
            .BindConfiguration("IntegrationEvents:Dispatcher");
        services.AddScoped<IIntegrationEventPublisher, HttpIntegrationEventPublisher>();
        services.AddSingleton<IOutboxDispatchHook, NoOpOutboxDispatchHook>();
        services.AddSingleton<IOutboxPersistenceHook, NoOpOutboxPersistenceHook>();
        services.AddHostedService<OutboxDispatcherWorker>();
        services.AddSingleton<IProviderInboxProcessingHook, NoOpProviderInboxProcessingHook>();
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
