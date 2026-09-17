using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using FaultLedger.Application.IntegrationEvents;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using FaultLedger.Infrastructure.Persistence;
using FaultLedger.SimulatedConsumer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FaultLedger.IntegrationTests;

[Trait("Category", "RequiresDocker")]
public sealed class ToxiproxyNetworkTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string ToxiproxyImage =
        "ghcr.io/shopify/toxiproxy:2.12.0@sha256:9378ed52a28bc50edc1350f936f518f31fa95f0d15917d6eb40b8e376d1a214e";
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NormalDelivery_TraversesToxiproxyAndAppliesOneDurableConsumerEffect()
    {
        await using NetworkLab lab = await NetworkLab.StartAsync(fixture,
            Guid.Parse("40000000-0000-0000-0000-000000000001"));

        Assert.Equal(OutboxDispatchResult.Published,
            await lab.DispatchAsync("normal-worker", TestContext.Current.CancellationToken));

        ConsumerEventState state = await lab.ReadConsumerStateAsync();
        Assert.Equal(1, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
        Assert.Equal(1, await lab.ReadAttemptCountAsync());
        Assert.True(await lab.IsPublishedAsync());
    }

    [Fact]
    public async Task ConnectionUnavailable_LeavesMessageRetryableAndRecoveryDeliversWithoutLoss()
    {
        await using NetworkLab lab = await NetworkLab.StartAsync(fixture,
            Guid.Parse("40000000-0000-0000-0000-000000000002"));
        await lab.SetProxyEnabledAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(OutboxDispatchResult.Failed,
            await lab.DispatchAsync("cut-worker", TestContext.Current.CancellationToken));
        Assert.False(await lab.IsPublishedAsync());
        Assert.Null(await lab.TryReadConsumerStateAsync());

        await lab.SetProxyEnabledAsync(true, TestContext.Current.CancellationToken);
        lab.AdvanceRetryTime();
        Assert.Equal(OutboxDispatchResult.Published,
            await lab.DispatchAsync("recovery-worker", TestContext.Current.CancellationToken));

        ConsumerEventState state = await lab.ReadConsumerStateAsync();
        Assert.Equal(1, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
        Assert.Equal(2, await lab.ReadAttemptCountAsync());
        Assert.True(await lab.IsPublishedAsync());
    }

    [Fact]
    public async Task DownstreamLatencyBeyondPublisherTimeout_RedeliveryRetainsStableIdentity()
    {
        await using NetworkLab lab = await NetworkLab.StartAsync(fixture,
            Guid.Parse("40000000-0000-0000-0000-000000000003"));
        await lab.AddDownstreamLatencyAsync(2_000, TestContext.Current.CancellationToken);

        Assert.Equal(OutboxDispatchResult.Failed,
            await lab.DispatchAsync("timeout-worker", TestContext.Current.CancellationToken));
        Assert.False(await lab.IsPublishedAsync());
        Guid eventId = lab.EventId;

        await lab.RemoveToxicAsync("response-latency", TestContext.Current.CancellationToken);
        lab.AdvanceRetryTime();
        Assert.Equal(OutboxDispatchResult.Published,
            await lab.DispatchAsync("retry-worker", TestContext.Current.CancellationToken));

        ConsumerEventState state = await lab.ReadConsumerStateAsync();
        Assert.Equal(eventId, state.EventId);
        Assert.Equal(2, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
        Assert.Equal(2, await lab.ReadAttemptCountAsync());
        Assert.True(await lab.IsPublishedAsync());
    }

    [Fact]
    public async Task ResponsePathResetAfterConsumerCommit_RedeliveryKeepsLogicalEffectOne()
    {
        await using NetworkLab lab = await NetworkLab.StartAsync(fixture,
            Guid.Parse("40000000-0000-0000-0000-000000000004"));
        await lab.AddResponseResetAsync(TestContext.Current.CancellationToken);

        Task<OutboxDispatchResult> firstAttempt = lab.DispatchAsync("reset-worker",
            TestContext.Current.CancellationToken);
        await lab.WaitUntilConsumerReceivedAsync();
        lab.ReleaseConsumerResponse();
        Assert.Equal(OutboxDispatchResult.Failed, await firstAttempt);
        Assert.False(await lab.IsPublishedAsync());

        await lab.RemoveToxicAsync("response-reset", TestContext.Current.CancellationToken);
        lab.AdvanceRetryTime();
        Assert.Equal(OutboxDispatchResult.Published,
            await lab.DispatchAsync("reset-retry-worker", TestContext.Current.CancellationToken));

        ConsumerEventState state = await lab.ReadConsumerStateAsync();
        Assert.Equal(2, state.ReceiptCount);
        Assert.Equal(1, state.LogicalEffectCount);
        Assert.Equal(2, await lab.ReadAttemptCountAsync());
        Assert.True(await lab.IsPublishedAsync());
    }

    private sealed class NetworkLab : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly WebApplication consumerApplication;
        private readonly NpgsqlDataSource consumerDataSource;
        private readonly IContainer toxiproxy;
        private readonly HttpClient toxiproxyAdmin;
        private readonly MutableTimeProvider time;
        private readonly ConsumerResponseGate responseGate;
        private readonly Uri proxyEndpoint;

        private NetworkLab(string connectionString, WebApplication consumerApplication,
            NpgsqlDataSource consumerDataSource, IContainer toxiproxy, HttpClient toxiproxyAdmin,
            MutableTimeProvider time, ConsumerResponseGate responseGate, Uri proxyEndpoint, Guid eventId)
        {
            this.connectionString = connectionString;
            this.consumerApplication = consumerApplication;
            this.consumerDataSource = consumerDataSource;
            this.toxiproxy = toxiproxy;
            this.toxiproxyAdmin = toxiproxyAdmin;
            this.time = time;
            this.responseGate = responseGate;
            this.proxyEndpoint = proxyEndpoint;
            EventId = eventId;
        }

        public Guid EventId { get; }

        public static async Task<NetworkLab> StartAsync(PostgresFixture fixture, Guid transferId)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string connectionString = await CreateDatabaseAsync(fixture, cancellationToken);
            await CreateCompletedTransferAsync(connectionString, transferId, cancellationToken);
            Guid eventId = await ReadEventIdAsync(connectionString, cancellationToken);
            var time = new MutableTimeProvider(Timestamp);
            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            var responseGate = new ConsumerResponseGate();
            WebApplication application = BuildConsumerApplication(dataSource, time, responseGate);
            await application.StartAsync(cancellationToken);
            int consumerPort = ReadBoundPort(application);
            IContainer? toxiproxy = null;
            HttpClient? admin = null;

            try
            {
                await TestcontainersSettings.ExposeHostPortsAsync((ushort)consumerPort, cancellationToken);
                toxiproxy = new ContainerBuilder(ToxiproxyImage)
                    .WithCommand("-host=0.0.0.0")
                    .WithPortBinding(8474, true)
                    .WithPortBinding(8666, true)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                        request.ForPort(8474).ForPath("/version")))
                    .Build();
                await toxiproxy.StartAsync(cancellationToken);
                admin = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{toxiproxy.GetMappedPublicPort(8474)}"),
                    Timeout = TimeSpan.FromSeconds(5)
                };
                using HttpResponseMessage created = await admin.PostAsJsonAsync("/proxies", new
                {
                    name = "simulated-consumer",
                    listen = "0.0.0.0:8666",
                    upstream = $"host.testcontainers.internal:{consumerPort}",
                    enabled = true
                }, cancellationToken);
                created.EnsureSuccessStatusCode();
                var endpoint = new Uri($"http://127.0.0.1:{toxiproxy.GetMappedPublicPort(8666)}/integration-events");
                return new NetworkLab(connectionString, application, dataSource, toxiproxy, admin, time,
                    responseGate, endpoint, eventId);
            }
            catch
            {
                admin?.Dispose();
                if (toxiproxy is not null)
                {
                    await toxiproxy.DisposeAsync();
                }
                await application.DisposeAsync();
                await dataSource.DisposeAsync();
                throw;
            }
        }

        public async Task<OutboxDispatchResult> DispatchAsync(string workerId,
            CancellationToken cancellationToken)
        {
            await using FaultLedgerDbContext context = OpenContext(connectionString);
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var publisher = new NetworkPublisher(client, proxyEndpoint);
            var dispatcher = new OutboxDispatcher(new PostgresOutboxStore(context), publisher,
                new NoOpOutboxDispatchHook(), time);
            return await dispatcher.DispatchNextAsync(workerId, cancellationToken);
        }

        public void AdvanceRetryTime() => time.Advance(OutboxDispatcher.RetryDelay);

        public async Task SetProxyEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "/proxies/simulated-consumer")
            {
                Content = JsonContent.Create(new
                {
                    name = "simulated-consumer",
                    listen = "0.0.0.0:8666",
                    upstream = $"host.testcontainers.internal:{ReadBoundPort(consumerApplication)}",
                    enabled
                })
            };
            using HttpResponseMessage response = await toxiproxyAdmin.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task AddDownstreamLatencyAsync(int latencyMilliseconds,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await toxiproxyAdmin.PostAsJsonAsync(
                "/proxies/simulated-consumer/toxics", new
                {
                    name = "response-latency",
                    type = "latency",
                    stream = "downstream",
                    toxicity = 1.0,
                    attributes = new { latency = latencyMilliseconds, jitter = 0 }
                }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task RemoveToxicAsync(string name, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await toxiproxyAdmin.DeleteAsync(
                $"/proxies/simulated-consumer/toxics/{name}", cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task AddResponseResetAsync(CancellationToken cancellationToken)
        {
            responseGate.Arm();
            using HttpResponseMessage response = await toxiproxyAdmin.PostAsJsonAsync(
                "/proxies/simulated-consumer/toxics", new
                {
                    name = "response-reset",
                    type = "reset_peer",
                    stream = "downstream",
                    toxicity = 1.0,
                    attributes = new { timeout = 0 }
                }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public Task WaitUntilConsumerReceivedAsync() =>
            responseGate.Received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        public void ReleaseConsumerResponse() => responseGate.Release();

        public async Task<ConsumerEventState> ReadConsumerStateAsync() =>
            await TryReadConsumerStateAsync() ?? throw new InvalidOperationException("The consumer did not persist the event.");

        public async Task<ConsumerEventState?> TryReadConsumerStateAsync()
        {
            var store = new SimulatedConsumerStore(consumerDataSource, time);
            return await store.FindAsync(EventId, TestContext.Current.CancellationToken);
        }

        public async Task<int> ReadAttemptCountAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT attempt_count FROM outbox_messages WHERE id = $1", connection);
            command.Parameters.AddWithValue(EventId);
            return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        public async Task<bool> IsPublishedAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT published_at IS NOT NULL FROM outbox_messages WHERE id = $1", connection);
            command.Parameters.AddWithValue(EventId);
            return Assert.IsType<bool>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        public async ValueTask DisposeAsync()
        {
            toxiproxyAdmin.Dispose();
            await toxiproxy.DisposeAsync();
            await consumerApplication.DisposeAsync();
            await consumerDataSource.DisposeAsync();
        }

        private static WebApplication BuildConsumerApplication(NpgsqlDataSource dataSource,
            TimeProvider timeProvider, ConsumerResponseGate responseGate)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(options => options.ListenAnyIP(0));
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddSingleton(timeProvider);
            builder.Services.AddSingleton(responseGate);
            builder.Services.AddScoped<SimulatedConsumerStore>();
            WebApplication application = builder.Build();
            application.MapPost("/integration-events", static async (IntegrationEventRequest request,
                SimulatedConsumerStore store, ConsumerResponseGate responseGate,
                CancellationToken cancellationToken) =>
            {
                string? error = request.Validate();
                if (error is not null)
                {
                    return Results.BadRequest(new { error });
                }

                ConsumerEventState? state = await store.ReceiveAsync(request, cancellationToken);
                if (state is not null)
                {
                    responseGate.MarkReceived();
                    await responseGate.WaitAsync(cancellationToken);
                }

                return state is null
                    ? Results.Conflict(new { error = "The event ID belongs to different immutable content." })
                    : Results.Ok(state);
            });
            return application;
        }

        private static int ReadBoundPort(WebApplication application)
        {
            IServer server = application.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>() ??
                throw new InvalidOperationException("Kestrel did not expose its bound address.");
            return new Uri(Assert.Single(addresses.Addresses)).Port;
        }

        private static async Task<string> CreateDatabaseAsync(PostgresFixture fixture,
            CancellationToken cancellationToken)
        {
            string name = $"stage6_network_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using (var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            string connectionString = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
            {
                Database = name
            }.ConnectionString;
            await using FaultLedgerDbContext context = OpenContext(connectionString);
            await context.Database.MigrateAsync(cancellationToken);
            return connectionString;
        }

        private static async Task CreateCompletedTransferAsync(string connectionString, Guid transferId,
            CancellationToken cancellationToken)
        {
            await using FaultLedgerDbContext context = OpenContext(connectionString);
            var store = new PostgresTransferStore(context, NullLogger<PostgresTransferStore>.Instance);
            Transfer transfer = Transfer.Create(transferId, $"network-{transferId:N}", $"key-{transferId:N}",
                new Money(10, "USD"), Timestamp);
            transfer.MarkReadyToSubmit(Timestamp);
            await store.CreateOrGetAsync(transfer, TransferRequestFingerprint.Compute(transfer.ClientReference,
                10, "USD"), TransferRequestFingerprint.CurrentVersion, cancellationToken);
            transfer.BeginSubmission(Timestamp.AddMinutes(1));
            await store.TryClaimSubmissionAsync(transfer, 1, cancellationToken);
            transfer.MarkAccepted($"provider-{transferId:N}", Timestamp.AddMinutes(2));
            await store.UpdateAsync(transfer, 2, cancellationToken);
            transfer.MarkCompleted("synthetic-network-completed", Timestamp.AddMinutes(3));
            await store.UpdateAsync(transfer, 3, cancellationToken);
        }

        private static async Task<Guid> ReadEventIdAsync(string connectionString,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT id FROM outbox_messages", connection);
            return Assert.IsType<Guid>(await command.ExecuteScalarAsync(cancellationToken));
        }

        private static FaultLedgerDbContext OpenContext(string connectionString) => new(
            new DbContextOptionsBuilder<FaultLedgerDbContext>().UseNpgsql(connectionString).Options);
    }

    private sealed class NetworkPublisher(HttpClient client, Uri endpoint) : IIntegrationEventPublisher
    {
        public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
        {
            using JsonDocument payload = JsonDocument.Parse(message.Payload);
            using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, new
            {
                message.EventId,
                message.EventType,
                message.SchemaVersion,
                message.AggregateId,
                message.AggregateVersion,
                message.OccurredAt,
                payload = payload.RootElement
            }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new IntegrationEventDeliveryException($"The consumer returned HTTP {(int)response.StatusCode}.");
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan elapsed) => current = current.Add(elapsed);
    }

    private sealed class ConsumerResponseGate
    {
        private TaskCompletionSource? release;

        public TaskCompletionSource Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm() => release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkReceived() => Received.TrySetResult();

        public Task WaitAsync(CancellationToken cancellationToken) =>
            Volatile.Read(ref release)?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;

        public void Release() => Volatile.Read(ref release)?.TrySetResult();
    }
}
