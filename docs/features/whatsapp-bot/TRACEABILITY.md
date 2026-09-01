# WhatsApp Bot — Traceability

**Status:** Proposed. `Pending` means not implemented. WA-009 supplies end-to-end acceptance evidence but is never a normative owner.

| Requirement | Scope | Normative owner(s) | Planned evidence | Evidence |
|---|---|---|---|---|
| WA-REQ-001 | channel record, mode, singleton platform channel | WA-002 | domain, mapping, and singleton constraint tests | Pending |
| WA-REQ-002 | encrypted channel credentials | WA-002 | envelope mapping, key version, and negative disclosure scans across endpoints, projections, logs | Pending |
| WA-REQ-003 | non-expiring token and connectivity check | WA-002 | token validity probe, expired-token transition, and panel DTO tests | Pending |
| WA-REQ-004 | Platform authority for channel administration | WA-002 | permission, active-Platform-tenant, recent-MFA, and no-bypass tests | Pending |
| WA-REQ-005 | routing by WABA identifier | WA-002, WA-003 | known, unknown, and suspended channel routing tests | Pending |
| WA-REQ-006 | channel health and suspension | WA-002 | health persistence, credential-failure suspension, and send-halt tests | Pending |
| WA-REQ-007 | link binding and active uniqueness | WA-004 | domain plus real-PostgreSQL partial unique index tests | Pending |
| WA-REQ-008 | session-derived link authority | WA-004 | authenticated-creation, submitted-tenant rejection, and submitted-identity rejection tests | Pending |
| WA-REQ-009 | code confirmation and hash-only storage | WA-004 | expiry, reuse, mismatch, wrong-sender, and hash-only persistence tests | Pending |
| WA-REQ-010 | administrative creation stays pending | WA-004 | administrator-created link requires inbound confirmation test | Pending |
| WA-REQ-011 | opt-in evidence retention | WA-004 | activation message retention and audit reproduction tests | Pending |
| WA-REQ-012 | per-message revalidation | WA-004 | revoked link, suspended membership, suspended tenant, disabled identity matrix | Pending |
| WA-REQ-013 | phone normalization | WA-004 | canonical write and lookup tests including national variant forms | Pending |
| WA-REQ-014 | webhook challenge | WA-003 | matching and non-matching verify-token tests | Pending |
| WA-REQ-015 | signature verification | WA-003 | captured raw-body positive, tampered-body, missing, malformed, and constant-time tests | Pending |
| WA-REQ-016 | explicit public marker | WA-003 | architecture test rejecting public-by-omission | Pending |
| WA-REQ-017 | acknowledge before processing | WA-003 | persistence-then-`200` and business-failure-still-`200` tests | Pending |
| WA-REQ-018 | provider identifier deduplication | WA-003 | real-PostgreSQL unique constraint and redelivery no-effect tests | Pending |
| WA-REQ-019 | provider timestamp ordering | WA-003 | out-of-order arrival ordering test | Pending |
| WA-REQ-020 | reject before model invocation | WA-003, WA-004 | unlinked-sender no-invocation, fixed-response, and per-number rate-limit tests | Pending |
| WA-REQ-021 | transactional outbox for sends | WA-005 | same-transaction write and provider idempotency-key delivery tests | Pending |
| WA-REQ-022 | service window enforcement | WA-005 | in-window free-form, out-of-window refusal, and template-only tests | Pending |
| WA-REQ-023 | delivery evidence | WA-005 | provider identifier persistence and receipt-updates-only tests | Pending |
| WA-REQ-024 | catalog declaration and synchronization | WA-006 | idempotent catalog sync and drift tests | Pending |
| WA-REQ-025 | query/action dispatch split | WA-006 | table check constraint plus architecture test forbidding declarative actions | Pending |
| WA-REQ-026 | server-injected tenant filter | WA-006 | injected-filter, model-supplied-parameter isolation, and unfilterable-statement activation refusal | Pending |
| WA-REQ-027 | model input restriction | WA-006 | prompt-construction allowlist and negative leak tests | Pending |
| WA-REQ-028 | selection validation | WA-006 | unknown, inactive, unavailable, unpermitted, and schema-invalid selection tests | Pending |
| WA-REQ-029 | resolution chain | WA-006 | full intersection matrix including absence rather than denial | Pending |
| WA-REQ-030 | per-organization tuning | WA-006 | disable, tighten, cap amount, cap count, restrict channel, and no-weakening-irreversible tests | Pending |
| WA-REQ-031 | module credentials | WA-006, WA-008 | schema validation, envelope, environment, expiry-before-effect, and health tests | Pending |
| WA-REQ-032 | declared not-understood outcome | WA-006 | no-capability outcome and audit tests | Pending |
| WA-REQ-033 | durable run before effect | WA-007 | draft creation, no-effect-before-confirmation, and expiry tests | Pending |
| WA-REQ-034 | application-computed approval values | WA-007 | rendered-summary provenance test and negative model-authored-value test | Pending |
| WA-REQ-035 | explicit confirmation | WA-007 | expired, already-confirmed, cancelled, foreign-tenant, and irreversible-web-default tests | Pending |
| WA-REQ-036 | mandatory reconciliation | WA-008 | architecture test rejecting an irreversible capability without reconciliation | Pending |
| WA-REQ-037 | attempts and unknown handling | WA-008 | attempt recording plus `Unknown`-reconciles-never-retries test | Pending |
| WA-REQ-038 | single effect on replay | WA-007 | webhook, confirmation, and worker-lease replay tests with unique-constraint backstop | Pending |
| WA-REQ-039 | run audit | WA-007 | append-only actor, tenant, origin, summary, outcome, and reference tests | Pending |
| WA-REQ-040 | Flow versioning and publication | WA-010 | checksum drift, per-channel publication, and republish tests | Pending |
| WA-REQ-041 | flow token binds a run | WA-010 | token-to-run resolution and foreign-token refusal tests | Pending |
| WA-REQ-042 | no business rules in definitions | WA-010 | data-endpoint derivation test and definition-content architecture test | Pending |
| WA-REQ-043 | data endpoint verification | WA-010 | key material, decryption, and unverifiable-request rejection tests | Pending |
| WA-REQ-044 | redaction | WA-002, WA-003, WA-007 | negative scans across logs, Problem Details, audit, telemetry, and retention tests | Pending |
| WA-REQ-045 | channel permission intersection | WA-004, WA-006 | intersection test plus negative escalation test | Pending |
| WA-REQ-046 | routing observability | WA-006 | outcome, token, latency, and model recording without credentials | Pending |
| WA-REQ-047 | HTTP contract | WA-003, WA-007 | DTO, status, Problem Details, OpenAPI drift, and webhook-exemption tests | Pending |
| WA-REQ-048 | channel audience declaration and non-mixing | WA-002, WA-014 | real-PostgreSQL rejection of a link on a `Contacts` channel and of a foreign-tenant link elsewhere | Pending |
| WA-REQ-049 | phone claim and web-completed enrollment | WA-014 | claim-has-no-authority, no-capability-offered, registration-activates, and abandoned/expired-discard tests | Pending |
| WA-REQ-050 | audience-dependent unknown-sender reply | WA-003, WA-014 | fixed-reply, enrollment-invitation, and non-disclosure tests per audience | Pending |
| WA-REQ-051 | personal and organization scope isolation | WA-014 | dual-tenant identity isolation matrix in both directions | Pending |
| WA-REQ-052 | capability audience exposure | WA-006, WA-011 | resolution-chain intersection test plus architecture test asserting no fiscal or money-moving capability declares `Contacts` | Pending |
| WA-REQ-053 | row-scoped contact authorization | WA-011 | own-rows-only, message-supplied-identifier rejection, and no-aggregate/no-internal-data tests | Pending |
| WA-REQ-054 | multi-context links and acting-tenant disclosure | WA-004, WA-007 | one active link per tenant; single-context catalog with no accumulated permissions; summary names the acting tenant and confirmation is not skipped for a capability enabled in more than one context | Pending |
| WA-INV-001..008 | persistence invariants | WA-002, WA-003, WA-004, WA-006, WA-007 | real-PostgreSQL constraint, composite-FK cross-tenant rejection, delete-behavior, and stale-write `409` tests | Pending |
| WA-INV-009 | audit classification | WA-002 | architecture test asserting each table's base type against the section 5.3 table, and that no append-only table carries modification columns | Pending |
| WA-INV-010 | actor attribution outside HTTP | WA-003, WA-007 | worker-written row carries the link's identity; system-written row carries the declared principal; no audited row is unattributed | Pending |
| WA-INV-011 | UUID identifiers | WA-002 | architecture test rejecting sequential keys on tenant-scoped entities | Pending |
| WA-INV-012 | tenant scoping classification | WA-002, WA-006 | architecture test asserting each table's group against section 5.4, including each partial table's compensating rule | Pending |
| WA-INV-013 | denormalized tenancy on attempts | WA-008 | composite foreign key plus direct-query cross-tenant tests for projections, retention, and the reconciliation worker | Pending |

## WA-009 acceptance journeys

WA-009 remains `Blocked` until WA-008 has verified its slices. Only then may its acceptance evidence move to `Review`. Journeys: link a phone and confirm it; ask a question and receive a tenant-scoped answer; invoke a reversible action and confirm it; observe a second confirmation refused; observe an unlinked sender rejected; administer channel, links, and capabilities from the Platform panel.
