namespace FaultLedger.Application.Transfers;

public sealed class ProviderCallbackService(IProviderInboxStore inboxStore)
{
    public async Task<InboxReceipt> ReceiveAsync(ProviderCallbackEnvelope callback, string normalizedPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try
        {
            callback.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ProviderCallbackValidationException(exception.Message);
        }

        if (string.IsNullOrWhiteSpace(normalizedPayload) || normalizedPayload.Length > 2048)
        {
            throw new ProviderCallbackValidationException("The normalized callback payload is invalid or too large.");
        }

        return await inboxStore.ReceiveAsync(callback, normalizedPayload, cancellationToken);
    }

    public Task<CallbackProcessingDetails?> ProcessNextAsync(CancellationToken cancellationToken) =>
        inboxStore.ProcessAsync(cancellationToken);
}
