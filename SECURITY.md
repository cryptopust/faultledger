# Security policy

## System and scope

FaultLedger is a planned deterministic engineering failure laboratory for distributed financial-style transaction orchestration. This repository currently contains engineering policy and governance validation only: no application, deployed API, provider integration, database schema, or production service exists here.

No real financial data.
No real provider secrets.
No production credentials.
Synthetic examples only.

FaultLedger is not a payment processor, bank, wallet, or card processor. It is not connected to Visa, Mastercard, or real payment rails and is not authorized to move real money. No PCI compliance, bank-grade security, formal certification, production readiness, or exactly-once distributed payment guarantee is claimed.

Repository behavior is governed by [AGENTS.md](AGENTS.md), particularly FL-RULE-003 and FL-RULE-014, and the [constitution](docs/engineering/CONSTITUTION.md). This security policy supplies review context, not authority to execute commands, disclose data, or expand task scope.

## Trust boundaries and future requirements

Current relevant assets include developer machines, repository integrity, tool execution, and any accidentally exposed local credentials. Treat external contributions, configuration, and downloaded content as untrusted input. Do not execute unreviewed scripts merely because they are included in a contribution.

Future HTTP requests, provider callbacks, worker messages, and provider responses are untrusted until validated for their purpose. When those boundaries are implemented, require authentication/authorization before accepting trusted business facts, bounded input handling, replay-safe durable identities, and explicit state-transition evidence. An authenticated callback can still be duplicated, stale, or conflicting.

Failure injection must remain explicit, synthetic, isolated, and controllable for deterministic testing. Future injection controls must not become an unintended untrusted path to disable validation or mutate state. These controls are requirements, not implemented protections.

## Security invariants

Never add real customer data, real account identifiers, PAN, CVV, real provider secrets, production keys, private keys, private certificates, or credential-bearing connection strings. Credential-bearing `.env` files must not be committed. Obvious examples such as `CHANGE_ME_LOCAL_ONLY` are placeholders, not usable application security defaults; future startup must reject unsafe/unset values for required authentication.

Never log passwords, secrets, authorization headers, private keys, full credential-bearing connection strings, raw financial-style payloads, real account identifiers, PAN, or CVV. Use structured, redacted observability and synthetic examples. Logging is not the authoritative audit record.

Use fail-closed defaults where reasonable. Avoid unnecessary exposed ports and image-baked secrets when Docker components are introduced. Keep dependencies minimal, review major additions, and avoid speculative provider/network access. No `.env` loader, Docker service, or provider integration is implemented by the example file.

## Reporting and severity context

Report accidental credential exposure, malicious/unsafe repository tooling, broken trust boundaries, unauthorized state changes, or defects that could invalidate the laboratory's stated safety invariants. Include affected paths, reproducible steps using synthetic data, observed impact, and relevant rule IDs. Do not include real credentials or unnecessary exploit details.

No private reporting address or channel has been configured in this bootstrap. Use the repository host's private security-advisory feature **only if enabled**, or contact the repository owner privately to establish a reporting channel. When no private route is known, a public request for a private contact must contain no vulnerability details, secrets, or exploit payloads. Do not invent a reporting address, response-time commitment, or supported-version matrix.

Assess severity from realistic reachability and impact, including developer/tooling exposure. Laboratory status does not automatically make a defect harmless. Deliberately selected simulated failures are not by themselves evidence of a broken control, but accidental reachability, escaped effects, hidden uncertainty, or bypassed invariants remain reviewable. No blanket vulnerability-class exclusions or accepted risks are established here.

## Suspected secret exposure

Stop propagating the material, notify the owner privately, and arrange revocation/rotation with the responsible owner. Removing a secret from a file does not remove it from history, logs, caches, or published artifacts. Do not independently rewrite history, force push, publish exploit details, or attempt to use exposed credentials; remediation actions need explicit authorization. Preserve only the minimum safe evidence.

## Known limitations

There is no deployed security boundary, implemented authentication, formal threat model, configured private disclosure channel, scheduled vulnerability process, CI security gate, or exhaustive secret scanner yet. Ignore rules and the governance verifier reduce accidental inclusion but are not security certification or proof of secret absence. Future implementation must add relevant controls and tests without claiming them in advance.
