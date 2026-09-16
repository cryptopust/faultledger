using FaultLedger.Domain;

namespace FaultLedger.Domain.Tests;

public sealed class TransferTests
{
    private static readonly Guid Identity = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewTransfer_StartsCreatedWithControlledTimeAndNoProviderEvidence()
    {
        Transfer transfer = NewTransfer();
        Assert.Equal(TransferState.Created, transfer.State);
        Assert.Equal(0, transfer.Version);
        Assert.Equal(Timestamp, transfer.CreatedAt);
        Assert.Equal(Timestamp, transfer.UpdatedAt);
        Assert.Null(transfer.ProviderReference);
    }

    public static IEnumerable<object[]> TransitionCases()
    {
        (string Operation, TransferState Source, TransferState Target)[] operations =
        [
            ("ready", TransferState.Created, TransferState.ReadyToSubmit),
            ("submit", TransferState.ReadyToSubmit, TransferState.Submitting),
            ("accept", TransferState.Submitting, TransferState.Accepted),
            ("fail", TransferState.Submitting, TransferState.Failed),
            ("unknown", TransferState.Submitting, TransferState.Unknown),
            ("resolve-accept", TransferState.Unknown, TransferState.Accepted),
            ("resolve-complete", TransferState.Unknown, TransferState.Completed),
            ("resolve-fail", TransferState.Unknown, TransferState.Failed),
            ("complete", TransferState.Accepted, TransferState.Completed),
            ("review", TransferState.Unknown, TransferState.ManualReview)
        ];
        foreach (TransferState initial in Enum.GetValues<TransferState>())
        {
            foreach (var operation in operations)
            {
                yield return [initial, operation.Operation, initial == operation.Source, operation.Target];
            }
        }
    }

    [Theory]
    [MemberData(nameof(TransitionCases))]
    public void StateTransition_OnlyLegalEvidenceAuthorizedEdgesSucceed(
        TransferState initial, string operation, bool allowed, TransferState expected)
    {
        Transfer transfer = InState(initial);
        long version = transfer.Version;
        string? reference = transfer.ProviderReference;
        if (allowed)
        {
            Apply(transfer, operation, Timestamp.AddMinutes(1));
            Assert.Equal(expected, transfer.State);
            Assert.Equal(version + 1, transfer.Version);
            Assert.Equal(Timestamp.AddMinutes(1), transfer.UpdatedAt);
            Assert.Equal(Timestamp, transfer.CreatedAt);
            if (operation is "accept" or "resolve-accept" or "resolve-complete")
            {
                Assert.Equal("synthetic-reference", transfer.ProviderReference);
                Assert.Equal(operation is "resolve-complete" ? TransferState.Completed : TransferState.Accepted,
                    transfer.State);
                Assert.Equal(operation is "resolve-complete", transfer.IsTerminal);
            }
        }
        else
        {
            Assert.Throws<InvalidTransferTransitionException>(() => Apply(transfer, operation, Timestamp.AddMinutes(1)));
            Assert.Equal(initial, transfer.State);
            Assert.Equal(version, transfer.Version);
            Assert.Equal(Timestamp, transfer.UpdatedAt);
            Assert.Equal(reference, transfer.ProviderReference);
        }
    }

    [Theory]
    [InlineData(TransferState.Completed, true)]
    [InlineData(TransferState.Failed, true)]
    [InlineData(TransferState.Accepted, false)]
    [InlineData(TransferState.Unknown, false)]
    [InlineData(TransferState.ManualReview, false)]
    public void TerminalPolicy_DistinguishesAcceptanceAndUnresolvedWork(TransferState state, bool terminal) =>
        Assert.Equal(terminal, InState(state).IsTerminal);

    [Fact]
    public void TransitionTime_IsUtcMicrosecondCanonicalAndCannotRegress()
    {
        Transfer transfer = NewTransfer();
        transfer.MarkReadyToSubmit(Timestamp.ToOffset(TimeSpan.FromHours(3)).AddTicks(19));
        Assert.Equal(TimeSpan.Zero, transfer.UpdatedAt.Offset);
        Assert.Equal(Timestamp.AddTicks(10), transfer.UpdatedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => transfer.BeginSubmission(Timestamp));
        Assert.Equal(TransferState.ReadyToSubmit, transfer.State);
        Assert.Equal(1, transfer.Version);
    }

