using System.Text.Json.Serialization;
using FaultLedger.Api.Transfers;
using FaultLedger.Application.Transfers;
using FaultLedger.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace FaultLedger.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddPostgresReadiness();
        builder.Services.AddTransferInfrastructure();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<TransferService>();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapTransferEndpoints();

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
