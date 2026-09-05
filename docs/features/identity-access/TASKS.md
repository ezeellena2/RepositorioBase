# Identity Access — Tasks

**Status:** In progress. IA-002 and IA-003 are complete. Tasks 1–16 implemented the Organization and Platform
foundation; IA-004 through IA-008, IA-012, IA-014 and IA-009 remain recorded as `Review`, not approved or closed.
The [2026-09-05 direct review](CODE-REVIEW-2026-09-05.md) found incomplete browser journeys and security/session
defects at `7e9eb55`. **R1-R7 and L1 are corrected**, each with a test that fails on the code the review read;
[TRACEABILITY.md](TRACEABILITY.md) names them row by row and records the full verification run. Correcting them
does not by itself close a task or establish a usable end-to-end product — the remaining entries there are
coverage gaps and one named residual, and the task states below are unchanged by this work.

The broader baseline is not complete: IA-010 (Personal/B2C tenants and AR/DNI), IA-011 (recovery/change and session
management), IA-013 (Google OIDC/linking) and IA-015 (operations hardening) remain `Proposed` and unimplemented.

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

The dispatcher exposes one pass as its own method. Provider delivery runs from `OutboxWorker`; an explicit
local drop uses `LocalOutboxDeliveryService` inside Web, keeping token encryption and decryption in one process.
AppHost omits the separate worker for that local configuration. Local folder delivery is restricted to
Development/Test/Testing, and functional tests that do not configure a drop do not start this poller. Delivery,
retry and settlement rules remain in the dispatcher. Review follow-up L1 records interrupted local-file handling.

Historical harness observations (not fresh verification results):

- The Aspire PostgreSQL fixture has been reported to time out on a cold first run of `Web.AcceptanceTests` and to
  pass on retry. Not reproduced locally: both local runs were 6/6.
- `MigrationUpgradeTests.UserSessions_round_trip_preserves_preexisting_sentinels_and_removes_only_session_schema`
  previously failed intermittently in the full suite. The current `AssertUserSessionChronologyConstraint`
  truncates its clock to PostgreSQL microsecond precision with `clock.AddTicks(-(clock.Ticks % 10))`, preserving
  the one-microsecond invalid timestamp used by the assertion. This correction is present at `7e9eb55`;
  the earlier database-contention suspicion is not established. No new integration run is claimed here.

Neither is a licence to ignore a red run. A single unexplained failure should be re-run with the failing test
name captured before it is called a flake, as was done for the one above.

## IA-008 merge gates

Reconciled against source at `7e9eb55`; this is not a new integration-test run or merge approval:

- **Implementation corrected:** `VersionedTokenHash.FromPersistedValue` is internal; `Of` is the sole public
  hash factory. `Invitation.Issue`/`Reissue` guard `IsEmpty`, and decoding/re-encoding plus the canonical SQL
  constraint reject alternate Base64 padding. `VersionedTokenHashTests.The_only_public_way_to_obtain_a_hash_is_to_hash_a_token`,
  `A_non_canonical_encoding_is_not_a_persisted_hash` and `The_default_value_is_empty` exist. Direct Issue/Reissue
  tests with `default` were not found; that narrow coverage gap is not the former missing implementation.
- **Implementation corrected:** `InvitationCanonicalForm` and `InvitationRecipientComposition` disable and
  restore `TR_Invitations_PreventSettledChange` around data repair.
  `MigrationUpgradeTests.Invitation_canonicalization_upgrades_settled_and_colliding_rows_and_leaves_the_trigger_operative`
  covers accepted/cancelled history, case/NFC collisions, preserved rows and the restored trigger.
- **Still applicable:** the maintainer's merge policy for `Co-Authored-By` trailers and
  `.claude/settings.local.json`. This documentation change does not alter that policy or configuration.
- **Closure defects, corrected:** R1–R7 and L1 of [CODE-REVIEW-2026-09-05.md](CODE-REVIEW-2026-09-05.md) have
  remediation and focused regression evidence, named per requirement in [TRACEABILITY.md](TRACEABILITY.md). Task
  states and workflow gates are not advanced by that correction either; what changed is that the defects and the
  browser journeys they blocked are no longer open.

## Executable-task contract

Before `Ready`, record actor, preconditions, request marker/permission, tenant scope, files/migration, compile-safe RED, behavioral RED, GREEN/REFACTOR, denial/concurrency/replay tests, audit/outbox behavior, and evidence. Platform tasks additionally prove invitation-before-MFA, bootstrap recovery, MFA freshness, last-owner, reserved-tenant, projection, and prohibited-capability invariants. IA-009 reaches `Review` only after IA-014 Platform verification. IA-001 approves documentation only.
