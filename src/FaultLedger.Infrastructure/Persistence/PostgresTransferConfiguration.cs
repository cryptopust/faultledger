using FaultLedger.Application.Transfers;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FaultLedger.Infrastructure.Persistence;

internal static class PostgresTransferConfiguration
{
    public static string GetConnectionString(IConfiguration configuration)
    {
        string? value = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TransferStorageUnavailableException();
        }

        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(value)
            {
                Timeout = 2,
                CommandTimeout = 5,
                CancellationTimeout = 1000,
                IncludeErrorDetail = false,
                PersistSecurityInfo = false
            };
        }
        catch (ArgumentException)
        {
            throw new TransferStorageUnavailableException();
        }

        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database) ||
            string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password) ||
            settings.Password == "CHANGE_ME_LOCAL_ONLY")
        {
            throw new TransferStorageUnavailableException();
        }

        return settings.ConnectionString;
    }
}
