# Identity Access — Tasks

**Status:** In progress. IA-002 and IA-003 are complete. Task 13 verified the pre-Platform foundation and Tasks
14-16 delivered and verified the Platform slice, so IA-004 through IA-008, IA-012, IA-014 and IA-009 are all in
`Review`. IA-009 moved last and only after the Platform journeys ran, which is the order its gate requires.

The roadmap tasks are untouched and are not implemented: IA-010 (personal tenants and AR/DNI), IA-011
(recovery/change and session management), IA-013 and IA-015 remain `Proposed`.

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
| IA-004 | Core identity/tenant/membership/audit persistence | IA-REQ-001,002,033..037 | Review | IA-003 | constraints, stale-write proof, upgrades |
| IA-005 | Roles, authorization, HTTP contract | IA-REQ-006..013,026,030,038 | Review | IA-004 | permission/Problem Details contracts |
| IA-006 | Organization registration/confirmation | IA-REQ-003..005,026..029; applies 038 | Review | IA-005 | atomic, idempotent neutral replay |
| IA-007 | Sessions, limits, active tenant | IA-REQ-006..008,019..026,029,031; applies 038 | Review | IA-006 | revocable sessions and limits |
| IA-008 | Invitations and reliable outbox | IA-REQ-014..018,026..029,047; applies 038 | Review | IA-007 | secure onboarding and delivery |
| IA-012 | Platform invitation persistence, credential onboarding, MFA and recovery codes | IA-REQ-041 | Review | IA-008 | persisted invitation, password confirmation, encrypted TOTP, hashed codes, step-up |
| IA-014 | Platform bootstrap, administration, operations panel | IA-REQ-039..040,042..046; applies 038 | Review | IA-012 | cold-start recovery, safe directories, no bypass |
| IA-009 | React and E2E acceptance evidence | evidence only, including 038 | Review | IA-014 | post-Platform verified journeys ready for Review |
| IA-010 | Personal tenant and AR/DNI | roadmap | Proposed | IA-009, PII policy | protected profile |
| IA-011 | Recovery/change and session management | roadmap | Proposed | IA-009 | recovery lifecycle |
| IA-013 | Google OIDC and linking | roadmap | Proposed | IA-011, IA-012 | explicit secure linking |
| IA-015 | Operations/preproduction gate | roadmap | Proposed | IA-010..014 | hardened baseline |

## IA-008 delivery boundary

IA-008 owns the onboarding orchestration: issue, replace, reissue, withdraw, token-aware registration and
authenticated acceptance, with their audit and their transactional outbox intent. It also owns deciding who
invalidates a superseded envelope — the request that supersedes an offer terminalizes the previous `OutboxSecret`
in the same transaction, so no withdrawn or rotated token is left deliverable.

Task 11 built the delivery loop: the lease and compare-and-swap dispatcher of IA-REQ-028, and the
lease/decrypt/render/send/terminalize handling of IA-REQ-018, with `OutboxMessage` gaining the dispatch state
Task 7 had left it without.

The dispatcher exposes one pass as its own method and runs from a separate `OutboxWorker` process. Registering a
hosted service inside the web application would also start it inside every functional test that boots that
application — `WebApiFactory` only strips `IHostedService` when an environment name is supplied — and it would
race the rows those tests assert on. The loop belongs to the worker; every rule about what to deliver, when to
retry and when to give up belongs to the dispatcher, which is what makes them testable against a clock a test
moves by hand.

Known harness flakes, recorded to watch rather than diagnosed:

- The Aspire PostgreSQL fixture has been reported to time out on a cold first run of `Web.AcceptanceTests` and to
  pass on retry. Not reproduced locally: both local runs were 6/6.
- `MigrationUpgradeTests.UserSessions_round_trip_preserves_preexisting_sentinels_and_removes_only_session_schema`
  fails intermittently inside the full `Infrastructure.IntegrationTests` run — observed twice, 142/143 — and
  passes 6/6 in isolation and on every re-run of the whole suite. Every migration round-trip test creates and
  drops its own database on the shared PostgreSQL instance while the rest of the suite runs in parallel, so the
  suspicion is contention over `CREATE`/`DROP DATABASE`, not a defect in the migration under test. Unproven.

Neither is a licence to ignore a red run. A single unexplained failure should be re-run with the failing test
name captured before it is called a flake, as was done for the one above.

## IA-008 merge gates

Open, and none of them block a slice. They block the merge:

- `VersionedTokenHash.FromPersistedValue` is public, `default(VersionedTokenHash)` is still constructible and
  invalid, and the parser accepts non-canonical Base64. Close it with a single public factory, a `default` guard
  in `Issue`/`Reissue`, and a decode-then-reencode check in both the domain and the SQL constraint. PostgreSQL
  cannot tell a raw token from a digest, so that provenance belongs at the Application boundary.
- The `UPDATE` statements in `InvitationCanonicalForm` and `InvitationRecipientComposition` still collide with
  `TR_Invitations_PreventSettledChange` for `Accepted`/`Cancelled` rows and for losers cancelled in the same
  migration. Needs upgrade tests for settled legacy rows, a canonicalized collision and NFC from the previous
  migration, each verifying the trigger is restored afterwards.
- `Co-Authored-By` trailers and `.claude/settings.local.json` per the maintainer's merge policy.

## Executable-task contract

Before `Ready`, record actor, preconditions, request marker/permission, tenant scope, files/migration, compile-safe RED, behavioral RED, GREEN/REFACTOR, denial/concurrency/replay tests, audit/outbox behavior, and evidence. Platform tasks additionally prove invitation-before-MFA, bootstrap recovery, MFA freshness, last-owner, reserved-tenant, projection, and prohibited-capability invariants. IA-009 reaches `Review` only after IA-014 Platform verification. IA-001 approves documentation only.
