using System.Text.Json;
using Npgsql;

namespace FaultLedger.SimulatedConsumer;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(provider => CreateDataSource(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddScoped<SimulatedConsumerStore>();

        var app = builder.Build();
        app.MapGet("/health/live", () => TypedResults.Ok("Healthy"));
        app.MapPost("/integration-events", ReceiveAsync);
        app.MapGet("/integration-events/{eventId:guid}", FindAsync);
        await app.RunAsync();
    }

    private static async Task<IResult> ReceiveAsync(IntegrationEventRequest request,
        SimulatedConsumerStore store, CancellationToken cancellationToken)
    {
        string? error = request.Validate();
        if (error is not null)
        {
            return TypedResults.BadRequest(new { error });
        }

        ConsumerEventState? state = await store.ReceiveAsync(request, cancellationToken);
        return state is null
            ? TypedResults.Conflict(new { error = "The event ID belongs to a different immutable message." })
            : TypedResults.Ok(state);
    }

    private static async Task<IResult> FindAsync(Guid eventId, SimulatedConsumerStore store,
        CancellationToken cancellationToken)
    {
        ConsumerEventState? state = await store.FindAsync(eventId, cancellationToken);
        return state is null ? TypedResults.NotFound() : TypedResults.Ok(state);
    }

    private static NpgsqlDataSource CreateDataSource(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("ConsumerPostgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The simulated consumer PostgreSQL connection is required.");
        }

        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            PersistSecurityInfo = false,
            Timeout = 2,
            CommandTimeout = 5,
            CancellationTimeout = 1000
        };
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database) ||
            string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password) ||
            settings.Password == "CHANGE_ME_LOCAL_ONLY")
        {
            throw new InvalidOperationException("The simulated consumer PostgreSQL connection is invalid.");
        }

        return NpgsqlDataSource.Create(settings.ConnectionString);
    }
}

public sealed record IntegrationEventRequest(Guid EventId, string EventType, int SchemaVersion, Guid AggregateId,
    long AggregateVersion, DateTimeOffset OccurredAt, JsonElement Payload)
{
    public string? Validate()
    {
        if (EventId == Guid.Empty || AggregateId == Guid.Empty || AggregateVersion <= 0)
        {
            return "Event and aggregate identity are required.";
        }

        if (EventType != "TransferCompleted" || SchemaVersion != 1)
        {
            return "The event type or schema version is unsupported.";
        }

        if (Payload.ValueKind != JsonValueKind.Object || Payload.GetRawText().Length > 2048)
        {
            return "The event payload must be a bounded JSON object.";
        }

        return null;
    }
}

public sealed record ConsumerEventState(Guid EventId, int ReceiptCount, int LogicalEffectCount);

public sealed class SimulatedConsumerStore(NpgsqlDataSource dataSource, TimeProvider timeProvider)
{
    public async Task<ConsumerEventState?> ReceiveAsync(IntegrationEventRequest request,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("""
            INSERT INTO simulated_consumer_events
                (event_id, event_type, schema_version, aggregate_id, aggregate_version, payload,
                 first_received_at, last_received_at, receipt_count, logical_effect_count)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $7, 1, 1)
            ON CONFLICT (event_id) DO UPDATE SET
                receipt_count = simulated_consumer_events.receipt_count + 1,
                last_received_at = EXCLUDED.last_received_at
            WHERE simulated_consumer_events.event_type = EXCLUDED.event_type
              AND simulated_consumer_events.schema_version = EXCLUDED.schema_version
              AND simulated_consumer_events.aggregate_id = EXCLUDED.aggregate_id
              AND simulated_consumer_events.aggregate_version = EXCLUDED.aggregate_version
              AND simulated_consumer_events.payload = EXCLUDED.payload
            RETURNING event_id, receipt_count, logical_effect_count
            """);
        command.Parameters.AddWithValue(request.EventId);
        command.Parameters.AddWithValue(request.EventType);
        command.Parameters.AddWithValue(request.SchemaVersion);
        command.Parameters.AddWithValue(request.AggregateId);
        command.Parameters.AddWithValue(request.AggregateVersion);
        command.Parameters.AddWithValue(request.Payload.GetRawText());
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ConsumerEventState(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2))
            : null;
    }

    public async Task<ConsumerEventState?> FindAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("""
            SELECT event_id, receipt_count, logical_effect_count
            FROM simulated_consumer_events
            WHERE event_id = $1
            """);
        command.Parameters.AddWithValue(eventId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ConsumerEventState(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2))
            : null;
    }
}
