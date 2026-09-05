# Identity Access — Traceability

**Status:** In progress. `Pending` means not implemented. IA-002 and IA-003 have implementation evidence; IA-009 supplies end-to-end acceptance evidence but is never a normative owner.

Task 13 recorded the pre-Platform evidence below. It is acceptance evidence: it demonstrates the journeys the
foundation promises, and it transfers nothing. Every requirement keeps the normative owner it already had, and
IA-009 stays `Blocked` until Tasks 14-16 supply verified Platform evidence.

| Requirement | Scope | Normative owner(s) | Planned evidence | Evidence |
|---|---|---|---|---|
| IA-REQ-001..002 | global identity and tenant-free user | IA-004 | domain, mapping, uniqueness, UUID migration | Pending |
| IA-REQ-003 | registration caller-state branches | IA-006 | anonymous/authenticated, existing-identity, mismatch, and neutral-response tests | Task 13 acceptance (IA-009 evidence only): `A pending registration becomes usable only after confirmation` drives the anonymous branch through the browser and reads the neutral acknowledgement; `A signed-in identity registers another organization without a second account` drives the authenticated branch, which reuses the session's identity instead of creating one. |
| IA-REQ-004 | atomic registration and equivalent replay | IA-006 | sequential and real-PostgreSQL concurrent replay: one organization, membership, outbox/audit set, and identical bodyless `202` | Pending |
| IA-REQ-005 | confirmation gate | IA-006 | activation and sensitive-operation gate tests | Task 13 acceptance (IA-009 evidence only): `the organization is not usable before its confirmation` reaches a protected page as the unconfirmed registrant and is sent back to sign-in; the following steps confirm and then reach it. |
| IA-REQ-006..008 | tenant resolution and active membership | IA-005, IA-007 | evaluator tests plus persisted-session/cross-tenant matrix | Task 13 acceptance (IA-009 evidence only): the zero-, one- and several-membership journeys, `they select the second organization`, and `Permissions do not cross organizations`, which proves the invite action is withdrawn in the organization that does not grant it. |
| IA-REQ-009..013 | catalog, roles, public marker, filtering, versioning | IA-005 | domain, architecture, permission, concurrency, and denial tests | Task 13 architecture: `IdentityAccessArchitectureTests` pins layer direction, every identity request authorizing on a code `Permissions` declares, exactly one of `IPublicRequest`/`[Authorize]`, and the absence of coupling to `UserSession`/`Invitation` from the model that preceded them. |
| IA-REQ-014..018 | invitation onboarding and secret envelope | IA-008, IA-011 | issue/register/accept/replay and delivery tests | Onboarding (Task 10): `InviteMemberTests`, `RegisterInvitedUserTests`, `AcceptInvitationTests`, `ResendAndCancelInvitationTests`, `InvitationHttpContractTests`, `InvitationSecretHygieneTests`, `InvitationAuditTests`. Task 10/11 corrections: both actual registration branches dispatch and settle; missing invitee confirms without early membership; `OutboxDeliveryTests`, `OutboxInfrastructureShapeTests`, `EmailConfigurationTests` prove configured transport, generic-host delivery, atomic cleanup, and bounded receipt reconciliation. External activation: [EMAIL-SETUP.md](EMAIL-SETUP.md). |
| IA-REQ-047 | no privilege escalation through an invitation | IA-008 | offer-outside-own-permissions refusal on every path that establishes an offer: issue, reissue and replacement | Done (Task 10): `Offering_a_role_that_grants_more_than_the_inviter_holds_is_refused_and_creates_nothing`, `Replacing_a_standing_offer_with_one_beyond_the_inviter_authority_is_refused`, `Resending_an_offer_the_inviter_can_no_longer_grant_is_refused` |
| IA-REQ-019..025 | sign-in, cookies, session lifecycle, client storage | IA-007 | options, rate-limit, lockout, antiforgery, expiry, and revocation tests | Task 13 acceptance (IA-009 evidence only): sign-in is driven through the form and its session cookie carries every later request; `A revoked session stops authenticating` proves revocation takes effect on the next protected page; `Sign-in throttling is per account and recovers` proves the lockout refuses a correct password, leaves an unrelated account able to sign in, and lifts once passed; the invitation token is asserted gone from the address bar before acceptance. |
| IA-REQ-026 | membership/role change and sensitive denial events | IA-005 | append-only correlation/redaction/event tests | Pending |
| IA-REQ-026 | registration and confirmation events | IA-006 | transactional audit coverage tests | Pending |
| IA-REQ-026 | sign-in and session create/revoke events | IA-007 | session audit coverage tests | Pending |
| IA-REQ-026 | invitation issue, accept, and invited-identity confirmation events | IA-008 | invitation audit coverage tests | `InvitationAuditTests`; `Invited_confirmation_is_audited_once_without_a_tenant_and_rollback_is_atomic` proves one tenantless `identity.confirmed` audit, no replay duplicate, and rollback with identity/secret state. |
| IA-REQ-026 | Platform bootstrap, MFA, admin, and tenant-lifecycle events | IA-014 | append-only allowlisted Platform audit tests | Pending |
| IA-REQ-027 | confirmation effects | IA-006 | business transaction creates message and encrypted envelope | Pending |
| IA-REQ-027 | invitation effects | IA-008 | invitation/outbox atomicity tests | Done (Task 10): `InviteMemberTests`, `ResendAndCancelInvitationTests` |
| IA-REQ-028 | lease, CAS, backoff, idempotent delivery | IA-011 | concurrent claim, transient, expired, permanent, and replay tests | Task 11 corrections: `OutboxDeliveryTests` covers immutable owner/generation settlement, just-in-time claims, expiry during earlier work, every exhausted cleanup branch, crashed-attempt bound, payload/credential drift, and real-adapter receipt replay after local rollback/24-hour expiry. `MigrationUpgradeTests.Outbox_safety_upgrade_preserves_history_and_bounds_preexisting_attempts_conservatively` proves prior-row preservation. Focused evidence: `artifacts/current-fix/outbox-final-focused.trx` (76 passed). |
| IA-REQ-029 | authorization/registration redaction | IA-005, IA-006 | allowlist and negative secret/PII scans | Pending |
| IA-REQ-029 | session/invitation/delivery redaction | IA-007, IA-008 | Problem Details, audit, outbox, and telemetry tests | Pending |
| IA-REQ-029 | Platform MFA/projection redaction | IA-012, IA-014 | encrypted-secret, hashed-code, allowlist, and negative leak tests | Pending |
| IA-REQ-030 | 401/403/404 semantics | IA-005 | authorization matrix and stable Problem Details | Task 13: `EndpointShapeTests.No_endpoint_touches_the_database` keeps every endpoint behind the request pipeline, which is what makes those answers uniform; the acceptance journeys read the refusals as rendered problems rather than as raw status codes. |
| IA-REQ-031 | target stack and same-origin policy | IA-002, IA-007 | architecture/configuration and cookie-session tests | IA-002 complete: PostgreSQL-only target and real PostgreSQL harness. Task 13: the same-origin policy is now genuinely exercised — the development frontend is served over HTTPS, so `__Host-` cookies are storable and the API compares the browser's Origin against the scheme it was actually reached on. Serving it over HTTP had made every SPA mutation fail exact-origin validation on scheme alone. |
| IA-REQ-032 | no seed or destructive startup | IA-003 | restart/sentinel/no-user test | IA-003 complete: baseline migration, restart/sentinel preservation, and no-user test |
| IA-REQ-033..034, IA-REQ-036 | PostgreSQL uniqueness, composite FKs, deletes | IA-004 | metadata plus real-PostgreSQL constraint tests | Pending |
| IA-REQ-035 | lost-update protection | IA-004 | Task 4 real-PostgreSQL stale `UpdateTodoItemDetailCommand` proof; Task 6 typed `todo_item_concurrency_conflict` RFC 9457 `409`/`traceId` evidence; Task 8 session mutations reload, revalidate and answer `401` `invalid_session` or typed `session_concurrency_conflict` `409`, with sign-in supersession made conflict-free by a conditional update | Pending |
| IA-REQ-037 | current-template baseline | IA-003 | empty-to-baseline and restart preservation | IA-003 complete: `BaselinePostgreSql` migration and restart preservation |
| IA-REQ-037 | identity-access upgrade | IA-004 | empty-to-latest and baseline-to-latest sentinel preservation | `MigrationUpgradeTests`: `TenantAuthorization_empty_database_upgrades_to_latest_without_pending_migrations` covers empty-to-latest; `IdentityAccess_upgrade_preserves_baseline_identity_and_todo_data` and the session, invitation and registration-messaging round trips cover baseline-to-latest with sentinel preservation. |
| IA-REQ-038 | shared Result types, HTTP mapping/writer, and OpenAPI contract | IA-005 | model, runtime, media-type, status/header/code, safe-500, and drift tests | Pending |
| IA-REQ-038 | registration/confirmation endpoint application | IA-005 | IA-006 DTO/status/error metadata and functional evidence | Pending |
| IA-REQ-038 | session/context/rate-limit endpoint application | IA-005 | IA-007 DTO/status/error metadata, `429` Problem Details, and `Retry-After` evidence | Pending |
| IA-REQ-038 | invitation endpoint application | IA-005 | IA-008 DTO/status/header/error metadata and functional evidence | Pending |
| IA-REQ-038 | React and browser acceptance | IA-005 | IA-009 sole-parser, MSW, lint/build, and E2E evidence only | Task 13 acceptance (IA-009 evidence only): `The served contract matches what the client calls` reads the served OpenAPI document and asserts every route the client calls is declared and no legacy identity route is; 56 client tests, lint and build all exit 0. |
| IA-REQ-038 | Platform endpoint/panel application | IA-005 | IA-014 DTO/status/code metadata plus Platform functional/React evidence | Pending |
| IA-REQ-039 | singleton Platform tenant and normal authority | IA-014 | domain, PostgreSQL singleton/active-membership, evaluator, and no-bypass tests | Pending |
| IA-REQ-040 | one-time Platform bootstrap and pre-activation recovery | IA-014 | public opaque recovery without identity/email input; `202` eligible/ineligible-state behavior, `400 antiforgery_validation_failed`, `429 rate_limit_exceeded`/`Retry-After`, expiry/permanent-failure reissue, token rotation, concurrency, config-change, outbox, and audit tests | Pending |
| IA-REQ-041 | Platform credential onboarding, mandatory TOTP, recovery codes, and step-up | IA-012 | persisted `PlatformAdminInvitation` before MFA RED; PasswordOptions-valid missing-identity creation, existing-identity credential-ignore/generic flow, token-aware confirm outbox path, authenticated confirmed-email/token/antiforgery binding, theft/mismatch/session negatives, encrypted-secret/hashed-code mapping, and recent-MFA tests | Pending |
| IA-REQ-042 | Platform administrator invitation/revocation | IA-014 | bootstrap/new-admin onboarding, invitation, first/last-owner, authorization, audit, and negative direct-elevation tests | Pending |
| IA-REQ-043 | conditional Organization suspension/reactivation | IA-014 | real-PostgreSQL stale update, immediate evaluator, Platform-reservation, and audit tests | Pending |
| IA-REQ-044 | allowlisted Platform projections and global audit | IA-014 | exact field allowlists, redaction, append-only, runtime, OpenAPI, and React parser tests | Pending |
| IA-REQ-045 | MFA-bound typed Platform panel/directories | IA-014 | admin directory, bounded/cursor DTOs, MSW, UI authorization, Problem Details, and browser journey tests | Pending |
| IA-REQ-046 | prohibited Platform capabilities | IA-014 | architecture, endpoint, functional, OpenAPI, React, and E2E negative tests | Pending |

## IA-009 acceptance journeys

IA-009 remains `Blocked` until IA-014 has verified Tasks 14–16; only then may its acceptance evidence move to `Review`.

| Journey | Normative tasks supplying behavior | Evidence |
|---|---|---|
| register → confirm → sign in | IA-006, IA-007 | Pending |
| select two organizations without permission leakage | IA-005, IA-007 | Pending |
| invite → register → confirm → sign in → accept | IA-008 | Pending |
| existing identity accepts without duplicates | IA-008 | Pending |
| revoke session → reject cookie | IA-007 | Pending |
| baseline/latest restart preserves data | IA-003, IA-004 | Pending |
| bootstrap → MFA → Platform operation → audit | IA-012, IA-014 | Pending |
| bootstrap/admin invitation → register/reuse → confirm → sign in → MFA → activate | IA-012, IA-014, IA-009 | Pending |
| Platform owner revocation and prohibited operations are rejected | IA-014 | Pending |

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
