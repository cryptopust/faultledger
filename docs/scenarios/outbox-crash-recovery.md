# Outbox crash and restart scenarios

The outbox has four durable phases: pending, claimed, published, and deferred
after a delivery failure. Claims carry a token and expiry. A process restart
does not rely on its memory: a new dispatcher can reclaim a row after the
lease expires.

| Failure boundary | Durable result | Recovery |
| --- | --- | --- |
| Before Transfer/outbox commit | Neither record changes | Retry the local transaction |
| After commit, before claim | Transfer and pending event survive | New dispatcher claims it |
| After claim, before send | Lease eventually expires | Another dispatcher reclaims it |
| Delivery fails | Attempt/error and next-attempt time are stored | Dispatcher retries when due |
| Remote effect, before `published_at` | Consumer may have acted; row remains retryable | Same event ID is redelivered; consumer deduplicates |
| After published marker | Event is terminal locally | No further dispatch |

The central flagship case is deliberately ambiguous: the consumer receives and
applies the event, then the publisher fails before marking `published_at`.
The second delivery is expected. Evidence distinguishes delivery attempts and
consumer receipts from logical effects:

```text
delivery attempts >= 2
consumer receipts >= 2
consumer logical effects = 1
outbox published = true after recovery
```

The test hook `AfterRemotePublishBeforeLocalAck` injects this boundary without
killing an arbitrary operating-system process. It leaves the database claim
untouched, so recovery exercises the real lease semantics. This is a faithful
boundary test but not a claim that graceful test teardown is equivalent to a
power loss.
