using FaultLedger.Application.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaultLedger.Infrastructure.IntegrationEvents;

public sealed class OutboxDispatcherWorkerOptions
{
    public bool Enabled { get; set; }
    public string WorkerId { get; set; } = "faultledger-dispatcher";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxDispatcherWorkerOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (settings.PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The outbox poll interval must be positive.");
        }

        using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);
        do
        {
            try
            {
                await DispatchDueMessagesAsync(settings.WorkerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed for worker {WorkerId}.",
                    settings.WorkerId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchDueMessagesAsync(string workerId, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            OutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            OutboxDispatchResult result = await dispatcher.DispatchNextAsync(workerId, cancellationToken);
            if (result == OutboxDispatchResult.NoMessage)
            {
                return;
            }
        }
    }
}