    [Fact]
    public void MissingTransitionEvidence_DoesNotMutateState()
    {
        Transfer transfer = InState(TransferState.Submitting);
        Assert.Throws<ArgumentException>(() => transfer.MarkAccepted("", Timestamp));
        Assert.Throws<ArgumentException>(() => transfer.MarkFailed(" ", Timestamp));
        Assert.Throws<ArgumentException>(() => transfer.MarkUnknown("", Timestamp));
        Assert.Equal(TransferState.Submitting, transfer.State);
        Assert.Equal(2, transfer.Version);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveTransfer_IsRejectedEvenThoughMoneySupportsIt(string amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Transfer.Create(Identity, "order-1", "key-1",
            new Money(Money.ParseAmount(amount), "USD"), Timestamp));

    [Theory]
    [InlineData("")]
    [InlineData(" order")]
    [InlineData("order/1")]
    [InlineData("order\n1")]
    [InlineData("Örder")]
    public void InvalidReferences_AreRejected(string reference)
    {
        Assert.Throws<ArgumentException>(() => Transfer.Create(Identity, reference, "key-1", new Money(1, "USD"), Timestamp));
        Assert.Throws<ArgumentException>(() => Transfer.Create(Identity, "order-1", reference, new Money(1, "USD"), Timestamp));
    }

    [Fact]
    public void OversizedReferencesAndEmptyIdentity_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => Transfer.Create(Identity, new string('a', 101), "key", new Money(1, "USD"), Timestamp));
        Assert.Throws<ArgumentException>(() => Transfer.Create(Identity, "order", new string('a', 129), new Money(1, "USD"), Timestamp));
        Assert.Throws<ArgumentException>(() => Transfer.Create(Guid.Empty, "order", "key", new Money(1, "USD"), Timestamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transfer.Create(Identity, "order", "key", new Money(1, "USD"), DateTimeOffset.MinValue));
    }

    [Fact]
    public void Rehydration_RejectsCorruptStateVersionTimeAndProviderEvidence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore((TransferState)999, 0, null, Timestamp));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Accepted, 3, null, Timestamp));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Created, 1, null, Timestamp));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Created, 0, "unexpected", Timestamp));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Created, 0, null, Timestamp.AddTicks(1)));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Created, 0, null, Timestamp.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => Restore(TransferState.Unknown, 4, null, Timestamp));
        Assert.Equal(TransferState.Accepted,
            Restore(TransferState.Accepted, 4, "synthetic-reference", Timestamp).State);
        Assert.Equal(TransferState.Completed,
            Restore(TransferState.Completed, 5, "synthetic-reference", Timestamp).State);
        Assert.Equal(TransferState.Failed, Restore(TransferState.Failed, 4, null, Timestamp).State);
    }

    [Fact]
    public void ReconciledAccepted_CanCompleteWithNextDurableVersion()
    {
        Transfer transfer = Restore(TransferState.Accepted, 4, "synthetic-reference", Timestamp);
        transfer.MarkCompleted("provider-completed", Timestamp);
        Assert.Equal(TransferState.Completed, transfer.State);
        Assert.Equal(5, transfer.Version);
    }

    private static Transfer Restore(TransferState state, long version, string? reference, DateTimeOffset updatedAt) =>
        Transfer.Restore(Identity, "order-1", "key-1", new Money(10.999m, "USD"), state, reference, Timestamp, updatedAt, version);

    private static Transfer NewTransfer() => Transfer.Create(Identity, "order-1", "key-1", new Money(10.999m, "USD"), Timestamp);

    private static Transfer InState(TransferState state)
    {
        Transfer transfer = NewTransfer();
        if (state == TransferState.Created)
        {
            return transfer;
        }

        transfer.MarkReadyToSubmit(Timestamp);
        if (state == TransferState.ReadyToSubmit)
        {
            return transfer;
        }

        transfer.BeginSubmission(Timestamp);
        switch (state)
        {
            case TransferState.Accepted:
            case TransferState.Completed:
                transfer.MarkAccepted("synthetic-reference", Timestamp);
                if (state == TransferState.Completed)
                {
                    transfer.MarkCompleted("confirmed-completed", Timestamp);
                }

                break;
            case TransferState.Failed:
                transfer.MarkFailed("confirmed-rejected", Timestamp);
                break;
            case TransferState.Unknown:
            case TransferState.ManualReview:
                transfer.MarkUnknown("outcome-unconfirmed", Timestamp);
                if (state == TransferState.ManualReview)
                {
                    transfer.SendToManualReview("review-required", Timestamp);
                }

                break;
        }

        return transfer;
    }

    private static void Apply(Transfer transfer, string operation, DateTimeOffset timestamp)
    {
        switch (operation)
        {
            case "ready": transfer.MarkReadyToSubmit(timestamp); break;
            case "submit": transfer.BeginSubmission(timestamp); break;
            case "accept": transfer.MarkAccepted("synthetic-reference", timestamp); break;
            case "fail": transfer.MarkFailed("confirmed-rejected", timestamp); break;
            case "unknown": transfer.MarkUnknown("outcome-unconfirmed", timestamp); break;
            case "resolve-accept": transfer.ResolveAccepted("synthetic-reference", timestamp); break;
            case "resolve-complete": transfer.ResolveCompleted("synthetic-reference", "confirmed-completed", timestamp); break;
            case "resolve-fail": transfer.ResolveFailed("confirmed-rejected", timestamp); break;
            case "complete": transfer.MarkCompleted("confirmed-completed", timestamp); break;
            case "review": transfer.SendToManualReview("review-required", timestamp); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
