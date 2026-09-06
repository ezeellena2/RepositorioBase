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

The [existing plan now continues with Tasks 17–28](../../superpowers/plans/2026-08-31-identity-access-foundation.md#continuation-to-local-b2bb2c-functional-completion).
This is planned work, not new implementation, coverage, approval, or a change to historical `Review` states.
Task 17 produces and obtains approval of a finite contract delta; its decision register is still proposed.
Only then do the dependent synthetic implementation tasks become `Ready`. Real-PII and production/reference
compliance have separate gates, so local functional completion cannot silently authorize deployment.

## Continuation roadmap mapping — all unchecked

| Plan task | Work | Tracking | Dependency/approval boundary |
|---|---|---|---|
| 17 | Local contracts, reference revision and explicit adoption/deviation mapping | IA-001; IA-010/011/013/015 | Human acceptance of proposed C1–C7; documentation only |
| 18 | Registration state-level privacy and CUIT reservation | IA-006; IA-009 evidence | **Done 2026-09-06.** C1 accepted; IA-REQ-003/004/005 amended and IA-REQ-048 added in SPEC §4 |
| 19 | Personal ownership and protected AR/DNI persistence | IA-010; IA-004 persistence | 18; 17 C1/C3/C7; synthetic only until PII gate |
| 20 | Personal signup, own profile and context React journey | IA-010; IA-009 evidence | 19 |
| 21 | Own sessions and recent reauthentication seam | IA-011; IA-007 sessions | 17 C2/C4; password proof now, Google proof in 23 |
| 22 | Password recovery and change end to end | IA-011; IA-009 evidence | 21; 17 C4 |
| 23 | Google login, explicit linking and last authenticator | IA-013; IA-009 evidence | 20–22; 17 C4; live provider activation separate |
| 24 | Custom Organization role administration | IA-005 continuation; IA-009 evidence | 17 C5 |
| 25 | Membership administration, ownership transfer and invitation lifecycle | IA-005/008 continuation; IA-009 evidence | 21/24; 17 C5 |
| 26 | Lifecycle, MFA recovery, retention executor and restore admission guard | IA-011/012/015; existing event owners | 19/21–25; 17 C6/C7 |
| 27 | Shared limits, keys, deployment guards and operations evidence | IA-015; IA-007/012/014 control owners | 26; 17 C6/C7 |
| 28 | Fixed full-journey acceptance and scoped closure | IA-009 evidence only; all continuation owners | 18–27 local evidence; separate PII/production/reference gates |

The roadmap requirement owners remain in SPEC; this mapping does not invent approved IA-REQ identifiers.
Custom roles and full membership administration are explicit continuation work even though their initial
model/pipeline owners IA-005 and IA-008 are already recorded as `Review`. Historical verification is retained;
future tests named by the continuation remain planned until run against implemented behavior.

## Task 17 — state of the contract package

Task 17 has three steps. Two are drafted; the third is the human decision and has not been taken.

| Step | State | Where it lives |
|---|---|---|
| 1 — draft the exact contract delta for C1–C7 | Drafted; C1 accepted, C2–C7 awaiting decision | [SPEC §14](SPEC.md#14-task-17-decision-package-proposed-not-approved), [ADR-004 decisions 18–24](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#proposed-continuation-decisions-task-17--not-accepted) |
| 2 — map reference adoption at the pinned revision | Drafted, awaiting decision | [SPEC §15](SPEC.md#15-reference-adoption-map-proposed--task-17-step-2) |
| 3 — structural review, then one human decision | **Partly taken (2026-09-06): C1 accepted, C2–C7 not.** Five amendments are required before C3, C4, C6 and C7 are put forward again | [ADR-004 decision record](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#decision-record--2026-09-06) |

**Task 17 is not complete.** One entry of seven is accepted. `Drafted` means the text exists and is internally
consistent; it is not approval and it is not evidence. For C2–C7 no behaviour is implemented and no test named in
§14 has been written. Amendments A1–A5 in the decision record are changes to the proposals, not open questions:
each of those blocks must return with its contract already reconciled, because a contradiction left as a note for
the implementer is not a delivered contract.

## Task 18 — done 2026-09-06

**Visible outcome met:** a caller can no longer infer whether someone else's address exists by submitting it
anonymously and then claiming the same fresh CUIT with their own identity. The anonymous phase reserves nothing.

| Step | What happened |
|---|---|
| RED | `RegistrationPrivacySequenceTests` — 4 of 4 failing. The headline: `known.ClaimErrorCode` should be `registration_conflict` but was `null`, i.e. the attacker's claim succeeded when the probed address existed and conflicted when it did not |
| GREEN | `PendingRegistrationIntent` and its additive `DeferredRegistrationReservation` migration; the anonymous branch of `RegisterOrganizationCommandHandler` writes one intent, one outbox message and one tenantless audit event and takes no business lock; `ConfirmEmailCommandHandler` finalizes the graph in one `IApplicationTransaction`; two delivery handlers carry the confirmation link and the tokenless sign-in notice |
| REFACTOR | The replay-after-conflict defect found during the rewrite was fixed (a spent envelope now answers the intent's recorded outcome, not the envelope's success) and the exception path the change orphaned was removed |

**Commands run, both from the plan.**

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegistrationPrivacySequenceTests|RegistrationTests|ConfirmEmailTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter MigrationUpgradeTests
```

37/37 and 11/11. Whole solution afterwards: Domain 162, Application.Unit 192, Infrastructure.Integration 236,
Application.Functional 401, browser acceptance 21; client 106 with lint clean; Debug and Release builds 0 errors.

**Effects proved on real PostgreSQL:** two concurrent same-CUIT finalizations leave one organization with the loser
settled `Conflicted`; two concurrent same-address finalizations leave one identity; an equivalent replay of the
initiation produces one intent and one message; a replay of a finalized token returns its recorded outcome,
conflict included; an expired envelope settles its intent `Expired`; and an injected failure after a real insert
leaves no orphan tenant, profile, membership, role, audit or claim.

**Limitation, named rather than claimed:** an anonymous initiation still sends one message per distinct submission
to any address that can be typed, and no per-address or per-CUIT budget bounds that. It is the same exposure as
before this task and is scoped to C6/C7 and Task 27, not closed here.

### Proposed requirements and the tasks they unblock

Each entry proposes its own requirement numbers. IA-REQ-047 remains the highest approved number; 048–057 are
allocated only inside the proposal and become real if and when the decision accepts them.

| Entry | Proposed requirements | Amends | Unblocks on acceptance | Still blocked afterwards |
|---|---|---|---|---|
| C1 **(accepted 2026-09-06; implemented)** | IA-REQ-048, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | IA-REQ-003, IA-REQ-004, IA-REQ-005 | 18, **done 2026-09-06**; the registration seam 19/20/23 build on | nothing from this entry: the IA-REQ-003 residual is closed on Task 18's paired-sequence evidence |
| C2 | IA-REQ-049 | IA-REQ-021, IA-REQ-023 | the session half of 21, 22, 23 | the proof half of 21/22, which is C4 |
| C3 **(amend A1)** | IA-REQ-050 | none; extends IA-REQ-002/044 | 19, 20; the data contracts in 26/27 | real DNI capture, which is C7's gate |
| C4 **(amend A2/A3)** | IA-REQ-051, IA-REQ-052 | IA-REQ-022 and §8, for one named callback route | 21, 22, 23; the recovery part of 26 | live provider registration, which is per-environment |
| C5 | IA-REQ-053 | IA-REQ-047, closing its deferred note | 24, 25 | C4, without which ownership transfer has no proof |
| C6 **(amend A3/A4)** | IA-REQ-054, IA-REQ-055 | makes IA-REQ-020 precise; extends IA-REQ-042 | 26, 27 against synthetic fixtures | live restore release, which needs the external authority |
| C7 **(amend A4/A5)** | IA-REQ-056, IA-REQ-057 | strengthens IA-REQ-019 to shared, fail-closed state | the synthetic design in 19, 26, 27 | real personal data, and production — separate gates in 28 |

A declined or amended entry blocks only its own consumers. Tasks 1–16 and their recorded `Review` states are
untouched by this package.

## First-increment Review Workload Forecast (historical)

This forecast describes Tasks 1–16, not approval of the continuation's pending contracts or deployment.

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
