using FaultLedger.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace FaultLedger.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddPostgresReadiness();

        var app = builder.Build();

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
