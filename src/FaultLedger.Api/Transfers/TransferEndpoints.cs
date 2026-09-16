using System.Text.Json;
using FaultLedger.Application.Transfers;
using FaultLedger.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FaultLedger.Api.Transfers;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/transfers", CreateAsync);
        endpoints.MapGet("/api/transfers/{id:guid}", FindAsync);
        return endpoints;
    }

    private static async Task<Results<Created<TransferResponse>, Ok<TransferResponse>>> CreateAsync(
        CreateTransferRequest request, HttpRequest httpRequest, TransferService service,
        CancellationToken cancellationToken)
    {
        decimal amount;
        try
        {
            if (request.Amount.ValueKind != JsonValueKind.Number)
            {
                throw new ArgumentException("Amount must be a JSON fixed-point number.");
            }

            amount = Money.ParseAmount(request.Amount.GetRawText());
        }
        catch (ArgumentException exception)
        {
            throw new TransferValidationException(exception.Message);
        }

        string bodyKey = request.IdempotencyKey ?? string.Empty;
        string? headerKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
        if (headerKey is not null && bodyKey.Length > 0 && !string.Equals(headerKey, bodyKey, StringComparison.Ordinal))
        {
            throw new TransferValidationException("The Idempotency-Key header and request field must agree.");
        }

        string idempotencyKey = headerKey ?? bodyKey;
        TransferCreateOutcome outcome = await service.CreateWithOutcomeAsync(
            new CreateTransferCommand(request.ClientReference, idempotencyKey, amount, request.Currency),
            cancellationToken);
        TransferResponse response = TransferResponse.FromDetails(outcome.Details);
        return outcome.IsReplay
            ? TypedResults.Ok(response)
            : TypedResults.Created($"/api/transfers/{response.Id:D}", response);
    }

    private static async Task<Results<Ok<TransferResponse>, NotFound<ProblemDetails>>> FindAsync(Guid id,
        TransferService service, CancellationToken cancellationToken)
    {
        TransferDetails? transfer = await service.FindAsync(id, cancellationToken);
        return transfer is null
            ? TypedResults.NotFound(new ProblemDetails { Status = 404, Title = "Transfer not found" })
            : TypedResults.Ok(TransferResponse.FromDetails(transfer));
    }
}

public sealed record CreateTransferRequest(string ClientReference, string? IdempotencyKey, JsonElement Amount, string Currency);

public sealed record TransferResponse(Guid Id, string ClientReference, string IdempotencyKey, decimal Amount,
    string Currency, string State, string? ProviderReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version)
{
    public static TransferResponse FromDetails(TransferDetails details) => new(details.Id, details.ClientReference,
        details.IdempotencyKey, details.Amount, details.Currency, details.State, details.ProviderReference,
        details.CreatedAt, details.UpdatedAt, details.Version);
}
