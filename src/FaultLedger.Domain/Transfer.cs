namespace FaultLedger.Domain;

public sealed class Transfer
{
    public const int ReferenceMaximumLength = 100;
    public const int IdempotencyKeyMaximumLength = 128;

    private Transfer(Guid id, string clientReference, string idempotencyKey, Money money, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Transfer identity cannot be empty.", nameof(id));
        }

        ValidateReference(clientReference, ReferenceMaximumLength, nameof(clientReference));
        ValidateReference(idempotencyKey, IdempotencyKeyMaximumLength, nameof(idempotencyKey));
        ArgumentNullException.ThrowIfNull(money);
        if (money.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(money), "Transfer amount must be positive.");
        }

        Id = id;
        ClientReference = clientReference;
        IdempotencyKey = idempotencyKey;
        Money = money;
        CreatedAt = CanonicalTime(createdAt);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }
    public string ClientReference { get; }
    public string IdempotencyKey { get; }
    public Money Money { get; }
    public TransferState State { get; private set; } = TransferState.Created;
    public string? ProviderReference { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public bool IsTerminal => State is TransferState.Completed or TransferState.Failed;

    public static Transfer Create(Guid id, string clientReference, string idempotencyKey, Money money,
        DateTimeOffset createdAt) => new(id, clientReference, idempotencyKey, money, createdAt);

    public static Transfer Restore(Guid id, string clientReference, string idempotencyKey, Money money,
        TransferState state, string? providerReference, DateTimeOffset createdAt, DateTimeOffset updatedAt, long version)
    {
        long requiredVersion = state switch
        {
            TransferState.Created => 0,
            TransferState.ReadyToSubmit => 1,
            TransferState.Submitting => 2,
            TransferState.Accepted or TransferState.Failed or TransferState.Unknown => 3,
            TransferState.Completed or TransferState.ManualReview => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        if (version != requiredVersion || updatedAt < createdAt ||
            createdAt != CanonicalTime(createdAt) || updatedAt != CanonicalTime(updatedAt))
        {
            throw new ArgumentException("Stored transfer version or timestamps violate its state invariants.");
        }

        if (state is TransferState.Accepted or TransferState.Completed)
        {
            ValidateReference(providerReference, ReferenceMaximumLength, nameof(providerReference));
        }
        else if (providerReference is not null)
        {
            throw new ArgumentException("This state cannot have an accepted provider reference.", nameof(providerReference));
        }

        var transfer = new Transfer(id, clientReference, idempotencyKey, money, createdAt)
        {
            State = state,
            ProviderReference = providerReference,
            UpdatedAt = updatedAt.ToUniversalTime(),
            Version = version
        };
        return transfer;
    }

    public void MarkReadyToSubmit(DateTimeOffset timestamp) =>
        Transition(TransferState.Created, TransferState.ReadyToSubmit, timestamp);

    public void BeginSubmission(DateTimeOffset timestamp) =>
        Transition(TransferState.ReadyToSubmit, TransferState.Submitting, timestamp);

    public void MarkAccepted(string providerReference, DateTimeOffset timestamp)
    {
        ValidateReference(providerReference, ReferenceMaximumLength, nameof(providerReference));
        Transition(TransferState.Submitting, TransferState.Accepted, timestamp);
        ProviderReference = providerReference;
    }

    public void MarkFailed(string rejectionEvidence, DateTimeOffset timestamp)
    {
        RequireEvidence(rejectionEvidence);
        Transition(TransferState.Submitting, TransferState.Failed, timestamp);
    }

    public void MarkUnknown(string uncertaintyReason, DateTimeOffset timestamp)
    {
        RequireEvidence(uncertaintyReason);
        Transition(TransferState.Submitting, TransferState.Unknown, timestamp);
    }

    public void MarkCompleted(string completionEvidence, DateTimeOffset timestamp)
    {
        RequireEvidence(completionEvidence);
        Transition(TransferState.Accepted, TransferState.Completed, timestamp);
    }

    public void SendToManualReview(string reason, DateTimeOffset timestamp)
    {
        RequireEvidence(reason);
        Transition(TransferState.Unknown, TransferState.ManualReview, timestamp);
    }

    private void Transition(TransferState expected, TransferState target, DateTimeOffset timestamp)
    {
        if (State != expected)
        {
            throw new InvalidTransferTransitionException(State, target);
        }

        DateTimeOffset canonical = CanonicalTime(timestamp);
        if (canonical < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Transition time cannot move backwards.");
        }

        long nextVersion = checked(Version + 1);
        State = target;
        UpdatedAt = canonical;
        Version = nextVersion;
    }

    private static DateTimeOffset CanonicalTime(DateTimeOffset timestamp)
    {
        long ticks = timestamp.UtcTicks;
        if (ticks < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Timestamp must not map to the database's infinity sentinel.");
        }

        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }

    private static void RequireEvidence(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw new ArgumentException("Explicit transition evidence is required.", nameof(evidence));
        }
    }

    private static void ValidateReference(string? value, int maximumLength, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
        {
            throw new ArgumentException($"Reference must contain 1-{maximumLength} ASCII letters, digits, '-', '_', '.', or ':'.", name);
        }
    }
}
