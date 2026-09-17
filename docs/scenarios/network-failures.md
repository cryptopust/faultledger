# Network-failure scenarios

The Stage 6 topology is:

```text
FaultLedger dispatcher -> Toxiproxy -> FaultLedger.SimulatedConsumer
```

The MockProvider remains a semantic failure laboratory. Toxiproxy is separate
test infrastructure for actual TCP/HTTP path faults. It is configured through
Compose and does not replace deterministic provider acceptance boundaries.

The publisher uses a bounded `HttpClient` timeout and records a retryable
outbox failure when the consumer cannot be reached. A timeout or broken
response path does **not** prove that the consumer did not receive the event.
The consumer therefore records the stable event ID and applies its durable
logical effect only once.

The Docker-required scenarios are:

* normal delivery through the proxy;
* latency beyond the publisher timeout;
* an unavailable proxy connection followed by recovery; and
* a response path reset after the consumer has received the request.

The last case is intentionally equivalent to crash-after-publish: the
publisher retries the same event ID and the consumer reports multiple receipts
but one logical effect. No scenario claims exactly-once distributed delivery,
global ordering, or that a transport error authorizes a new provider
submission.
