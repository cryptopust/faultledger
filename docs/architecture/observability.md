# Focused observability

FaultLedger uses BCL `ActivitySource` and `Meter` in Application, then registers
OpenTelemetry ASP.NET Core and HttpClient instrumentation in the API composition
root. Domain remains dependency-free. No exporter is bundled, and telemetry is
not durable correctness evidence.

## Spans

The custom spans are `faultledger.transfer.create`,
`faultledger.provider.submit`, `faultledger.transfer.reconcile`,
`faultledger.callback.receive`, `faultledger.callback.process`, and
`faultledger.outbox.dispatch`. Safe IDs and categorical outcomes may be trace
attributes. Callback secrets, authorization headers, connection strings, raw
payloads, and monetary values are excluded.

## Metrics and operational questions

| Metric | Operational question |
| --- | --- |
| `faultledger_transfers_created_total` | Are durable transfer registrations arriving at the expected rate? |
| `faultledger_provider_submissions_total` | How many provider POST boundaries were actually invoked? |
| `faultledger_unknown_outcomes_total` | Are ambiguous external outcomes increasing? |
| `faultledger_reconciliations_total` | How often is lookup-only recovery being requested? |
| `faultledger_idempotency_replays_total` | How much client/network retry traffic is being recovered safely? |
| `faultledger_idempotency_conflicts_total` | Are callers reusing keys for different immutable requests? |
| `faultledger_callback_duplicates_total` | Is callback redelivery/replay increasing? |
| `faultledger_outbox_redeliveries_total` | Are delivery acknowledgement loss or retry conditions increasing? |

Pending inbox/outbox gauges are deliberately omitted. A process-local count
would be stale and could be mistaken for authority; an operator needing backlog
depth should query PostgreSQL or add a database-observed instrument with an
explicit cost budget. High-cardinality transfer/event IDs are trace attributes,
not metric labels.
