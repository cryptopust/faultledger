using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FaultLedger.Infrastructure.Persistence;

public sealed class FaultLedgerDbContextFactory : IDesignTimeDbContextFactory<FaultLedgerDbContext>
{
    public FaultLedgerDbContext CreateDbContext(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        string connectionString = PostgresTransferConfiguration.GetConnectionString(configuration);
        var options = new DbContextOptionsBuilder<FaultLedgerDbContext>().UseNpgsql(connectionString).Options;
        return new FaultLedgerDbContext(options);
    }
}
