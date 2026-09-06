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
| 19 | Personal ownership and protected AR/DNI persistence, plus the shared attempt-budget store | IA-010; IA-004 persistence | 18 (done); 17 **C3 and C7**; synthetic only until PII gate |
| 20 | Personal signup, own profile and context React journey | IA-010; IA-009 evidence | 19, inheriting its C3/C7 approval; no further entry |
| 21 | Own sessions and recent reauthentication seam | IA-011; IA-007 sessions | 17 C2/C4; password proof now, Google proof in 23 |
| 22 | Password recovery and change end to end | IA-011; IA-009 evidence | 21; 17 C4 |
| 23 | Google login, explicit linking and last authenticator | IA-013; IA-009 evidence | 20–22; 17 C4; live provider activation separate |
| 24 | Custom Organization role administration | IA-005 continuation; IA-009 evidence | 17 C5 |
| 25 | Membership administration, ownership transfer and invitation lifecycle | IA-005/008 continuation; IA-009 evidence | 21/24; 17 C5 |
| 26 | Lifecycle, MFA recovery, retention executor, restore admission guard and both halves of the documentary dispute | IA-011/012/015; existing event owners | 19/21–25; 17 C6/C7 plus C3/C4, the dispute needing C4's proof |
| 27 | Remaining budget scopes on the shared store, keys, deployment guards and operations evidence | IA-015; IA-007/012/014 control owners | 26; 17 C6/C7; port and adapter already landed in 19 |
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
| 3 — structural review, then one human decision | **Partly taken (2026-09-06): C1, C3 and C7 accepted — C3 and C7 for synthetic data only; C2, C4, C5 and C6 not.** C4 and C6 still carry amendments A2/A3 and A3/A4 | [ADR-004 decision records](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#decision-record--2026-09-06-second-c3-and-c7) |

**Task 17 is not complete.** Three entries of seven are accepted — C1, and C3/C7 for synthetic data only. `Drafted`
means the text exists and is internally consistent; it is not approval and it is not evidence. For C2, C4, C5 and C6
no behaviour is implemented and no test named in §14 has been written. Amendments A2/A3 and A3/A4 in the decision
record are changes to those proposals, not open questions: each of those blocks must return with its contract
already reconciled, because a contradiction left as a note for the implementer is not a delivered contract.

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

## Task 19 — done 2026-09-06

**Visible outcome met:** one identity can own exactly one Personal tenant with its own protected profile and
document, and neither the tenant nor the document can be doubled by a race. Nothing here is reachable over HTTP;
Task 20 is what exposes it.

| Step | What happened |
|---|---|
| RED | `PersonalIdentityMappingTests` 5 of 5 failing, by name rather than by compiler: "The model does not map CleanArchitecture.Domain.IdentityAccess.People.PersonProfile". Then `PersonalIdentityTests` 7 of 11 failing against skeletons with no guards, and `PersonalDocumentProtectionTests` + `SharedAttemptBudgetTests` 8 of 16 failing with the adapters absent |
| GREEN | `People` domain slice (profile, ownership, document, fingerprint, `NormalizedDocument`, `DataClassification`); four EF configurations; the additive `PersonalIdentity` and `SharedIdentityAttemptBudgets` migrations; `IdentityDocumentProtector` and `IdentityDocumentFingerprintFactory` over the existing Data Protection key ring; `PostgreSqlAttemptBudget` behind `ISharedAttemptBudget` |
| REFACTOR | The timestamp interceptor now stamps profiles as well as tenants and was renamed `OperationalTimestampInterceptor` for what it does. The architecture test that refuses an unclassified Domain slice was answered by classifying `People`, not by relaxing it |

**Commands run, both from the plan.**

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter PersonalIdentityTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PersonalIdentityMappingTests|PersonalDocumentProtectionTests|SharedAttemptBudgetTests|MigrationUpgradeTests"
```

11/11 and 33/33. Whole solution afterwards: Domain 173, Application.Unit 192, Infrastructure.Integration 258,
Application.Functional 401; Debug and Release builds 0 errors.

**Effects proved on real PostgreSQL:** two identities claiming one documentary identity leave one row, and so do a
claim made after the first person's tenant is suspended and a claim made inside a transaction that then rolls back;
only a purge frees the number, and it deletes the fingerprints rather than blanking them; a payload sealed for the
outbox purpose is refused rather than read; absent, too-short, non-Base64 and unversioned fingerprint key material is
refused; exactly the budget is admitted at a threshold and a spent window stays spent for an adapter that never saw
it spent.

**Not claimed.** `SharedAttemptBudgetTests` runs its adapters in one process. That is persistence evidence, not
evidence that a budget holds across service instances or restarts — Task 27 owns that, and this task does not
anticipate it. Fingerprint key material is read from configuration with no default; recording it per environment is
also Task 27's.

### Proposed requirements and the tasks they unblock

Each entry proposes its own requirement numbers. IA-REQ-048 was accepted on 2026-09-06 and is normative in
[SPEC §4](SPEC.md#4-normative-requirements), so it — not IA-REQ-047 — is the highest approved number. IA-REQ-049–058
are allocated only inside the proposal and become real if and when the decision accepts them; 058 was added on
2026-09-06 by amendment A1.

| Entry | Proposed requirements | Amends | Unblocks on acceptance | Still blocked afterwards |
|---|---|---|---|---|
| C1 **(accepted 2026-09-06; implemented)** | IA-REQ-048, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | IA-REQ-003, IA-REQ-004, IA-REQ-005 | 18, **done 2026-09-06**; the registration seam 19/20/23 build on | nothing from this entry: the IA-REQ-003 residual is closed on Task 18's paired-sequence evidence |
| C2 | IA-REQ-049 | IA-REQ-021, IA-REQ-023 | the session half of 21, 22, 23 | the proof half of 21/22, which is C4 |
| C3 **(accepted 2026-09-06, synthetic only)** | IA-REQ-050, IA-REQ-058, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | none; extends IA-REQ-002/044 | 19 and 20, together with C7 | IA-REQ-058's dispute, which needs C4 and lands in 26; real DNI capture, which is the G2 gate; and the §14.3 residual, which was not accepted |
| C4 **(A2/A3 folded in 2026-09-06)** | IA-REQ-051, IA-REQ-052 | IA-REQ-022 and §8, for one named callback route | 21, 22, 23; the recovery part of 26 | live provider registration, which is per-environment |
| C5 | IA-REQ-053 | IA-REQ-047, closing its deferred note | 24, 25 | C4, without which ownership transfer has no proof |
| C6 **(A3/A4 folded in 2026-09-06)** | IA-REQ-054, IA-REQ-055 | makes IA-REQ-020 precise; extends IA-REQ-042 | 26, 27 against synthetic fixtures | live restore release, which needs the external authority |
| C7 **(accepted 2026-09-06, synthetic only)** | IA-REQ-056, IA-REQ-057, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | strengthens IA-REQ-019 to shared, fail-closed state | 19's classification stamp and budget store, 20's public claim budget; 26 and 27 stay blocked on C6 | real personal data, and production — separate gates in 28 |

A declined or amended entry blocks only its own consumers. Tasks 1–16 and their recorded `Review` states are
untouched by this package.

**Amendments A1–A5 were folded into C3, C4, C6 and C7 on 2026-09-06**, together with two contradictions outside that
list (C2 versus C4 on the session route, C5 versus C6 on the last-administrator refusal). Every one of those entries
now states a single contract: no `Gap`, no "fragments disagree", nothing left for an implementer to decide. That
changes what is being asked, not whether it has been answered — C2 through C7 are still proposals awaiting one
decision each, and no task after 18 is `Ready`.

**Dependencies realigned to those contracts, 2026-09-06.** Three consequences of the reconciled text were not
reflected in the roadmap and now are. Task 19 needs C3 **and** C7, because the classification stamp on every stored
row and the budget that bounds a document claim are both persistence and neither can be added afterwards. The shared
attempt-budget port, adapter, table and migration move from Task 27 into Task 19, so the limit exists before Task 20
exposes Personal registration; Task 27 keeps the remaining scopes and all the distributed evidence. And IA-REQ-058's
dispute — both halves — moves to Task 26, because the owner's half requires a live C4 proof that does not exist
before Task 22; Tasks 19 and 20 record a document and never offer a way to correct one. Task 19 stays domain and
persistence with no public route, and every public flow stays in the task that owns it.

**Taken 2026-09-06: C3 and C7 accepted together, for synthetic data only.** Task 19 is `Ready`, and Task 20 becomes
`Ready` once Task 19 is verified. Nothing after Task 20 is authorized. C2 and C4 unblock the 21–23 line whenever they
are decided; C5 and C6 depend on C4 and come after. Real personal data, production, and the §14.3 residual were
withheld and are not part of this acceptance.

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
