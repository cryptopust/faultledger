# Final test matrix

The table links major claims to executable tests. Docker-required tests are not
counted as passing on this workstation.

| Invariant ID | Scenario | Failure injection | Expected local state | Provider state / submissions | Inbox | Outbox | Consumer effect | Primary test | Docker? | Executed status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| FL-INV-001 | exact Money | excessive scale/currency mismatch | rejected before persistence | 0 | n/a | n/a | n/a | `MoneyTests` | No | PASS |
| FL-INV-002 | same-key replay | identical/different fingerprints | same transfer or conflict | no replay submit | n/a | n/a | n/a | `TransferServiceTests` | No | PASS |
| FL-INV-003 | 100-way contention | simultaneous registration | one durable transfer | 1 | n/a | n/a | n/a | `SameIdempotencyKey_OneHundredConcurrentRequests_SubmitsProviderExactlyOnce` | Yes | BLOCKED |
| FL-INV-004 | timeout after accept | explicit ambiguous scenario | Unknown/DoNotRepost | 1 | n/a | n/a | n/a | `UnknownOutcomeLaboratoryTests` | No | PASS |
| FL-INV-005 | NotFound | lookup returns NotFound | remains Unknown | unchanged | n/a | n/a | n/a | reconciliation tests | No | PASS |
| FL-INV-006 | duplicate callback | same event repeatedly | one transition | unchanged | one durable row | at most one completion event | n/a | callback tests | Yes for persistence | contract PASS / DB BLOCKED |
| FL-INV-007 | stale callback | Completed then Accepted | Completed | unchanged | processed/stale | unchanged | n/a | `CallbackTransitionTests` | No | PASS |
| FL-INV-008 | processing crash | hook before commit throws | unchanged | unchanged | pending survives | none | none | callback rollback test | Yes | BLOCKED |
| FL-INV-009 | completion rollback | hook before commit throws | prior state | unchanged | n/a | zero new row | none | `CompletionTransactionRollback_LeavesTransferAndOutboxUnchanged` | Yes | BLOCKED |
| FL-INV-010 | publish/ack crash | hook after remote publish throws | Completed | unchanged | n/a | pending then published, same ID | receipts 2/effect 1 | `CrashAfterPublishBeforeAck_RedeliversStableEventAndConsumerEffectIsOne` | Yes | BLOCKED |
| FL-INV-011 | duplicate delivery | same event ten times | unchanged | unchanged | n/a | unchanged | receipts 10/effect 1 | `ConsumerReceivesSameEventTenTimes_AppliesOneDurableLogicalEffect` | Yes | BLOCKED |
| FL-INV-012 | worker restart | abandoned lease | unchanged | unchanged | n/a | reclaimed/published | one | `ClaimOwnerCrash_AfterLeaseExpiryAnotherDispatcherPublishes` | Yes | BLOCKED |
| FL-INV-013 | audit progression | normal/Unknown/reconcile/callback | ordered history | unchanged | transaction-aligned | transaction-aligned | n/a | outbox/callback PostgreSQL assertions | Yes | BLOCKED |
| FL-INV-014 | oversized callback | unknown-length 4096-byte stream | no mutation | unchanged | zero | none | none | `OversizedChunkedBody_IsRejectedWithoutReadingAnUnboundedPayload` | No | PASS |
