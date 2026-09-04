# Identity Access — Tasks

**Status:** In progress. IA-002 and IA-003 are complete; the remaining Identity Access tasks remain pending approval and implementation.

## Review Workload Forecast

Decision needed before apply: No
Chained PRs recommended: Yes
Delivery decision: size:exception (not a chain strategy)
400-line budget risk: High

Suggested units: stack/migrations/test harness; domain/persistence; authorization/registration/session; invitations/outbox/MFA/Platform backend; React/Platform/E2E. The maintainer accepted `size:exception` and direct work on `main`; these units remain mandatory commit, verification, and rollback boundaries.

## Canonical requirement ownership

- IA-005 owns IA-REQ-030/038 and request classification; IA-004 owns IA-REQ-033..036, including stale-write evidence.
- IA-006 owns registration replay; IA-012 owns IA-REQ-041; IA-014 owns IA-REQ-039..040 and IA-REQ-042..046. IA-009 provides evidence only.

## Tracked work

| ID | Task | Requirements | Status | Depends on | Observable result |
|---|---|---|---|---|---|
| IA-001 | Approve SPEC and ADR | — | Review | — | human approval |
| IA-002 | PostgreSQL target and harness | IA-REQ-031 | Complete | IA-001 | real-PostgreSQL tests |
| IA-003 | Safe baseline migration/startup | IA-REQ-032,037 | Complete | IA-002 | sentinel preserved; no administrator |
| IA-004 | Core identity/tenant/membership/audit persistence | IA-REQ-001,002,033..037 | Blocked | IA-003 | constraints, stale-write proof, upgrades |
| IA-005 | Roles, authorization, HTTP contract | IA-REQ-006..013,026,030,038 | Blocked | IA-004 | permission/Problem Details contracts |
| IA-006 | Organization registration/confirmation | IA-REQ-003..005,026..029; applies 038 | Blocked | IA-005 | atomic, idempotent neutral replay |
| IA-007 | Sessions, limits, active tenant | IA-REQ-006..008,019..026,029,031; applies 038 | Blocked | IA-006 | revocable sessions and limits |
| IA-008 | Invitations and reliable outbox | IA-REQ-014..018,026..029,047; applies 038 | Blocked | IA-007 | secure onboarding and delivery |
| IA-012 | Platform invitation persistence, credential onboarding, MFA and recovery codes | IA-REQ-041 | Blocked | IA-008 | persisted invitation, password confirmation, encrypted TOTP, hashed codes, step-up |
| IA-014 | Platform bootstrap, administration, operations panel | IA-REQ-039..040,042..046; applies 038 | Blocked | IA-012 | cold-start recovery, safe directories, no bypass |
| IA-009 | React and E2E acceptance evidence | evidence only, including 038 | Blocked | IA-014 | post-Platform verified journeys ready for Review |
| IA-010 | Personal tenant and AR/DNI | roadmap | Proposed | IA-009, PII policy | protected profile |
| IA-011 | Recovery/change and session management | roadmap | Proposed | IA-009 | recovery lifecycle |
| IA-013 | Google OIDC and linking | roadmap | Proposed | IA-011, IA-012 | explicit secure linking |
| IA-015 | Operations/preproduction gate | roadmap | Proposed | IA-010..014 | hardened baseline |

## IA-008 delivery boundary

IA-008 owns the onboarding orchestration: issue, replace, reissue, withdraw, token-aware registration and
authenticated acceptance, with their audit and their transactional outbox intent. It also owns deciding who
invalidates a superseded envelope — the request that supersedes an offer terminalizes the previous `OutboxSecret`
in the same transaction, so no withdrawn or rotated token is left deliverable.

The delivery loop itself is IA-011's: the lease and compare-and-swap worker of IA-REQ-028, and the
lease/decrypt/render/send/terminalize handler of IA-REQ-018. None of it exists yet — no worker, lease columns,
handler contract, decrypt port, email adapter or test sink — and it is a separate RED. A hosted service added
naively would also run inside the whole functional suite, because `WebApiFactory` only strips `IHostedService`
when an environment name is supplied; the dispatcher needs an explicitly invocable unit.

Known harness flakes, recorded to watch rather than diagnosed:

- The Aspire PostgreSQL fixture has been reported to time out on a cold first run of `Web.AcceptanceTests` and to
  pass on retry. Not reproduced locally: both local runs were 6/6.
- `Infrastructure.IntegrationTests` reported 142/143 once, in a run chained immediately after the functional
  suite, and 143/143 on both re-runs. The failing test was not captured, so the cause is unknown; the suspicion is
  contention for the shared PostgreSQL instance between back-to-back suites, not a defect in the code under test.

Neither is a licence to ignore a red run. A single unexplained failure in either suite should be re-run with the
failing test name captured before it is called a flake.

## Executable-task contract

Before `Ready`, record actor, preconditions, request marker/permission, tenant scope, files/migration, compile-safe RED, behavioral RED, GREEN/REFACTOR, denial/concurrency/replay tests, audit/outbox behavior, and evidence. Platform tasks additionally prove invitation-before-MFA, bootstrap recovery, MFA freshness, last-owner, reserved-tenant, projection, and prohibited-capability invariants. IA-009 reaches `Review` only after IA-014 Platform verification. IA-001 approves documentation only.
