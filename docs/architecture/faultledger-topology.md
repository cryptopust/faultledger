# FaultLedger topology

```mermaid
flowchart LR
    Client --> API[FaultLedger API]
    API --> App[Application orchestration]
    App --> DB[(PostgreSQL)]
    DB --> T[Transfers]
    DB --> A[Transfer audit history]
    DB --> I[Provider inbox]
    DB --> O[Transactional outbox]
    App --> P[Synthetic provider]
    P --> L[Lookup / reconciliation]
    Callback[Provider callback] --> API
    O --> D[Outbox dispatcher]
    D --> X[Toxiproxy]
    X --> C[Simulated consumer]
    C --> DB
```

The consumer is a laboratory fixture, not another FaultLedger business system.
Toxiproxy sits only on the outbound HTTP path. No broker, Redis, global
ordering service, or real financial network is part of this topology.
