using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FaultLedger.Application.Diagnostics;

public static class FaultLedgerTelemetry
{
    public const string SourceName = "FaultLedger";
    public const string MeterName = "FaultLedger";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> TransfersCreated = Meter.CreateCounter<long>(
        "faultledger_transfers_created_total", description: "Transfers durably registered.");
    public static readonly Counter<long> ProviderSubmissions = Meter.CreateCounter<long>(
        "faultledger_provider_submissions_total", description: "Provider submission calls dispatched.");
    public static readonly Counter<long> UnknownOutcomes = Meter.CreateCounter<long>(
        "faultledger_unknown_outcomes_total", description: "Provider outcomes retained as Unknown.");
    public static readonly Counter<long> Reconciliations = Meter.CreateCounter<long>(
        "faultledger_reconciliations_total", description: "Provider reconciliation calls started.");
    public static readonly Counter<long> IdempotencyReplays = Meter.CreateCounter<long>(
        "faultledger_idempotency_replays_total", description: "Requests recovered from an existing idempotency key.");
    public static readonly Counter<long> IdempotencyConflicts = Meter.CreateCounter<long>(
        "faultledger_idempotency_conflicts_total", description: "Requests rejected for an idempotency conflict.");
    public static readonly Counter<long> CallbackDuplicates = Meter.CreateCounter<long>(
        "faultledger_callback_duplicates_total", description: "Duplicate provider callback receipts.");
    public static readonly Counter<long> OutboxRedeliveries = Meter.CreateCounter<long>(
        "faultledger_outbox_redeliveries_total", description: "Outbox attempts after an earlier delivery attempt.");
}
