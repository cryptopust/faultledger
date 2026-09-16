using FaultLedger.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FaultLedger.IntegrationTests;

public sealed class FaultLedgerApiFactory(string? connectionString) : WebApplicationFactory<Program>
{
    private Action<IServiceCollection>? configureServices;

    public FaultLedgerApiFactory WithServices(Action<IServiceCollection> configure)
    {
        configureServices = configure;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString
            }));
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}
