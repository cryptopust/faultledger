using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FaultLedger.Application.Transfers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FaultLedger.IntegrationTests;

public sealed class ProviderCallbackContractTests
{
    private const string Secret = "synthetic-callback-secret";
    private static readonly Guid TransferId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task InvalidSignature_MissingModifiedAndWrongValues_CreateNoInboxRows()
    {
        var store = new RecordingInboxStore();
        await using var factory = CreateFactory(store);
        using HttpClient client = factory.CreateClient();
        byte[] body = Body("evt-auth-1");

        foreach (string? signature in new[] { null, "00", Sign(body) + "0" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/provider-callbacks")
            {
                Content = new ByteArrayContent(body)
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            if (signature is not null)
            {
                request.Headers.Add("X-FaultLedger-Signature", signature);
            }

            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        byte[] modified = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("provider-1", "provider-2",
            StringComparison.Ordinal));
        using var modifiedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/provider-callbacks")
        {
            Content = new ByteArrayContent(modified)
        };
        modifiedRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        modifiedRequest.Headers.Add("X-FaultLedger-Signature", Sign(body));
        using HttpResponseMessage modifiedResponse = await client.SendAsync(modifiedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, modifiedResponse.StatusCode);
        Assert.Equal(0, store.ReceiptCount);
    }

    [Fact]
    public async Task ValidCallback_IsAcknowledgedAfterReceiptAndDuplicateIsIdempotent()
    {
        var store = new RecordingInboxStore();
        await using var factory = CreateFactory(store);
        using HttpClient client = factory.CreateClient();
        byte[] body = Body("evt-valid-1");

        using HttpRequestMessage first = CreateRequest(body);
        using HttpResponseMessage firstResponse = await client.SendAsync(first, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        for (int i = 0; i < 10; i++)
        {
            using HttpRequestMessage duplicate = CreateRequest(body);
            using HttpResponseMessage duplicateResponse = await client.SendAsync(duplicate,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        }
        Assert.Equal(1, store.ReceiptCount);
        Assert.Equal(1, store.DurableInsertCount);
    }

    [Fact]
    public async Task InvalidEnvelope_IsRejectedAfterAuthenticationWithoutReceipt()
    {
        var store = new RecordingInboxStore();
        await using var factory = CreateFactory(store);
        using HttpClient client = factory.CreateClient();
        byte[] body = Encoding.UTF8.GetBytes("{\"eventId\":\"evt-invalid\",\"transferId\":\"00000000-0000-0000-0000-000000000000\",\"eventType\":\"COMPLETED\"}");
        using HttpRequestMessage request = CreateRequest(body);

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.ReceiptCount);
    }

    private static FaultLedgerApiFactory CreateFactory(RecordingInboxStore store) =>
        new FaultLedgerApiFactory("Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Password=synthetic_test_only")
            .WithServices(services =>
            {
                services.RemoveAll<IProviderInboxStore>();
                services.AddSingleton<IProviderInboxStore>(store);
            });

    private static HttpRequestMessage CreateRequest(byte[] body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/provider-callbacks")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-FaultLedger-Signature", Sign(body));
        return request;
    }

    private static byte[] Body(string eventId) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        eventId,
        transferId = TransferId,
        eventType = "COMPLETED",
        providerReference = "provider-1",
        evidence = "provider-completed",
        occurredAt = "2026-09-17T00:00:00Z",
        payloadVersion = 1
    }));

    private static string Sign(byte[] body) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(Secret), body)).ToLowerInvariant();

    private sealed class RecordingInboxStore : IProviderInboxStore
    {
        private readonly Dictionary<string, Guid> events = new(StringComparer.Ordinal);
        public int ReceiptCount => events.Count;
        public int DurableInsertCount { get; private set; }

        public Task<InboxReceipt> ReceiveAsync(ProviderCallbackEnvelope callback, string normalizedPayload,
            CancellationToken cancellationToken)
        {
            if (events.TryGetValue(callback.ProviderEventId, out Guid existing))
            {
                return Task.FromResult(new InboxReceipt(InboxReceiptOutcome.Duplicate, existing));
            }

            Guid inboxId = Guid.NewGuid();
            events.Add(callback.ProviderEventId, inboxId);
            DurableInsertCount++;
            return Task.FromResult(new InboxReceipt(InboxReceiptOutcome.Received, inboxId));
        }

        public Task<CallbackProcessingDetails?> ProcessAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CallbackProcessingDetails?>(null);

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(events.Count);
    }
}
