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

    private static async Task<Created<TransferResponse>> CreateAsync(CreateTransferRequest request,
        TransferService service, CancellationToken cancellationToken)
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

        TransferDetails transfer = await service.CreateAsync(new CreateTransferCommand(request.ClientReference,
            request.IdempotencyKey, amount, request.Currency), cancellationToken);
        return TypedResults.Created($"/api/transfers/{transfer.Id:D}", TransferResponse.FromDetails(transfer));
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

public sealed record CreateTransferRequest(string ClientReference, string IdempotencyKey, JsonElement Amount, string Currency);

public sealed record TransferResponse(Guid Id, string ClientReference, string IdempotencyKey, decimal Amount,
    string Currency, string State, string? ProviderReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version)
{
    public static TransferResponse FromDetails(TransferDetails details) => new(details.Id, details.ClientReference,
        details.IdempotencyKey, details.Amount, details.Currency, details.State, details.ProviderReference,
        details.CreatedAt, details.UpdatedAt, details.Version);
}
