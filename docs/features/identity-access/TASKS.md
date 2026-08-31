# Identity Access — Tasks

**Status:** Proposed.

## Review Workload Forecast

Decision needed before apply: Yes  
Chained PRs recommended: Yes  
Chain strategy: pending  
400-line budget risk: High

Suggested units: stack/migrations/test harness; domain/persistence; authz/registration/session; invitations/outbox; React/E2E. A person chooses the chain strategy before apply.

## Canonical requirement ownership

- `IA-REQ-006..008`: IA-005 and IA-007.
- `IA-REQ-026`: IA-005, IA-006, IA-007, and IA-008 for their respective events.
- `IA-REQ-030`: IA-005 only.
- `IA-REQ-033..036`: IA-004 only.
- `IA-REQ-037`: IA-003 and IA-004.
- `IA-REQ-038`: IA-005 only; IA-006..008 apply endpoint contracts and IA-009 supplies evidence.
- IA-009 owns no normative requirement.

## Tracked work

| ID | Task | Requirements | Status | Depends on | Observable result |
|---|---|---|---|---|---|
| IA-001 | Approve SPEC and ADR | — | Review | — | a person accepts scope and security decisions |
| IA-002 | Specialize React/PostgreSQL and create the test harness | IA-REQ-031 | Blocked | IA-001 | target stack and real-PostgreSQL tests run |
| IA-003 | Add `BaselinePostgreSql` and safe startup | IA-REQ-032, 037 | Blocked | IA-002 | restart preserves a Todo sentinel and creates no administrator |
| IA-004 | Model and persist identity, tenant, organization profile, membership, and audit foundation; add `IdentityAccess` | IA-REQ-001, 002, 033..037 | Blocked | IA-003 | UUID/composite constraints and both migration paths pass |
| IA-005 | Persist roles/permissions; enforce authorization and API contracts | IA-REQ-006..013, 026, 030, 038 | Blocked | IA-004 | permissions, denials, and shared HTTP contracts pass |
| IA-006 | Register and confirm organizations | IA-REQ-003..005, 026, 027, 029; applies 038 | Blocked | IA-005 | pending state activates atomically with endpoint contracts |
| IA-007 | Add sessions, login controls, and active tenant | IA-REQ-006..008, 019..026, 029, 031; applies 038 | Blocked | IA-006 | sessions, limits, tenant switching, and HTTP errors pass |
| IA-008 | Add invitations and reliable outbox delivery | IA-REQ-014..018, 026..029; applies 038 | Blocked | IA-007 | onboarding, delivery, and endpoint contracts pass |
| IA-009 | Deliver React and E2E acceptance evidence | evidence only, including 038 | Blocked | IA-008 | every first-increment journey and client contract passes |
| IA-010 | Add Personal tenant and AR/DNI protection | roadmap | Proposed | IA-009, PII policy | protected Personal profile works |
| IA-011 | Add recovery/change and session management | roadmap | Proposed | IA-009 | recovery and revocation work |
| IA-012 | Add TOTP and recovery codes | roadmap | Proposed | IA-011 | sensitive actions require stronger authentication |
| IA-013 | Add Google OIDC and explicit linking | roadmap | Proposed | IA-011, IA-012 | linking never trusts email coincidence |
| IA-014 | Add Platform and controlled bootstrap | roadmap | Proposed | IA-012 | no default credential or global bypass exists |
| IA-015 | Complete operations and preproduction gate | roadmap | Proposed | IA-010..014 | hardened-baseline evidence is complete |

## Executable-task contract

Before `Ready`, record actor, preconditions, input/output/errors, permission or `IPublicRequest`, tenant scope, files/migration, compile-safe shape RED, behavioral RED, GREEN/REFACTOR, denial/concurrency/cross-tenant tests, audit/outbox behavior, and evidence. IA-001 approves documentation only; it authorizes no implementation or external effect.
