using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FaultLedger.Domain;

namespace FaultLedger.Application.Transfers;

public sealed record CreateTransferCommand(string ClientReference, string IdempotencyKey, decimal Amount, string Currency);

public static class TransferRequestFingerprint
{
    public const int CurrentVersion = 1;

    public static string Compute(CreateTransferCommand command) =>
        Compute(command.ClientReference, command.Amount, command.Currency);

    public static string Compute(string clientReference, decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(clientReference);
        ArgumentNullException.ThrowIfNull(currency);
        string canonicalAmount = amount.ToString("0.###########################", CultureInfo.InvariantCulture);
        string canonical = $"v{CurrentVersion}\nclient-reference={LengthPrefix(clientReference)}\namount={canonicalAmount}\ncurrency={currency.ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string LengthPrefix(string value) => $"{value.Length}:{value}";
}

public sealed record TransferDetails(Guid Id, string ClientReference, string IdempotencyKey, decimal Amount,
    string Currency, string State, string? ProviderReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version)
{
    public static TransferDetails FromTransfer(Transfer transfer) => new(transfer.Id, transfer.ClientReference,
        transfer.IdempotencyKey, transfer.Money.Amount, transfer.Money.Currency, transfer.State.ToString(),
        transfer.ProviderReference, transfer.CreatedAt, transfer.UpdatedAt, transfer.Version);
}

public interface ITransferStore
{
    Task AddAsync(Transfer transfer, CancellationToken cancellationToken);
    Task UpdateAsync(Transfer transfer, long expectedVersion, CancellationToken cancellationToken);
    Task<Transfer?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<TransferRegistration> CreateOrGetAsync(Transfer transfer, string requestFingerprint, int fingerprintVersion,
        CancellationToken cancellationToken);
    Task<SubmissionClaimResult> TryClaimSubmissionAsync(Transfer transfer, long expectedVersion,
        CancellationToken cancellationToken);
}

public sealed record TransferRegistration(Transfer Transfer, string? RequestFingerprint, int? FingerprintVersion, bool Created);

public enum SubmissionClaimResult
{
    ClaimAcquired,
    AlreadyClaimedOrSubmitted,
    InvalidState,
    NotFound
}

public class IdempotencyConflictException() : InvalidOperationException("The idempotency key belongs to a different canonical request.");

public sealed class DuplicateTransferKeyException() : IdempotencyConflictException();

public sealed class TransferConcurrencyException() : InvalidOperationException("The durable transfer changed concurrently. No retry was performed.");

public sealed class TransferValidationException(string message) : ArgumentException(message);

public sealed class TransferStorageUnavailableException() : InvalidOperationException("Transfer persistence is unavailable. An earlier commit or submission may exist; do not resubmit with a new key.");
