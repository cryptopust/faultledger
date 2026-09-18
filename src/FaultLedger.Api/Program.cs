using System.Text.Json.Serialization;
using FaultLedger.Api.Callbacks;
using FaultLedger.Api.Transfers;
using FaultLedger.Application.Diagnostics;
using FaultLedger.Application.Transfers;
using FaultLedger.Infrastructure;
using FaultLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FaultLedger.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddPostgresReadiness();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(FaultLedgerTelemetry.SourceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(FaultLedgerTelemetry.MeterName));
        builder.Services.AddTransferInfrastructure();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<TransferService>();
        builder.Services.AddScoped<TransferReconciliationService>();
        builder.Services.AddScoped<ProviderCallbackService>();
        builder.Services.AddSingleton<ProviderCallbackAuthenticator>();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        var app = builder.Build();
        if (args.Contains("--migrate-only", StringComparer.Ordinal))
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FaultLedgerDbContext>().Database.MigrateAsync();
            return;
        }

        app.UseExceptionHandler();
        app.MapTransferEndpoints();
        app.MapProviderCallbackEndpoints();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).WithMetadata(new HttpMethodMetadata(["GET"]));

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        }).WithMetadata(new HttpMethodMetadata(["GET"]));

        await app.RunAsync();
    }
}
