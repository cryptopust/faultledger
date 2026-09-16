using FaultLedger.Application.Transfers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FaultLedger.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        (int status, string title, string detail) = exception switch
        {
            TransferValidationException => (400, "Invalid transfer request", exception.Message),
            CallbackAuthenticationException => (401, "Invalid callback authentication", "The callback signature is invalid."),
            ProviderCallbackValidationException => (400, "Invalid provider callback", exception.Message),
            ProviderEventConflictException => (409, "Provider event conflict", exception.Message),
            ProviderCallbackConflictException => (409, "Provider callback conflict", exception.Message),
            BadHttpRequestException => (400, "Invalid request body", "Use the documented JSON contract."),
            IdempotencyConflictException => (409, "Idempotency conflict", exception.Message),
            TransferConcurrencyException => (409, "Transfer concurrency conflict", exception.Message),
            ReconciliationNotEligibleException => (409, "Transfer is not eligible for reconciliation", exception.Message),
            TransferNotFoundException => (404, "Transfer not found", exception.Message),
            ReconciliationUnavailableException => (503, "Provider reconciliation unavailable", exception.Message),
            TransferStorageUnavailableException => (503, "Transfer persistence unavailable", exception.Message),
            ProviderSubmissionException providerException => (502, "Provider submission did not complete",
                providerException.Result.SafeMessage),
            _ => (500, "Unexpected request failure", "The operation may already be durable or submitted. Do not automatically resubmit.")
        };
        logger.LogWarning("API request rejected with {StatusCode} ({FailureType}).", status, exception.GetType().Name);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        }, cancellationToken);
        return true;
    }
}
