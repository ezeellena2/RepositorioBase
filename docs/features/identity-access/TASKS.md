# Identity Access — Tasks

**Status (2026-09-07).** Tasks 1–25 are implemented and recorded below, each with its own dated entry and its own
evidence. Task 17 is complete: every entry C1–C7 is decided, the last of them on 2026-09-07 (see
[ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#decision-record--2026-09-07-c6-with-one-withdrawal)).
Tasks 26–28 are the remaining continuation work.

**Reading order, because parts of this file are older than others.** The dated task entries and the ADR decision
records are the authority. Several summaries written earlier in the feature's life — including the paragraphs this
one replaces, and the roadmap table below — described a baseline that later work overtook: IA-010 (Personal/B2C and
AR/DNI), IA-011 (recovery, change and session management) and IA-013 (Google OIDC and linking) were subsequently
implemented by Tasks 19–23, and IA-015 is Task 27's subject. Where an undated summary and a dated record disagree,
the dated record stands. The historical entries themselves are left as they were written; correcting a summary is
not the same as rewriting what happened.

The [plan runs Tasks 17–28](../../superpowers/plans/2026-08-31-identity-access-foundation.md#continuation-to-local-b2bb2c-functional-completion).
Real-PII and production/reference compliance remain separate gates, so local functional closure authorizes no
deployment and certifies nothing.

## Continuation roadmap mapping — 17–25 done, 26–28 remaining

| Plan task | Work | Tracking | Dependency/approval boundary |
|---|---|---|---|
| 17 | Local contracts, reference revision and explicit adoption/deviation mapping | IA-001; IA-010/011/013/015 | Human acceptance of proposed C1–C7; documentation only |
| 18 | Registration state-level privacy and CUIT reservation | IA-006; IA-009 evidence | **Done 2026-09-06.** C1 accepted; IA-REQ-003/004/005 amended and IA-REQ-048 added in SPEC §4 |
| 19 | Personal ownership and protected AR/DNI persistence, plus the shared attempt-budget store | IA-010; IA-004 persistence | **Done 2026-09-06.** 18 (done); C3 and C7 accepted for synthetic data |
| 20 | Personal signup, own profile and context React journey | IA-010; IA-009 evidence | **Done 2026-09-06.** 19, inheriting its C3/C7 approval; no further entry |
| 21 | Own sessions and recent reauthentication seam | IA-011; IA-007 sessions | 17 C2/C4; password proof now, Google proof in 23 |
| 22 | Password recovery and change end to end | IA-011; IA-009 evidence | 21; 17 C4 |
| 23 | Google login, explicit linking and last authenticator | IA-013; IA-009 evidence | 20–22; 17 C4; live provider activation separate |
| 24 | Custom Organization role administration | IA-005 continuation; IA-009 evidence | 17 C5 |
| 25 | Membership administration, ownership transfer and invitation lifecycle | IA-005/008 continuation; IA-009 evidence | 21/24; 17 C5 |
| 26 | Lifecycle, MFA recovery, retention executor, restore admission guard and both halves of the documentary dispute | IA-011/012/015; existing event owners | **Unblocked 2026-09-07**: C6 accepted with withdrawal E1 |
| 27 | Remaining budget scopes on the shared store, keys, deployment guards and operations evidence | IA-015; IA-007/012/014 control owners | **Unblocked 2026-09-07**: 26; port and adapter already landed in 19 |
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
| 3 — structural review, then one human decision | **Complete. C1, C3, C7 accepted 2026-09-06; C2 and C4 the same day; C5 with amendments D1–D4; C6 on 2026-09-07 with withdrawal E1.** Every acceptance is for synthetic data only | [ADR-004 decision records](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#decision-record--2026-09-06-second-c3-and-c7) |

**Task 17 is complete (2026-09-07).** All seven entries are decided and every one of them is accepted for
synthetic data only. The amendments are part of what was accepted rather than open questions: A1–A5 on C3/C4/C6/C7,
D1–D4 on C5, and E1 on C6, which withdraws the provider reactivation half and C4's `Recovery` purpose because a
provider round trip cannot demonstrate that a person is present. Acceptance is a contract, not evidence: what each
entry is worth is recorded in the task entry that implemented it.

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

## Task 20 — done 2026-09-06

**Visible outcome met:** a person chooses what they are registering, completes a personal signup through the
delivered confirmation link, or adds a personal context to the account they already have; then reads their profile
with the document masked and edits the two names it allows. Switching contexts never mixes permissions.

| Step | What happened |
|---|---|
| RED | `PersonalJourneyTests` failed to compile against three absent namespaces, then drove the whole slice |
| GREEN | `PendingPersonalIntent` and its additive `PersonalRegistrationIntent` migration; `RegisterPersonalCommandHandler`; a third branch in `ConfirmEmailCommandHandler`; `CreatePersonalContextCommandHandler`; the profile query and command; `PersonalEndpoints`; `ChooseContextPage`, `PersonalPages` and their routes and navigation |
| REFACTOR | `PersonalContextFactory` is the one place the graph is built, so the mailed path and the proved path cannot drift apart. The profile page derives its form from the response rather than syncing state in an effect, which is what the lint rule was pointing at |

**Commands run, both from the plan.**

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PersonalJourneyTests|IdentityContextPermissionTests"
npm test --prefix src/Web/ClientApp -- PersonalPages.test.jsx AppRoutes.test.jsx IdentityProvider.test.jsx
```

12/12 and the client suite. Whole solution afterwards: Domain 173, Application.Unit 192,
Infrastructure.Integration 258, Application.Functional 413, browser acceptance 21; client 112 with lint clean;
Debug and Release builds 0 errors.

**Contracts proved, not just implemented:** an anonymous signup writes one intent and one message and no fingerprint
for either a known or an unknown address; a delivered confirmation creates the whole graph once and a replay adds
nothing; an already-registered identity adds Personal with the password hasher never invoked; a duplicate document
and an identity that already owns a context receive the same `409` with the same detail; the fourth claim in a day
is `429`; an unreachable budget store is `503` with `Retry-After: 30`; a stranger is told `personal_profile_not_found`
rather than shown somebody else's; no document, address or password reaches an audit record, an outbox payload or a
log; and two concurrent creations by one identity leave exactly one context.

**Not built, deliberately.** `correctionAvailable` is `false` and no dispute route exists: IA-REQ-058 needs C4's
recent proof and lands in Task 26. The screen says so rather than offering a control the product cannot serve.

## Task 21 — done 2026-09-06

**Visible outcome met:** an identity sees its own devices, ends one or all the others, and proves itself again
before doing so. Signing in on a second device no longer signs the first one out.

| Step | What happened |
|---|---|
| RED | `SessionManagementTests` 9 of 10 failing, including the coexistence assertion against the old revoke-everything sign-in |
| GREEN | `SessionReference` and `SessionDeviceLabel`; `RecentIdentityProof` and `IdentitySecurityState` with the additive `IdentityReauthentication` migration; `SessionIssuer` and `SessionLock`; the three session-management requests and `ReauthenticateCommand`; `SessionsPage` |
| REFACTOR | Four existing tests asserted the behaviour C2 replaced and were rewritten to it, each naming what changed. The race barrier's stage is `Eviction` now, because the cap eviction is the write it interrupts |

**Commands run, both from the plan.**

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionManagementTests|ReauthenticationTests|SessionConcurrencyTests|SessionTests"
npm test --prefix src/Web/ClientApp -- SessionsPage.test.jsx IdentityProvider.test.jsx
```

102/102 and the client suite. Whole solution afterwards: Domain 173, Application.Unit 192,
Infrastructure.Integration 258, Application.Functional 431, browser acceptance 21; client 116 with lint clean;
Debug and Release builds 0 errors.

**Honest about the RED.** `SessionManagementTests` drove the whole slice and failed 9 of 10 first.
`ReauthenticationTests` did not: it was written after the proof store existed and passed on its first run. It pins
semantics that file only exercised indirectly — single use, session binding, action binding, expiry, and
invalidation by the security version — and it is recorded here as coverage rather than as a RED that happened.

**A property of the acceptance suite, not of this change.** `Web.AcceptanceTests` runs against the development
database and resets nothing, while asserting a cold start the Platform bootstrap will never repeat. It is 21/21 on
a fresh database and fails on a second run against the same one. That was true before this task; making the suite
repeatable belongs to Task 28.

## Task 22 — done 2026-09-06

**Visible outcome met:** a person who cannot sign in asks for a link from the sign-in screen, follows the one that
is actually delivered, and chooses a new password. A person who can sign in changes it from their account after
proving it is still them.

| Step | What happened |
|---|---|
| RED | `PasswordLifecycleTests` did not compile against the absent `Credentials` domain slice, then drove every case |
| GREEN | `PasswordResetRequest` with the additive `PasswordRecovery` migration and its one-live-link partial index; `IdentityCredentialService` over ASP.NET Identity's hasher and validators; the recovery, reset and change handlers; `PasswordRecoveryDeliveryHandler`; `PasswordEndpoints`; the three React pages and the sign-in link |
| REFACTOR | `CredentialSessionEffects` is the one place a credential change touches sessions, because the reset and the change differ only in which session survives |

**Commands run, all three from the plan.**

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PasswordLifecycleTests|SessionConcurrencyTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxDeliveryTests
npm test --prefix src/Web/ClientApp -- PasswordPages.test.jsx
```

9/9 plus the session races, 28/28, and 5/5. Whole solution afterwards: Domain 173, Application.Unit 192,
Infrastructure.Integration 259, Application.Functional 440, browser acceptance 21; client 121 with lint clean;
Debug and Release builds 0 errors.

**Contracts proved:** a recovery request answers the same for an address with an account and one without, and only
the first writes anything; the delivered link sets the password, revokes every session and issues none; a spent
link answers `invalid_credential_token`; asking again kills the older link rather than leaving two that work; a
change spends its proof, revokes the others, rotates the acting session into a new row and signs the caller into
it; a refused password changes nothing; and no token, address or password reaches a payload, an audit record or a
log.

**Named limitation.** The "recovering a credential lifts no restriction" case is proved against lockout, the
restriction that exists today. Administrative suspension is C6's contract and Task 26's work, so the stronger form
of that assertion is not claimed here.

## Task 23 — done 2026-09-06

**Visible outcome met:** a person signs in with Google from the sign-in screen, or links and unlinks a Google
account from `/identity/external`. A matching email address takes over nothing, and no action removes the last
way into an account.

| Step | What happened |
|---|---|
| RED | `GoogleOidcTests` against `ControlledOidcProvider` — a provider that really publishes discovery and JWKS, mints codes bound to the nonce and PKCE challenge, and refuses a bad exchange |
| GREEN | `ExternalAuthorizationRequest` with the additive `ExternalProviderLogins` migration; `GoogleOidcConfiguration` over the framework's OpenID Connect handler; `ExternalIdentityService` over `AspNetUserLogins`; `ExternalCallbackRecorder`; `ExternalLoginEndpoints`; `ExternalAccountsPage` and the sign-in button |
| REFACTOR | The routes, the single completion, the list shape and the callback path were brought onto the SPEC §14.4 contract after the behaviour was green, and the three places the written contract could not be built as written are corrected in that section rather than left to drift |

**Commands run, both from the plan.**

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "GoogleOidcTests|ReauthenticationTests|SessionManagementTests"
npm test --prefix src/Web/ClientApp -- ExternalAccountsPage.test.jsx AppRoutes.test.jsx
```

41/41 and 25/25. Whole solution afterwards: Domain 173, Application.Unit 192, Infrastructure.Integration 259,
Application.Functional 463, browser acceptance 21; client 134 with lint clean; Debug and Release builds 0 errors.

**Contracts proved:** a verified provider account nobody has claimed becomes an identity with a session, no
password, no tenant and no membership; the same account coming back is recognized rather than duplicated; a
verified address that already belongs to a local account answers `409 external_login_conflict` and links nothing;
an unverified address signs nobody in; a tampered state spends no code and validates no handoff; an impostor
issuer, a wrong audience, a signature from a key the JWKS does not carry, an expired token and a replayed nonce
each settle the handoff as failed and reach no application decision; a replayed callback produces no second
session; linking needs consent, a live proof and a confirmed identity, and refuses a subject somebody else owns, an
address belonging to another local identity, and a second link for a provider already linked; unlinking spends its
own proof and leaves the password working; the last authenticator cannot be removed, proved on an identity whose
only proof comes from the provider itself; two identities racing for one provider account leave exactly one link;
and neither the authorization code nor the client secret reaches a log at Information or above, a response body or
the address bar.

**Named limitations.**

- **Verified against the protocol is not verified against Google.** Every case runs against a controlled provider.
  No request has ever been made to `accounts.google.com`. Activating the real provider needs an OAuth client, its
  secret and the exact redirect URI registered in the operator's own Google account — steps only the account owner
  can take, written out in [RUNNING-LOCALLY.md](RUNNING-LOCALLY.md#signing-in-with-google). Until then the button
  and the account screen exist and answer `invalid_external_login`, because the middleware is not registered at
  all when no client is configured.
- **The `Recovery` purpose is not built.** It is C6's contract and Task 26's work; approving C4 did not authorize
  it. `ExternalAuthorizationPurpose` carries no `Recovery` member, so it cannot be reached by accident.
- **The acceptance suite still needs a reset database**, unchanged from Task 21 and Task 22 and recorded there as
  Task 28's.

## Task 23 follow-ups — done 2026-09-06

Two gaps in what Task 23 shipped, both found by looking at the delivery rather than at the tests.

**The screen was unreachable.** `/identity/external` was routed and named nowhere, so only somebody who already
knew the URL could open it — for a self-service screen, the same as not shipping it. `NavMenu.test.jsx` now pins
the whole signed-in set, so the next screen cannot ship orphaned either.

**The screen could not be honest.** `GET /api/identity/credentials` — the `{ hasPassword, passwordUpdatedAt }` row
of SPEC 14.4, unimplemented by Task 22 and recorded above as a gap — is now built, with `PasswordUpdatedAt` added
to `IdentitySecurityState` by the additive `PasswordChangeStamp` migration and stamped only by the reset and the
authenticated change. An identity whose only way in is a provider is now told so, and shown an explanation instead
of an unlink button whose one possible answer is `last_authenticator_required`; it is also no longer asked for a
password it does not have.

**The devices screen was a dead end for those accounts.** `/identity/sessions` disabled both revoke buttons until
a password was typed, so an identity created through a provider could see its other devices and end none of them.
It now says so and points at the mailed reset — the one way to obtain a password that needs no proof, which is
exactly why it is the way out.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "OwnCredentialsTests|PasswordLifecycleTests|GoogleOidcTests"
npm test --prefix src/Web/ClientApp
```

`OwnCredentialsTests` additionally proves the answer carries exactly two members, needs a session, and does not
report a provider link as a password change. Whole solution afterwards: Domain 173, Application.Unit 192,
Infrastructure.Integration 259, Application.Functional 467; client 140 with lint clean.

**Named limitation.** A provider-only identity still cannot obtain a recent proof *through its provider* from the
devices or accounts screens — the `Proof` purpose is built and tested end to end on the server, but no screen
starts one, because with a single provider configured every action it would authorize is either unreachable or
has the mailed reset as a working answer. It becomes worth wiring when a second provider exists.

## Task 23 adversarial review — done 2026-09-06

Four lenses read the shipped provider-login surface for defects a passing test would not show — protocol,
authorization and linking, persistence and races, leakage and client — and every candidate was then given to a
separate reader whose job was to refute it. Twelve of the sixteen candidates were refuted. **The four that
survived were real, and each fix is pinned by a test checked against a reverted fix rather than trusted because
it went green.**

| Defect | What it cost | Fix |
|---|---|---|
| The lock was per identity, the invariant per provider subject | two identities racing one Google account: the loser met `500`, not `external_login_conflict`. The original race test only counted the winners, so it passed over this | `IExternalSubjectLock`, a third advisory space keyed on (provider, subject), taken after the identity lock |
| A `Proof` round trip asked the provider for nothing | a "recent identity proof" obtained through Google proved an unlocked browser, not a present person | `prompt=login` on the `Proof` challenge, read from the purpose sealed at start |
| A link audited its session revocations as `password_changed` | an investigation would be told a password changed when none did | the reason is `authenticator_linked` |
| A callback failure cleared the handoff cookie unconditionally | any stranger posting to the callback path could cancel a round trip somebody else had started | cleared only when the failure names a handoff |

Two more, in the client, were graded cosmetic by the verifier and fixed anyway because the reasoning holds:
`ExternalReturnPage` chose whether to rotate the antiforgery pair from the `?outcome` query rather than doing it
after every completion — a mismatch left the transport holding a token whose cookie the server had deleted — and
the account page announced "your other devices have been signed out" purely because the URL said so. Both now
report only what they saw the server do. A third, `safeReturnUrl` accepting `/\evil.test` and resolving it to
`https://evil.test/`, was **confirmed**: the guard rejected a leading `//` but not the backslash the URL parser
treats the same way. It now resolves the candidate and compares origins, which cannot be spelled around.

The account page also no longer guesses which providers exist: `GET /api/identity/external` reports what the
deployment configured, so a deployment without a Google client offers nothing instead of spending a password on
a proof for a round trip that cannot start.

## Task 24 — done 2026-09-06

**Visible outcome met:** an administrator creates, renames, edits and retires custom roles from `/roles`, sees
what each one confers, and watches authorization change on the very next request — with no role-name checks
anywhere.

**The finding that had to be fixed first.** Registering an Organization made you its `Owner` of a role created
with **no permissions at all**: `RegistrationInitialRoleProvisioner` wrote the role and the assignment and zero
`RolePermission` rows, and effective permissions come only from those rows. A real registered owner could not
invite anybody, and no test noticed, because every test granted itself the permissions it needed. C5's ceiling
makes that unrecoverable from inside the product — an actor may grant only what it holds — which is why amendment
D1 exists. `OrganizationOwnerAuthorityTests` now pins what a registration actually produces.

| Step | What happened |
|---|---|
| RED | `RoleAdministrationTests` over the production HTTP pipeline; `OrganizationOwnerAuthorityTests` for what a registration produces; `OrganizationOwnerCatalogTests` for D1's decision |
| GREEN | `PermissionDefinition` gained the positional `OrganizationOwner` answer; `PermissionCatalogSynchronizer` backfills existing owners; `IRoleAdministrationStore` and `RoleAdministrationStore`; `RoleRequests`/`RoleHandlers` with the ceiling, the floor and offer cancellation; `RoleEndpoints`; `RolesPage` |
| REFACTOR | The floor is counted from flushed state and refuses by rolling the write back, since a check made after a write can be honoured no other way |

**Commands run, all four from the plan.**

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "RolePermissionMappingTests|MigrationUpgradeTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RoleAdministrationTests|RoleMembershipAuditTests"
npm test --prefix src/Web/ClientApp -- RolesPage.test.jsx
```

Whole solution afterwards: Domain 175, Application.Unit 196, Infrastructure.Integration 261, Application.Functional
489, browser acceptance 21; client 166 with lint clean; Release build 0 errors.

**Contracts proved:** a created role confers exactly what was asked for; nobody can put a permission into a role
they do not hold themselves, while removing one they do not hold is allowed because narrowing is not granting; the
catalogue tells each caller which codes they could grant, and it is the same list for everybody; a system role is
neither editable nor retirable; a stale `version` answers `role_concurrency_conflict` and changes nothing;
another tenant's role is `404` rather than `403`; a change that would leave nobody holding both `roles.manage` and
`members.manage` answers `last_administrator_required` and leaves the role exactly as it was; every write spends a
recent proof and is refused without one; widening a role withdraws every offer that named it and audits
`role-widened`, while narrowing leaves those offers standing; retiring withdraws them too, is idempotent, and the
retired role grants nothing on the very next request.

**Evidence rather than green.** The ceiling and the floor were each verified by removing the rule from the handler
and confirming the test fails, then restoring it. So was D1's backfill.

**Named limitations.**

- **`AssignableRoleCatalog` was not built.** The plan reserved a migration for catalogue metadata; D1's answer is a
  code-owned catalogue field and needs no column, so no migration was written. Nothing else in Task 24 needed one.
- **Members, ownership and the invitation lifecycle are Task 25.** C5 covers them and they are approved; they are
  simply not built yet, so `/roles` is the only C5 screen today.
- **`last_administrator_required` on C6's deactivate route stays unreachable**, because that route is C6's and C6
  is not accepted.

## Task 25 — done 2026-09-07

**Visible outcome met:** an administrator opens `/members`, sees who is in the organization and which one owns
it, changes what a member holds, suspends, reactivates and removes people, and hands the organization over
deliberately; from `/members/invite` they now offer roles by name, see every standing offer, and reissue or
withdraw one.

**What was missing rather than broken.** `ResendInvitationCommand` and `CancelInvitationCommand` had existed since
Task 21 and were reachable only through MediatR — from tests, never from the product. Task 25's outcome says
"from the real interface", so the two routes were added and the invite screen was rebuilt around them: role
checkboxes instead of a box asking a person to type GUIDs, and the list of offers next to the form that makes
them.

| Step | What happened |
|---|---|
| RED | `MembershipLifecycleTests` for the transitions and the ownership rule; `MembershipAdministrationTests`, `OwnershipTransferTests` and `MembershipAtomicityTests` over the production HTTP pipeline; two contention cases in `ResendAndCancelInvitationTests`; `MembersPage.test.jsx` and `InviteMemberPage.test.jsx` |
| GREEN | `TenantMembership.Reactivate`/`Revoke`/`Reinstate`; `Tenant.OwnerMembershipId` with `TransferOwnershipTo` and the `OrganizationOwnership` migration; `IMembershipAdministrationStore` and its store; `MembershipRequests`/`MembershipHandlers`; `MembershipEndpoints` and the two invitation routes; `MembersPage`, the rebuilt `InviteMemberPage` |
| REFACTOR | `OrganizationScenario` extracted so the role and member suites share one organization harness; the assignment rollback hook added to the save interceptor; the two invitation races moved from ordering to real contention |

**Commands run, all three from the plan.**

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter "InvitationTests|RolePermissionTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "MembershipAdministrationTests|OwnershipTransferTests|ResendAndCancelInvitationTests|RoleMembershipAuditTests"
npm test --prefix src/Web/ClientApp -- MembersPage.test.jsx
```

Whole solution afterwards: Domain 191, Application.Unit 196, Infrastructure.Integration 261,
Application.Functional 527, browser acceptance 21; client 192 with lint clean; Release build 0 errors.

**Contracts proved.** A member list says who is in the organization, what each holds and which one owns it, and
carries a name and an address and nothing else about the person (D4). Handing somebody a role is compared against
the actor's whole conferred set, not the difference, so a role conferring one code the actor lacks is refused
even when everything else in it is theirs. Every role change spends a proof and is refused without one (D2). The
owner's own membership cannot be suspended or revoked, and transferring first is what makes them removable — the
one way out of a rule that would otherwise be a trap. Ownership moves only for the current owner, only to an
active member of the same organization, only with a live proof and the row's own `version`; another organization's
membership is `404` rather than `403`; a repeat of a transfer that already holds answers the same `204` and
records nothing new; and being refused for not being the owner spends no proof, proved by using the surviving one.
A transfer is audited and notified through one outbox message carrying two membership ids, no address and no
`OutboxSecret`. A failure after the assignment rows have really landed takes the rows, the audit and the
authorization version back together, while the refusal that never ran leaves exactly `{ code, outcome }` through
the denial writer's own scope. Withdrawing and reissuing one offer at the same time settle it once, and widening a
role while its offer is being reissued leaves no live offer of it and no deliverable token — whichever of the two
commits first.

**Evidence rather than green.** Two of the first cases written were wrong and said so: one read the member list as
an owner whose administration the case had just taken away and got a truthful `403`, and one built a "wider" role
out of permissions the actor already held, so the ceiling had nothing to refuse. Both were fixed rather than
relaxed. The widening-versus-reissue race was left honest in the same way: the widening really can lose on
`Tenants.xmin`, so the test asserts the rule holds on the retry instead of pretending the loser cannot exist.

**Named limitations.**

- **`Reinstate` is unreachable.** The transition exists and is tested in the Domain; coming back after a
  revocation is the acceptance of a fresh invitation, and the rest of that path is C6's and Task 26's.
- **No route reads a single member.** `GET .../members/{membershipId}` was never in C5's table; the list is the
  read, and the store's `FindAsync` exists to answer the write path.
- **Ownership transfer sends no mail yet.** The outbox message is written in the same transaction, as
  IA-REQ-027 requires; `identity.ownership.transferred.notice.requested` has no delivery handler, so the notice is
  recorded and not yet delivered.
- **C6 is still not accepted**, so deactivation, reactivation and the lifecycle half of membership stay unbuilt,
  and `last_administrator_required` on C6's deactivate route stays unreachable.
- **Reopened and closed again on 2026-09-07.** A review of Tasks 21–25 found that four of this task's own rules
  were stated but not enforced — the membership version, the return of a revoked member, the members screen under
  `members.read` alone, and the continuation of every directory. They are fixed and pinned in
  [the remediation section](#tasks-2125-review-remediation--done-2026-09-07); this task is closed on that evidence.
- **The plan's file list was a forecast and three entries did not survive it.** `TransferOwnership.cs` and
  `TransferOwnershipHandler.cs` are not separate files: the transfer is one of five membership requests and lives
  with them, because splitting the one that shares the floor check and the tenant guard from the four that use
  the same rules would have meant copying them. `MembershipRole.cs` and `Invitation.cs` needed no change —
  assignment is a whole-set replacement over existing rows and the invitation aggregate already had every
  transition this task drives.

## Task 26 — in progress

C6 covers five things that share a task number and almost nothing else: identity lifecycle, Platform MFA recovery,
retention maintenance, the documentary dispute and the restore admission guard. They land as separate units with
separate evidence, because a change that mixes them is one nobody can review.

### Unit 26.1 — identity lifecycle: parking your own account and coming back (done 2026-09-07)

**Visible outcome met:** a person proves their password, parks their own account, and every door it had open shuts
at once — the cookie that asked, the cookie on the other device, and the password itself. Later they ask for a
link at the address they own, and come back with that link and their password.

**The state is now the answer, not an assembly of flags.** "May this identity act" used to be computed at each
call site from `EmailConfirmed` and an unexpired lockout. `IdentityAccountStatus` was an enum with no column and
no reader. It is now a real column on `AspNetUsers` — `PendingConfirmation`, `Active`, `SelfDeactivated`,
`AdministrativelySuspended`, `Closed`, checked by the database — and `IdentityAccount.IsActive` derives from it,
so there is one answer and every existing reader inherits it (IA-REQ-054). The migration maps each existing row
onto the answer it already gave, and the column's default is the least an account may do, so a row inserted by
something that has never heard of the column is one nobody can sign into.

**Withdrawal E1, in the code.** `ReactivateAccountCommand` carries a ticket and a password and nothing else.
There is no `providerProofToken` and no `ExternalAuthorizationPurpose.Recovery`. The ticket deliberately carries
no security version — unlike `PasswordResetRequest`, which needs one because it *replaces* a credential — since
stamping it would refuse the person who did exactly what E1 tells a provider-only identity to do: set a password
first, then come back.

| Step | What happened |
|---|---|
| RED | `IdentityLifecycleTests` did not compile against the absent `ProofActions.AccountDeactivate`, then drove ten behaviours: both cookies dying, the password that stops opening anything, the way back, the ticket that is worth nothing without the password, the spent ticket that answers exactly what a forged one does, the reissue that kills its predecessor, the neutral 202 for every address, the password reset that is not a way out of being parked, and the two refusals that stop somebody stranding an organization or the Platform. |
| GREEN | `AccountReactivationRequest`, the persisted status with its conditional `TryTransitionAsync`, `VerifyPasswordAsync`, three handlers, three routes, and the `IdentityAccountLifecycle` migration. |
| Not vacuous | With the sign-in gate reverted to `!EmailConfirmed`, three of the ten fail — including a parked account signing straight back in. Restored, all ten pass. |
| Verified | Functional 562/562, Application unit 197/197, Domain unit 191/191, Infrastructure integration 261/261, client 212/212. |

**Two premises were corrected, no assertion was.** `SessionTests.SetAccountStateAsync` and
`InviteMemberTests.UnconfirmAsync` unconfirmed an account by writing the flag alone. Under IA-REQ-054 that
describes an account this system has no way to be in — unconfirmed by flag, usable by state — so both now move
the state as well. What each test asserts is untouched.

**What this unit does not do.** It does not suspend anybody: `AdministrativelySuspended` and `Closed` exist in the
state set and the database constraint, and nothing writes them yet. That is unit 26.2, which also adds the column
recording the state a suspension interrupted, so reactivation can restore it rather than assume `Active`.

### Unit 26.2 — administrative suspension, and the state a suspension interrupted (done 2026-09-07)

**Visible outcome met:** a Platform operator stops an account under a reason from a closed set, and the account
loses its sessions and its password at once. Later they let it go, and it lands back where it was.

**The decision this unit had to take, stated rather than assumed.** SPEC's route table names
`ReactivateIdentityRequest { expectedStatus, acknowledgeSelfDeactivation }` without saying what the second field
does. Two readings existed: the operator confirming they know the account returns to the person's own parked
state, or the operator overriding that decision and forcing `Active`. **Taken: the first.** The route table also
says reactivation restores "the pre-disable state rather than `Active` unconditionally", and an operator lifting
their own suspension is not entitled to undo a choice that was never theirs. The flag exists so nobody believes
they restored somebody's access when they did not — without it, the request is refused rather than silently
landing somewhere the operator did not expect. The audit outcome C6 already names for this,
`reactivated_over_self_deactivation`, is what it writes.

`StatusBeforeSuspension` is the column that makes it knowable, with a database constraint tying it to the state:
it is set exactly while the account is suspended, and only to a state a suspension could have started from.

**The permission, and the install it would otherwise have missed.** `platform.identities.manage` is new and
distinct from both `platform.identities.read` and `platform.tenants.manage` — an operator who may suspend a
company is not thereby entitled to suspend a person. The bootstrap ceremony that grants Platform its system
permissions runs **once per database**, so a new code reaches a fresh install and no existing one. The migration
therefore backfills the grant onto every Platform system role that already holds `platform.tenants.manage`, and
writes the catalogue row itself because migrations run before the startup synchronizer that normally owns it.
Both statements are idempotent.

| Step | What happened |
|---|---|
| RED | `PlatformIdentityLifecycleTests` did not compile against the absent `Platform.Identities` namespace, then drove eleven behaviours: the account losing every way in, the reason living in the audit and not in the directory, the restore to the person's own state, the unacknowledged lift being refused, the last Platform owner, the expected-state precondition, the `Closed` tombstone, the unknown identity, the session that never stepped up, and the two halves staying apart — a ticket minted before a suspension does not survive it, and a suspended account asking for the public way back is answered and sent nothing. |
| GREEN | `IdentitySuspensionReason`, `StatusBeforeSuspension` with its constraint, `IIdentityLifecycleStore` and its two conditional writes, two commands, two Platform routes, and the `IdentityAdministrativeSuspension` migration with its grant backfill. |
| REFACTOR | The disable effects — sessions, proofs, outstanding links, the Platform step-up — moved into `IdentityLifecycleEffects`, shared by parking and suspension, because two copies of that list would be two chances to forget the same entry. The Platform last-owner question moved with them. |
| Not vacuous | With the restore forced to `Active`, two of the eleven fail. Restored, all eleven pass. |
| Verified | Functional 573/573, Application unit 197/197, Domain unit 191/191, Infrastructure integration 261/261 (`MigrationUpgradeTests` included, which is what runs the backfill from the previous version), client 212/212. |

**Still not done in Task 26:** MFA recovery, the retention executor, the documentary dispute and the restore
admission guard. `Closed` is still written by nothing — it is reached by an executed erasure, which is unit 26.4.

### Unit 26.3 - Platform MFA recovery (done 2026-09-07)

**Visible outcome met:** an operator who lost the authenticator holding their second factor spends one recovery
code, is handed a replacement secret and a fresh set of codes once, and is back to where they were - after
proving the new factor, which nobody has yet.

**Two places SPEC's route table had to be read rather than copied, both recorded here.**

- *`proofToken`.* The table writes the C4 proof as a request field. It is not one, for the same reason no other
  sensitive route has one: C4's proofs are server-side rows spent by identity, session and action, and nothing the
  client holds names one. `ProofActions.PlatformMfaRecover` is its own action, so a proof bought to change a
  password does not pay for replacing a second factor - which is what the closed action set is for.
- *"no active Platform tenant".* Read as a prohibition first, and that was wrong: signing in selects the only
  tenant an operator belongs to (`SessionIssuer` does it when there is exactly one), so refusing there would make
  the route unreachable for precisely the person it exists for. The clause is an absence from the requirement
  list - every other Platform change needs an active tenant *and* a step-up, and this one cannot - so the
  permission stays application-scoped and no tenant is checked either way. **The test caught this**, which is why
  it is recorded rather than shipped: `Being_signed_into_Platform_is_not_a_bar_because_that_is_where_the_person_already_is`
  now pins the corrected reading.

**The replacement is in place, on the one row.** `TryRecover` is gated on `Status == Active` and one unspent code;
it swaps the secret, replaces the whole code set, and clears the step-up evidence. Status, `VerifiedAt` and
`RecoveryAcknowledgedAt` are untouched, because recovery does not restart the enrollment gates - there is no
`Retired` status and no second row, since a second row would be a second way in. "Spent exactly once" is enforced
by the row's existing `xmin` token rather than by the consumed marker, because the spent code disappears with the
set it belonged to.

| Step | What happened |
|---|---|
| RED | `PlatformMfaRecoveryTests` did not compile against the absent `RecoverPlatformMfaCommand`, then drove ten behaviours: the replacement working and the lost authenticator not, the old step-up dying with the factor it belonged to, a spent code, a sibling code from the replaced set, a missing proof, a proof bought for another action, being signed into Platform, an enrollment nobody finished, the attempt budget, and two devices racing. |
| GREEN | `PlatformMfaEnrollment.TryRecover`, `RecoverPlatformMfaCommand` and its handler, `POST /api/platform/mfa/recover`, and the `platform_mfa_concurrency_conflict` answer. No migration: the shape did not change. |
| Not vacuous, on the second attempt | Removing `ForgetStepUp()` left all ten passing, because the step-up in that test lived on a different session from the one recovering - the test was passing for the wrong reason. Rewritten to step up on the very session that then recovers, and to check it could change Platform a moment earlier; removing the clearing now fails it, and restoring it passes. |
| Verified | Functional 583/583, Application unit 198/198, Domain unit 191/191, Infrastructure integration 261/261, client 212/212. |

**The race is the real one.** A proof is single-use per session, so two recoveries from one session cannot both be
paid for - the honest shape is the same person on two devices, each signed in, each holding its own proof, each
spending its own code. That needs two cookies, so this one test goes over HTTP. One factor exists afterwards and
the device that lost is told the row moved.

**Deferred to Task 27, not forgotten.** SPEC also gives this route `503 service_unavailable` when the shared
budget store is unreachable (IA-REQ-057). The current limiter has no way to say "unreachable" as distinct from
"exhausted", and fail-closed behaviour is Task 27's subject for *every* budget rather than this one alone. It is
listed there, not here.

### Unit 26.4a - the retention policy, and legal holds (done 2026-09-07)

**Visible outcome met:** an operator reads what this deployment's retention policy actually says, and places a
hold that stops one person's data being erased - which is all a hold does. The person it names notices nothing:
they sign in, their account state is unchanged, and no authorization decision anywhere reads the hold.

**A policy is configuration, and this unit contains no number.** `ConfiguredRetentionPolicy` reads
`IdentityAccess:RetentionPolicy` and every refusal answers the same way - no policy. A missing section, a missing
identifier, a category outside the closed set, a period that is not an ISO-8601 duration: all of them leave the
deployment with nothing, and a deployment with nothing performs no destructive action in either personal-data
mode. That is the only direction this class can be wrong in, and it is the safe one. A category the parser cannot
read is dropped rather than completed, because inventing the missing line would be this system writing policy.

**Amendment A4, made structural.** `RetentionLegalHold` lives in its own domain slice, and the architecture guard
now records it as one nothing older may depend on. That is what "a hold is not an account state" means when it is
enforced rather than asserted: there is no path from the lifecycle to this type.

| Step | What happened |
|---|---|
| RED | `PlatformRetentionTests` did not compile against the absent `Platform.Retention` namespace, then drove eighteen cases over the real routes: the empty policy, the configured one read back exactly, an unreadable period, an unknown category, the factor-proof gate on the read and the step-up gate on the writes, one hold per subject and reason, two reasons ending independently, idempotent release, an unknown subject, three malformed references, and the standing-hold count. |
| GREEN | `RetentionLegalHold`, `IRetentionPolicy` with its configuration adapter, three requests and their handlers, three routes, and the `RetentionLegalHolds` migration with its permission backfill. |
| Not vacuous | Making an unreadable period fall back to ninety days fails two of the eighteen. Removed, all eighteen pass. |
| Caught by an existing guard | `Every_identity_domain_slice_is_either_an_earlier_one_or_a_later_one` refused the new `Retention` namespace until it was classified - which is exactly what it is for. |
| Verified | Functional 601/601, Application unit 198/198, Domain unit 191/191, Infrastructure integration 261/261, client 212/212. |

**Two new Platform permissions, and the install they would otherwise have missed.** `platform.retention.read` and
`platform.retention.manage` are separate on purpose - manage does not imply read - and the migration backfills
both onto every Platform system role that already holds `platform.tenants.manage`, for the same reason unit 26.2
did: the bootstrap ceremony runs once per database.

**Tests drive the real routes.** A policy is a property of a running deployment rather than of a request, so a
test that could not vary the configuration would be testing a constant. `PlatformOperator` is the harness that
makes that practical - one person, one cookie, no shared jar - and Task 28 will want it.

**Still to come in Task 26:** the bounded maintenance executor and its erasure records (26.4b), the documentary
dispute (26.5) and the restore admission guard (26.6). Nothing yet erases anything, so the hold this unit places
is currently a promise the executor has still to keep.

### Unit 26.4b - the bounded retention executor (done 2026-09-07)

**Visible outcome met:** settled sessions past their period go, a document past its period is erased with both
halves and its evidence, a held subject is skipped, and none of it happens at all in a deployment that configured
no policy.

**Where the numbers live, reconciled explicitly.** C6 lists 15 minutes, 500 rows, 10 passes, a 60-second budget
*and* "90-day retention for revoked or expired `UserSession` rows" as product defaults, while IA-REQ-056 says a
policy "contains no period, threshold or jurisdictional number" in code. Both are honoured by drawing the line
where it actually falls: the first four bound the **executor** and are constants in source; the ninety days is a
retention **period** and lives only in configuration. If no policy names `SessionRecords`, no session is deleted -
there is no fallback anywhere.

**Three bounds at once, because each fails differently.** A large batch holds locks too long; an unbounded loop
never yields; a cheap-looking pass over a big table can still outlast the interval. Whichever runs out first ends
the run, and what was not reached is reached next time.

**Two guards, both proved load-bearing.** The legal-hold skip and the per-category advisory lock were each
removed to see what failed. The hold check failed two tests immediately. The single-flight lock failed **none** -
because the test asked it of sessions, where deleting a row twice is harmless. Rewritten to ask it of documents,
where a purge writes evidence and two instances would leave two records of one erasure, it fails without the lock
and passes with it. That is the second time in Task 26 a vacuity check found a test passing for the wrong reason,
and both are recorded rather than quietly fixed.

**A deployment with no Platform tenant does nothing.** Audit records are tenant-scoped and this worker acts for
Platform, so an un-bootstrapped deployment has nowhere to record what a purge did - and erasing without being
able to say so is not something this does. It fails closed and says nothing, which is the safe way to be unable
to audit.

| Step | What happened |
|---|---|
| RED | `RetentionLifecycleTests` did not compile against the absent executor, then drove twelve cases against real PostgreSQL with an injected clock and no sleeps: the absent policy, the idle pass that writes nothing, live sessions surviving, the row bound, the pass bound, the tombstone, the evidence, a reclaimed number, a held subject, a released hold, a row classified for another deployment, and two instances at once. |
| GREEN | `PersonalDataErasureRecord`, `RetentionMaintenanceCycle` with its bounds and advisory space `0x5E5513`, `LifecycleMaintenanceService`, the purge tombstone stamp, and the `PersonalDataErasureRecords` migration. |
| Where it runs | The outbox worker process, not the web application - a loop registered there would start inside every functional test, and this one deletes rows. |
| Verified | Functional 601/601, Application unit 198/198, Domain unit 191/191, Infrastructure integration 273/273, client 212/212. |

**Named rather than glossed:** only `SessionRecords` and `PersonalIdentityDocument` are erased today, and only
for the triggers named in [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md). Every other category in the
closed set is skipped **and recorded as skipped**, rather than silently treated as "nothing to do". `AccountClosure`
as a trigger is among them, because C6's `Closed` state is still reached by nothing.

**The runbook is [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md)**, which the plan's Task 26 file list
asks for. It records the MFA recovery route, the policy shape, the executor's bounds, the audit vocabulary, and a
closing table of what none of this closes - real personal data, production, legal certification, the `503` an
unreachable budget store should produce, the unimplemented categories, and live restore certification.

### Unit 26.5 - both halves of the documentary dispute (done 2026-09-07)

**Visible outcome met:** a person whose recorded document is wrong says so from their own profile screen, and an
operator with a case reference held outside this system either corrects it or rejects it. Neither of them can do
it alone, and nothing about the person's access changes while the dispute is open.

**The two absences are the contract, and both are proved.** The owner may open a dispute and can never write a
document value; the operator may resolve one only against a stored dispute and never for their own identity.
Removing the self-resolution guard fails one test; removing the uniqueness check fails another. That is what "no
support bypass" means once it is enforced rather than asserted.

**The claimed number is protected on arrival, exactly like the recorded one.** It exists in the handler for as
long as it takes to encrypt it and reaches nothing else: not the response, not a log, not an audit record, not an
outbox payload. `The_number_somebody_claims_reaches_no_log_no_audit_and_no_response` checks all four rather than
trusting the code's intention.

**The fingerprint table stayed unqueryable.** The correction needs "is this number recorded against somebody who
is not the subject", and the obvious way to ask it - a `DbSet` on the application context - is exactly what the
architecture guard forbids. It became a third narrow member on `IPersonalDocumentRegistry` instead, so there is
still nothing anywhere that lists documents or answers whose a document is.

| Step | What happened |
|---|---|
| RED | `DocumentDisputeTests` did not compile against the absent `People.Documents` namespace, then drove sixteen cases: opening one and access being untouched, the claimed number reaching nothing, the proof gate, an identity with no document, one dispute at a time, a correction replacing both halves, a record holding no value, a rejection changing nothing, an absent dispute, self-resolution, a number somebody else records, three malformed references, the step-up gate, and a settled dispute leaving the way open. |
| GREEN | `IdentityDocumentDispute` and `IdentityDocumentCorrectionRecord`, `IdentityDocument.Correct`, two requests and handlers, two routes, the profile screen's dispute form, and the `DocumentDisputes` migration with its permission backfill. |
| Not vacuous | The self-resolution guard and the uniqueness check were each removed; each failed exactly the test that names it. |
| Verified | Functional 617/617, Application unit 199/199, Domain unit 191/191, Infrastructure integration 273/273, client 213/213. |

**Two tests changed because their premise expired, and neither assertion was weakened.**
`A_profile_reads_masked_and_offers_no_correction_in_this_increment` and its client twin both asserted that
`correctionAvailable` was `false` *because the dispute route did not exist*. It exists now, and the field means
something else: whether this person may open one. Both were rewritten to the new meaning and the client one gained
a sibling that drives the form end to end.

**The screen offers it unconditionally**, which SPEC asks for by name: whether a correction is possible must not
depend on what somebody can see about anybody else, only on whether they already have one open.

**Still to come in Task 26:** the fail-closed restore admission guard (26.6).

### Unit 26.6 - the fail-closed restore admission guard (done 2026-09-07)

**Visible outcome met:** a deployment that may be running on restored data refuses every route with `503`
`recovery_admission_closed` until a signed record held outside that database verifies against a key the backup
does not contain. Only the liveness and readiness probes answer, and they say nothing about the deployment.

**What arms it, decided and named rather than buried.** `IdentityAccess:Recovery:Deployment` alone. Naming a
deployment is an operator saying "this process may be running on restored data", and from that moment nothing
else in the configuration can talk it back down. A deployment that names nothing is not recovering and admits
what it always did - **which is the one place this guard can be wrong in the open direction**. The alternative,
closing every deployment by default, would make an ordinary start indistinguishable from a restore, and a guard
nobody can start a system with is a guard somebody switches off.

**A test found the hole in the first attempt at that.** Arming originally required the deployment *and* the key
together, so an operator who deployed the evidence and forgot the key got a **fully open** deployment on restored
data - the exact shape of mistake this exists for. `Evidence_without_the_operators_key_opens_nothing` failed,
which is how it was found. Arming now needs only the deployment name, and a missing key is `EvidenceInvalid`
rather than "unarmed". Task 27 turns it into a refusal to start at all.

**The middleware runs before everything that reads anything** - before the file server, before authentication,
before any route. Deciding to refuse *after* consulting data that may itself be restored would be the thing the
guard exists to prevent, and the `503` is written directly rather than through the problem-details mapper, which
would resolve services from the request scope.

| Step | What happened |
|---|---|
| RED | `RestoreAdmissionTests` (15) and `RecoveryAdmissionTests` (5) did not compile against the absent adapter, then drove: an unarmed deployment, an armed one that cannot verify, absent evidence, a missing file, quarantine, release, the epoch comparison, expiry, correctly signed evidence about another deployment, a wrong key, an unsigned record, one edited after signing, one that is not a document, a second start over the same evidence, evidence read from a file, and the transport half. |
| GREEN | `IRecoveryAdmission` with its three states, `ConfiguredRecoveryAdmission` over HMAC-SHA256, and `RecoveryAdmissionMiddleware`. No new package: what matters is that the key lives outside the restored data, not that the algorithm is exotic. |
| Not vacuous | Neutering the middleware's check fails two of the five transport tests. Restored, all pass. |
| Verified | Functional 622/622, Application unit 199/199, Domain unit 191/191, Infrastructure integration 288/288, client 213/213. |

**Stated plainly: this is not restore certification.** No backup was taken, none was restored, and no external
authority issued anything. What these tests prove is that the adapter refuses everything it should against
evidence a test produced. The live gate - a real backup, a real restore, and an authority outside this repository
issuing the record - has an owner elsewhere and is recorded as open in
[RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md).

**Deferred to Task 27, named rather than forgotten:** the quarantine state currently gates public ingress and
delivery. Treating sessions created before the epoch as revoked, and terminalizing `OutboxSecret` rows that
predate it, are reads of the restored database that belong with Task 27's other fail-closed work; `PredatesRecovery`
is the question they will ask and it is tested here.

## Task 26 - complete 2026-09-07

All six units are implemented, tested and committed: identity lifecycle, administrative suspension, MFA recovery,
the retention policy with legal holds, the bounded executor, the documentary dispute and the restore admission
guard. Three vacuity checks found tests that proved nothing and each is recorded in its own unit rather than
quietly fixed.

## Tasks 21–25 review remediation — done 2026-09-07

A review of Tasks 21–25 produced eight directed reproductions. They are kept as they were written and were used
as the RED: **15 of 16 backend cases and 11 of 11 client cases failed** before any production code changed. Task 25
is reopened by this section and closed again at the end of it, because every one of those cases is now green.

Nothing here is a new requirement. Each item is a rule that was already stated and was not actually enforced, and
each is recorded against the task that stated it.

| # | Task | What was actually wrong | What now enforces it |
|---|---|---|---|
| R1A | 21, 22 | A sign-in validated the password, then issued a session **after** a password change or a recovery reset had already committed. The change revokes the sessions that exist when it runs, and this one did not exist yet, so it could not be revoked by anything the change did — the login had to refuse itself. It did not, and answered `204` with a working cookie. | The identity's security version is read **before** validation and compared again inside the issuing transaction, under the same per-identity advisory lock that credential changes now take first. Two interleavings survive: the change commits first and the comparison refuses, or the login commits first and the change's own revocation reaches it. |
| R1B | 22 | A recovery link issued before an authenticated password change stayed usable, so a link mailed earlier silently undid a change made later. | `PasswordResetRequest` records the security version it was minted against and is refused when that version has moved. Unknown, spent, superseded, expired and no-longer-about-this-credential remain one answer. |
| R2 | 23 | A provider proof was issued for any validly signed ID token. `prompt=login` is a request, and the reply never says whether it was honoured — so a round trip proved the browser held a provider session, not that a person was present. | The proof challenge sends `max_age`, which obliges the ID token to carry `auth_time`, and the callback refuses when that claim is absent, unreadable, future-dated beyond clock skew or older than five minutes. Fail-closed in every direction. |
| R3 | 24 | Cancelling the offers of a widened role can only see committed offers. An offer validated but not yet inserted was invisible, so an invitation and a widening could both commit and the recipient ended up holding a permission the inviter never held. | A per-tenant role-authority advisory lock, taken first (and waiting) by a role change, taken last (and never waiting) by an offer, which then re-decides under it and answers `invitation_conflict` if it cannot. |
| R4A | 24 | Editing only a role's permissions never rewrote the role row, so its `xmin` — the `version` the contract hands out — never moved and a second administrator's stale edit overwrote the first. | The write path rewrites the owning row in the same transaction, which moves the version and puts the token in the UPDATE's own `WHERE`. |
| R4B | 25 | The same defect on `PUT .../members/{id}/roles`. | The same fix on the membership row. |
| R5 | 25 | C5 allows `Revoked → Active` only through a fresh invitation, and both the issue path and acceptance refused anybody holding a membership row of any status — so a removed person could never return. | A `Revoked` row no longer blocks an offer, and accepting reinstates that same membership with only the newly offered roles. |
| R6A | 23, 24, 25 | Every sensitive screen gated its buttons on a typed password, so an identity that arrived through Google could not begin an operation it held the permission for. | One shared `useIdentityProof` seam: it knows whether there is a password and which provider is linked, and either spends the password or starts a provider round trip bound to the same action. |
| R6B | 25 | `/members` read the roster and the role catalogue in one `Promise.all`, so a session holding `members.read` without `roles.read` lost the whole screen to a `403` on the courtesy read. | On that screen `listMembers` is the only read that owns the error. Every other read answers `null` on refusal and is rendered as an absence. |
| R6C | 24, 25 | The continuation page answered `500` — `x.Id.Value > cursor` is a member access on a value-converted property and EF cannot translate it — and no screen offered a way to ask for one. | The comparison is written through the identifier itself (`MembershipId` already documented the pattern; `RoleId` and `InvitationId` now match it), and all three directories offer a continuation that appends. |

**Two defects were found by working rather than by the reproductions**, and both are fixed here.

- Moving the cursor comparison to `RoleId.From(...)` made an all-zero cursor throw, because the identifiers refuse
  an empty value — turning a well-formed guess into a `500`. Both decoders now read it as "not a position", which
  is what their own comments already promised.
- The first attempt at R1A reused the code `invalid_session` for the new refusal. That code is what the **neutral**
  sign-in failure already returns, so every wrong password became a `401`: fourteen tests said so at once. The
  refusal has its own `credential_superseded` code, and neutrality is verified by the tests that existed for it.

**Commands run.** The eight reproductions, then every suite.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "Revalidation|DelegatedAdministrationRevalidationTests|AdministrationDirectoryRevalidationTests"
npm test --prefix src/Web/ClientApp -- IdentityAccessReviewRevalidation.test.jsx
```

**Named limitations that remain.**

- **Real Google is still unverified, and that is not a code question.** The design relies on OIDC Core 1.0 §3.1.2.1:
  once a request carries `max_age`, the ID token MUST include `auth_time`. Whether Google honours it can only be
  established by a human running one real round trip with real credentials, which this work has not done and must
  not fake. R2 is proved against a controlled provider that models a conformant one. **If that observation comes
  back negative**, a single product decision is owed and must not be resolved by softening the check: either
  provider proof is retired for providers that will not return `auth_time` (and a provider-only identity must set
  a password before any sensitive change), or IA-REQ-051 is amended to name a second, explicitly weaker form of
  evidence. The second is a documented weakening of C4 and has to be signed off as one.
- ~~**`TransferOwnershipAsync` echoes the recipient membership's version but writes only the tenant row**~~ —
  **closed 2026-09-07.** `isOwner` is not a column on a membership, so the transfer rewrote neither member row and
  both kept handing out a version that had stopped describing them. The recipient and the member giving the
  organization up are now both rewritten inside the transfer's own transaction, so a later write echoing either old
  version is refused as stale, and a transfer that changes nothing still moves nothing.
  `OwnershipTransferTests.Handing_the_organization_over_moves_the_version_of_every_member_row_it_changes` fails on
  the previous code with the recipient's version unchanged; `A_transfer_that_changes_nothing_moves_no_version` is
  the guard against the fix over-reaching. The original wording follows, for the record:
- **`TransferOwnershipAsync` echoes the recipient membership's version but writes only the tenant row**, so that
  membership's exposed version does not move even though the member view's `isOwner` flips. It is the same class
  of defect as R4 and was outside the reproductions' scope; it is recorded here rather than fixed quietly.
- **C6 is still not accepted**, so nothing about identity lifecycle changed, and Task 26 remains blocked.

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
| C4 **(accepted 2026-09-06, synthetic only)** | IA-REQ-051, IA-REQ-052, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | IA-REQ-022 and §8, for one named callback route | 21, 22, 23 | live provider registration, which is per-environment; and the reactivation half of `Recovery`, which is C6's and Task 26's |
| C5 **(accepted 2026-09-06, synthetic only, amended D1–D4)** | IA-REQ-053, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | IA-REQ-047, closing its deferred note | 24, 25 | the `Owner` backfill is one-time rather than standing (D1); `roles.manage` and member role changes gain a recent proof (D2); the split-duty lockout is pinned by a test rather than given a rescue route (D3); and `last_administrator_required` on C6's deactivate route stays unreachable until C6 |
| C6 **(accepted 2026-09-07, synthetic only, amended A3/A4, withdrawal E1)** | IA-REQ-054, IA-REQ-055, now normative in [SPEC §4](SPEC.md#4-normative-requirements) | makes IA-REQ-020 precise; extends IA-REQ-042 | 26, 27 against synthetic fixtures | live restore release, which needs the external authority; and provider-only self-reactivation, withdrawn by E1 because Google does not support reauth requests |
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

**Taken 2026-09-06: C3 and C7, then C2 and C4, all for synthetic data only.** Tasks 19 and 20 are done and verified
on the first pair; the second enables Tasks 21, 22 and 23. C5 and C6 remain undecided, so nothing after Task 23 is
authorized. Real personal data, production, §14.3's residual, live provider registration and the reactivation half of
C4's `Recovery` purpose were all withheld.

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

**Closed 2026-09-06.** `Invitation.Issue` and `Reissue` now have direct tests for the default
`VersionedTokenHash` — the one value the struct cannot refuse at its own factory, because nothing was called to
make it. Both were checked by removing the aggregate's guard and watching them fail, so they are evidence rather
than decoration. This was the plan's Task 25 REFACTOR bullet, and it needed no contract: the guard already
existed and only its coverage was missing.

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
