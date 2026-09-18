using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FaultLedger.Application.Diagnostics;
using FaultLedger.Application.Transfers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FaultLedger.Api.Callbacks;

public sealed class ProviderCallbackAuthenticator(IConfiguration configuration, IHostEnvironment environment)
{
    private const string DefaultTestingSecret = "synthetic-callback-secret";
    private readonly string? secret = configuration["FaultLedger:Callbacks:Secret"] ??
        (environment.IsEnvironment("Testing") ? DefaultTestingSecret : null);

    public bool IsValid(ReadOnlySpan<byte> body, string? signature)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        string expectedHex = Convert.ToHexString(expected).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHex), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}

public static class ProviderCallbackEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IEndpointRouteBuilder MapProviderCallbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/provider-callbacks", ReceiveAsync);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(HttpRequest request, ProviderCallbackService service,
        ProviderCallbackAuthenticator authenticator, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using Activity? activity = FaultLedgerTelemetry.ActivitySource.StartActivity("faultledger.callback.receive");
        if (request.ContentLength is > 2048)
        {
            throw new ProviderCallbackValidationException("The callback body exceeds the bounded callback envelope size.");
        }

        using var body = new MemoryStream(capacity: 2048);
        byte[] buffer = new byte[1024];
        int total = 0;
        while (true)
        {
            int read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > 2048)
            {
                throw new ProviderCallbackValidationException("The callback body exceeds the bounded callback envelope size.");
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        byte[] bytes = body.ToArray();
        if (!authenticator.IsValid(bytes, request.Headers["X-FaultLedger-Signature"].FirstOrDefault()))
        {
            throw new CallbackAuthenticationException();
        }

        ProviderCallbackRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ProviderCallbackRequest>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new ProviderCallbackValidationException("The callback envelope is not valid JSON.");
        }

        if (payload is null || !Enum.TryParse(payload.EventType, ignoreCase: true, out ProviderCallbackEventType eventType) ||
            !Enum.IsDefined(eventType))
        {
            throw new ProviderCallbackValidationException("The callback event type is unsupported.");
        }

        DateTimeOffset occurredAt = payload.OccurredAt ?? timeProvider.GetUtcNow();
        var callback = new ProviderCallbackEnvelope(payload.EventId ?? string.Empty, payload.TransferId, eventType,
            payload.ProviderReference, payload.Evidence ?? eventType.ToString(), occurredAt, payload.PayloadVersion);
        string normalizedPayload = JsonSerializer.Serialize(payload, SerializerOptions);
        InboxReceipt receipt = await service.ReceiveAsync(callback, normalizedPayload, cancellationToken);
        activity?.SetTag("faultledger.inbox.id", receipt.InboxId);
        if (receipt.Outcome == InboxReceiptOutcome.Duplicate)
        {
            FaultLedgerTelemetry.CallbackDuplicates.Add(1);
        }

        var response = new ProviderCallbackReceiptResponse(receipt.InboxId, receipt.Outcome.ToString(),
            "Callback durably received; business processing is recoverable from the inbox.");
        return receipt.Outcome == InboxReceiptOutcome.Duplicate
            ? TypedResults.Ok(response)
            : Results.Accepted(value: response);
    }
}

public sealed record ProviderCallbackRequest(
    string? EventId,
    Guid TransferId,
    string? EventType,
    string? ProviderReference,
    string? Evidence,
    DateTimeOffset? OccurredAt,
    int PayloadVersion = 1);

public sealed record ProviderCallbackReceiptResponse(Guid InboxId, string Outcome, string Message);
