using System.Net.Http.Json;
using System.Text.Json;
using FaultLedger.Application.IntegrationEvents;
using Microsoft.Extensions.Options;

namespace FaultLedger.Infrastructure.IntegrationEvents;

public sealed class IntegrationEventPublisherOptions
{
    public string Endpoint { get; set; } = string.Empty;
}

public sealed class HttpIntegrationEventPublisher(
    IHttpClientFactory httpClientFactory,
    IOptions<IntegrationEventPublisherOptions> options) : IIntegrationEventPublisher
{
    public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Value.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new IntegrationEventDeliveryException("The simulated consumer endpoint is not configured.");
        }

        using HttpClient client = httpClientFactory.CreateClient(nameof(HttpIntegrationEventPublisher));
        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        var envelope = new OutboundIntegrationEventEnvelope(message.EventId, message.EventType,
            message.SchemaVersion, message.AggregateId, message.AggregateVersion, message.OccurredAt,
            payload.RootElement);
        using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, envelope, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new IntegrationEventDeliveryException(
                $"The simulated consumer returned HTTP {(int)response.StatusCode}.");
        }
    }
}

internal sealed record OutboundIntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    Guid AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAt,
    JsonElement Payload);
