namespace FaultLedger.Domain;

public sealed class InvalidTransferTransitionException(TransferState current, TransferState target)
    : InvalidOperationException($"Transfer cannot transition from {current} to {target}.");
