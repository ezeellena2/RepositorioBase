# Identity Access — Traceability

**Status:** In progress. `Pending` means not implemented. IA-002 and IA-003 have implementation evidence; IA-009 supplies end-to-end acceptance evidence but is never a normative owner.

| Requirement | Scope | Normative owner(s) | Planned evidence | Evidence |
|---|---|---|---|---|
| IA-REQ-001..002 | global identity and tenant-free user | IA-004 | domain, mapping, uniqueness, UUID migration | Pending |
| IA-REQ-003 | registration caller-state branches | IA-006 | anonymous/authenticated, existing-identity, mismatch, and neutral-response tests | Pending |
| IA-REQ-004 | atomic registration and equivalent replay | IA-006 | sequential and real-PostgreSQL concurrent replay: one organization, membership, outbox/audit set, and identical bodyless `202` | Pending |
| IA-REQ-005 | confirmation gate | IA-006 | activation and sensitive-operation gate tests | Pending |
| IA-REQ-006..008 | tenant resolution and active membership | IA-005, IA-007 | evaluator tests plus persisted-session/cross-tenant matrix | Pending |
| IA-REQ-009..013 | catalog, roles, public marker, filtering, versioning | IA-005 | domain, architecture, permission, concurrency, and denial tests | Pending |
| IA-REQ-014..018 | invitation onboarding and secret envelope | IA-008 | issue/register/accept/replay and delivery tests | Onboarding done (Task 10): `InviteMemberTests`, `RegisterInvitedUserTests`, `AcceptInvitationTests`, `ResendAndCancelInvitationTests`, `InvitationHttpContractTests`, `InvitationSecretHygieneTests`, `InvitationAuditTests`. Delivery (IA-REQ-018 dispatch) pending in Task 11 |
| IA-REQ-047 | no privilege escalation through an invitation | IA-008 | offer-outside-own-permissions refusal on every path that establishes an offer: issue, reissue and replacement | Done (Task 10): `Offering_a_role_that_grants_more_than_the_inviter_holds_is_refused_and_creates_nothing`, `Replacing_a_standing_offer_with_one_beyond_the_inviter_authority_is_refused`, `Resending_an_offer_the_inviter_can_no_longer_grant_is_refused` |
| IA-REQ-019..025 | sign-in, cookies, session lifecycle, client storage | IA-007 | options, rate-limit, lockout, antiforgery, expiry, and revocation tests | Pending |
| IA-REQ-026 | membership/role change and sensitive denial events | IA-005 | append-only correlation/redaction/event tests | Pending |
| IA-REQ-026 | registration and confirmation events | IA-006 | transactional audit coverage tests | Pending |
| IA-REQ-026 | sign-in and session create/revoke events | IA-007 | session audit coverage tests | Pending |
| IA-REQ-026 | invitation issue and accept events | IA-008 | invitation audit coverage tests | Pending |
| IA-REQ-026 | Platform bootstrap, MFA, admin, and tenant-lifecycle events | IA-014 | append-only allowlisted Platform audit tests | Pending |
| IA-REQ-027 | confirmation effects | IA-006 | business transaction creates message and encrypted envelope | Pending |
| IA-REQ-027 | invitation effects | IA-008 | invitation/outbox atomicity tests | Pending |
| IA-REQ-028 | lease, CAS, backoff, idempotent delivery | IA-008 | concurrent claim, transient, expired, permanent, and replay tests | Pending |
| IA-REQ-029 | authorization/registration redaction | IA-005, IA-006 | allowlist and negative secret/PII scans | Pending |
| IA-REQ-029 | session/invitation/delivery redaction | IA-007, IA-008 | Problem Details, audit, outbox, and telemetry tests | Pending |
| IA-REQ-029 | Platform MFA/projection redaction | IA-012, IA-014 | encrypted-secret, hashed-code, allowlist, and negative leak tests | Pending |
| IA-REQ-030 | 401/403/404 semantics | IA-005 | authorization matrix and stable Problem Details | Pending |
| IA-REQ-031 | target stack and same-origin policy | IA-002, IA-007 | architecture/configuration and cookie-session tests | IA-002 complete: PostgreSQL-only target and real PostgreSQL harness |
| IA-REQ-032 | no seed or destructive startup | IA-003 | restart/sentinel/no-user test | IA-003 complete: baseline migration, restart/sentinel preservation, and no-user test |
| IA-REQ-033..034, IA-REQ-036 | PostgreSQL uniqueness, composite FKs, deletes | IA-004 | metadata plus real-PostgreSQL constraint tests | Pending |
| IA-REQ-035 | lost-update protection | IA-004 | Task 4 real-PostgreSQL stale `UpdateTodoItemDetailCommand` proof; Task 6 typed `todo_item_concurrency_conflict` RFC 9457 `409`/`traceId` evidence; Task 8 session mutations reload, revalidate and answer `401` `invalid_session` or typed `session_concurrency_conflict` `409`, with sign-in supersession made conflict-free by a conditional update | Pending |
| IA-REQ-037 | current-template baseline | IA-003 | empty-to-baseline and restart preservation | IA-003 complete: `BaselinePostgreSql` migration and restart preservation |
| IA-REQ-037 | identity-access upgrade | IA-004 | empty-to-latest and baseline-to-latest sentinel preservation | Pending |
| IA-REQ-038 | shared Result types, HTTP mapping/writer, and OpenAPI contract | IA-005 | model, runtime, media-type, status/header/code, safe-500, and drift tests | Pending |
| IA-REQ-038 | registration/confirmation endpoint application | IA-005 | IA-006 DTO/status/error metadata and functional evidence | Pending |
| IA-REQ-038 | session/context/rate-limit endpoint application | IA-005 | IA-007 DTO/status/error metadata, `429` Problem Details, and `Retry-After` evidence | Pending |
| IA-REQ-038 | invitation endpoint application | IA-005 | IA-008 DTO/status/header/error metadata and functional evidence | Pending |
| IA-REQ-038 | React and browser acceptance | IA-005 | IA-009 sole-parser, MSW, lint/build, and E2E evidence only | Pending |
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
