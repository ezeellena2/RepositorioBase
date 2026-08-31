# Identity Access — Traceability

**Status:** Proposed. `Pending` means not implemented. IA-009 supplies end-to-end acceptance evidence but is never a normative owner.

| Requirement | Scope | Normative owner(s) | Planned evidence | Evidence |
|---|---|---|---|---|
| IA-REQ-001..002 | global identity and tenant-free user | IA-004 | domain, mapping, uniqueness, UUID migration | Pending |
| IA-REQ-003..005 | registration, atomicity, confirmation gate | IA-006 | branch, rollback, replay, and confirmation tests | Pending |
| IA-REQ-006..008 | tenant resolution and active membership | IA-005, IA-007 | evaluator tests plus persisted-session/cross-tenant matrix | Pending |
| IA-REQ-009..013 | catalog, roles, public marker, filtering, versioning | IA-005 | domain, architecture, permission, concurrency, and denial tests | Pending |
| IA-REQ-014..018 | invitation onboarding and secret envelope | IA-008 | issue/register/accept/replay and delivery tests | Pending |
| IA-REQ-019..025 | sign-in, cookies, session lifecycle, client storage | IA-007 | options, rate-limit, lockout, antiforgery, expiry, and revocation tests | Pending |
| IA-REQ-026 | membership/role change and sensitive denial events | IA-005 | append-only correlation/redaction/event tests | Pending |
| IA-REQ-026 | registration and confirmation events | IA-006 | transactional audit coverage tests | Pending |
| IA-REQ-026 | sign-in and session create/revoke events | IA-007 | session audit coverage tests | Pending |
| IA-REQ-026 | invitation issue and accept events | IA-008 | invitation audit coverage tests | Pending |
| IA-REQ-027 | confirmation effects | IA-006 | business transaction creates message and encrypted envelope | Pending |
| IA-REQ-027 | invitation effects | IA-008 | invitation/outbox atomicity tests | Pending |
| IA-REQ-028 | lease, CAS, backoff, idempotent delivery | IA-008 | concurrent claim, transient, expired, permanent, and replay tests | Pending |
| IA-REQ-029 | authorization/registration redaction | IA-005, IA-006 | allowlist and negative secret/PII scans | Pending |
| IA-REQ-029 | session/invitation/delivery redaction | IA-007, IA-008 | Problem Details, audit, outbox, and telemetry tests | Pending |
| IA-REQ-030 | 401/403/404 semantics | IA-005 | authorization matrix and stable Problem Details | Pending |
| IA-REQ-031 | target stack and same-origin policy | IA-002, IA-007 | architecture/configuration and cookie-session tests | Pending |
| IA-REQ-032 | no seed or destructive startup | IA-003 | restart/sentinel/no-user test | Pending |
| IA-REQ-033..036 | PostgreSQL uniqueness, composite FKs, concurrency, deletes | IA-004 | metadata plus real-PostgreSQL constraint tests | Pending |
| IA-REQ-037 | current-template baseline | IA-003 | empty-to-baseline and restart preservation | Pending |
| IA-REQ-037 | identity-access upgrade | IA-004 | empty-to-latest and baseline-to-latest sentinel preservation | Pending |
| IA-REQ-038 | shared Result types, HTTP mapping/writer, and OpenAPI contract | IA-005 | model, runtime, media-type, status/header/code, safe-500, and drift tests | Pending |
| IA-REQ-038 | registration/confirmation endpoint application | IA-005 | IA-006 DTO/status/error metadata and functional evidence | Pending |
| IA-REQ-038 | session/context/rate-limit endpoint application | IA-005 | IA-007 DTO/status/error metadata, `429` Problem Details, and `Retry-After` evidence | Pending |
| IA-REQ-038 | invitation endpoint application | IA-005 | IA-008 DTO/status/header/error metadata and functional evidence | Pending |
| IA-REQ-038 | React and browser acceptance | IA-005 | IA-009 sole-parser, MSW, lint/build, and E2E evidence only | Pending |

## IA-009 acceptance journeys

| Journey | Normative tasks supplying behavior | Evidence |
|---|---|---|
| register → confirm → sign in | IA-006, IA-007 | Pending |
| select two organizations without permission leakage | IA-005, IA-007 | Pending |
| invite → register → confirm → sign in → accept | IA-008 | Pending |
| existing identity accepts without duplicates | IA-008 | Pending |
| revoke session → reject cookie | IA-007 | Pending |
| baseline/latest restart preserves data | IA-003, IA-004 | Pending |

## Completion record

```text
Task: IA-000
Requirements: IA-REQ-...
Permission: ... / IPublicRequest because ...
Migration: ... / N/A
Tests and commands: ...
Results: ...
Review: ...
Accepted limitations: none / ...
```
