# Multitenant Identity Access — Specification

**Status:** Proposed for approval  
**Date:** 2026-08-31  
**Functional source:** external `CleanArchitecture` reference repository, `docs/standards/identity-access`  
**Related decision:** [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md)

## 1. Objective

Turn this starter into a reusable identity and access foundation for multitenant SaaS applications with an ASP.NET Core backend, PostgreSQL, and React web. Identity is global; access is resolved within an active tenant through memberships, roles, and permissions. The system supports invitations, revocable sessions, a minimal Platform operations slice, and auditability without delegating business rules to ASP.NET Core Identity or the frontend.

This specification adopts the reference standard's **business semantics**. It does not copy the reference repository's working methodology. This repository uses the project SDD protocol summarized in section 12.

## 2. Product scope

### 2.1 Target baseline

- ASP.NET Core Identity manages credentials, lockout, framework tokens, and external providers; it does not manage tenant membership.
- PostgreSQL is the source of truth for extended identity, tenants, memberships, roles, permissions, invitations, sessions, audit, and outbox records.
- React is served same-origin with the API and uses a BFF cookie. JavaScript never receives or persists bearer tokens.
- An identity can have at most one `Personal` tenant, belong to multiple `Organization` tenants, and, when applicable, hold a separate `Platform` membership.
- Roles are dynamic and tenant-scoped. Permissions are stable backend-defined capabilities.
- Reliable external effects are recorded in an outbox in the same transaction as the business change.

### 2.2 First functional increment

The first increment delivers one usable vertical journey:

1. register an organization and its responsible user;
2. confirm email;
3. sign in locally with a revocable web session;
4. select an active organization;
5. query identity context and effective permissions;
6. invite a member by email;
7. accept the invitation idempotently as a new or existing identity;
8. switch organizations without mixing data or permissions;
9. sign out and revoke the current session;
10. bootstrap the first Platform owner, complete mandatory MFA, and manage Platform administrators;
11. use a safe Platform panel to operate organization lifecycle and inspect allowlisted security projections;
12. deliver React screens and tests for the complete journey.

### 2.3 Required roadmap outside the first increment

The following capabilities remain part of the baseline but are delivered in later slices:

- `Personal` tenant, `PersonProfile`, and protected `AR/DNI` document data;
- password recovery and password change;
- Google OIDC sign-in and secure account linking;
- custom roles and complete membership administration;
- lifecycle/recovery, retention, and advanced operational controls from the standard.

Until the applicable controls are complete, the starter must not claim full baseline compliance or enable real PII or production use.

### 2.4 Explicit non-goals

The initial Platform slice does not permit impersonation, destructive deletion, tenant-private or business-data reading, a client-selected tenant context, a global boolean/claim/role bypass, or a default administrator credential.

## 3. Domain language

| Term | Meaning in this project |
|---|---|
| Identity | Global authenticatable person represented by `ApplicationUser` |
| Tenant | Logical data and authorization boundary |
| Membership | Relationship between an identity and a tenant |
| Role | Tenant-scoped grouping of permissions |
| Permission | Stable backend-known capability such as `members.invite` |
| Active tenant | Explicit context validated for a request |
| Session | Persisted revocable record referenced by a protected cookie |
| Invitation | Temporary, single-use intent to add an identity to an organization |
| Outbox | Transactional intent to perform an external effect |
| Platform | Reserved singleton tenant used only for explicit operational permissions |
| Platform MFA | TOTP enrollment, recovery-code acknowledgement, and recent step-up proof for a Platform administrator |

`TenantId` is used for tenancy. `ClientId` remains reserved for OAuth/OIDC. B2C and B2B describe operating contexts, not user types.

## 4. Normative requirements

### Identity and registration

- **IA-REQ-001:** one normalized email identifies at most one active local identity.
- **IA-REQ-002:** `ApplicationUser` does not contain `TenantId`, role, DNI, CUIT, or a B2C/B2B discriminator.
- **IA-REQ-003:** organization registration deterministically follows the caller state below. Every branch that creates an organization writes the identity decision, tenant, organization profile, responsible membership, initial roles, audit, and applicable confirmation/outbox intent in one consistency boundary.
  - An anonymous request for an email with no identity creates an unconfirmed identity plus a pending organization and responsible membership, then returns the neutral `202` response.
  - An anonymous request for an email that already belongs to an identity creates no identity, tenant, membership, or role. It returns the same neutral `202` response and may enqueue a generic sign-in or confirmation notice; the caller must authenticate before creating another organization.
  - An authenticated request creates another organization for the current identity. Any submitted email must normalize to that identity's email; otherwise the request is rejected. The new tenant and responsible membership belong to the authenticated identity only.
- **IA-REQ-004:** a partial failure never leaves an organization without its responsible membership. The Application registration service derives a canonical equivalent-submission key from normalized caller scope and normalized registration intent, claims it before effects, and stores the completed neutral response. Sequential or concurrent replay returns the same bodyless `202` and produces one organization, responsible membership, outbox set, and audit set; database uniqueness remains a backstop, not the idempotency mechanism. Distinct conflicting branches in IA-REQ-003 remain unchanged.
- **IA-REQ-005:** email must be confirmed before inviting members, administering roles, accepting an invitation, or performing a sensitive operation.

### Tenancy and authorization

- **IA-REQ-006:** every tenant-scoped operation requires an effective `TenantId`. The server resolves it exclusively from `UserSession.ActiveTenantId` after validating the protected cookie, persisted session, revocation/expiry state, tenant, and membership. Client-provided tenant headers, query strings, claims, or local storage never establish the active tenant.
- **IA-REQ-007:** each request uses exactly one membership identified by `(UserId, TenantId)`; permissions from other tenants are never accumulated.
- **IA-REQ-008:** authentication and authorization are independent. A valid session does not reactivate a suspended tenant or membership.
- **IA-REQ-009:** the permission catalog lives in code, uses `resource.action`, and is synchronized idempotently to PostgreSQL.
- **IA-REQ-010:** roles belong to one tenant and group permissions. The backend never authorizes by display role name.
- **IA-REQ-011:** Application authorization is deny-by-default. Every Application request must implement the authorized-request contract and declare its permission and tenant requirement, or implement the explicit `IPublicRequest` marker. An architecture test rejects requests implementing neither contract; HTTP endpoint metadata alone cannot make a business request public.
- **IA-REQ-012:** every query for a tenant-scoped resource filters by the validated tenant as well as the resource identifier.
- **IA-REQ-013:** changing roles, assignments, memberships, or tenant state increments `AuthorizationVersion` in the same transaction.

### Invitations

- **IA-REQ-014:** an invitation belongs to an `Organization`, one normalized email, and initial roles from that same tenant.
- **IA-REQ-015:** the acceptance token is cryptographically random; only its hash is persisted in the invitation. It expires, is single-use, and is invalidated when cancelled or accepted.
- **IA-REQ-016:** final acceptance requires an authenticated identity whose confirmed email matches the recipient. It reuses `ApplicationUser` and creates at most one membership. A new invitee first submits the invitation token and credential-registration input to the public token-aware registration endpoint: a missing identity is created for the invitation email and sent confirmation, while an existing identity ignores credential input and receives only a generic sign-in/confirmation notice. Neither path accepts the invitation before confirmation, sign-in, and the authenticated acceptance request.
- **IA-REQ-017:** creating, reissuing or replacing an invitation and its outbox message is atomic; unlimited equivalent active tokens are not allowed. Inviting a recipient who already holds a live offer supersedes that offer in one transaction — rotating the token and terminalizing the superseded `OutboxSecret` so its token can never be delivered — rather than failing. A recipient never holds two live offers, and no caller is left with an offer it cannot correct: withdrawal requires `members.manage` and has no route in the first increment, so refusing would strand a holder of `members.invite`.
- **IA-REQ-018:** the usable email token never appears in an outbox payload. It is stored in a separate encrypted, expiring `OutboxSecret` envelope referenced by an opaque identifier. The delivery handler leases the outbox message, loads and decrypts the envelope only in memory, renders the configured email adapter or isolated test sink, and sends with the outbox message ID as its idempotency key. After acknowledged delivery, one local transaction marks the message delivered and terminalizes the envelope by clearing ciphertext and recording non-secret delivery evidence. Retries consult the message state and adapter delivery receipt so the same logical message is not sent twice. Expired or permanently failed envelopes are terminalized without exposing the token. Raw tokens are forbidden in outbox payloads, audit, application logs, Problem Details, and delivery telemetry.
- **IA-REQ-047:** an invitation may offer only roles whose permissions the inviter already holds in that tenant. A role granting any permission outside the inviter's own effective set is refused on every path that establishes an offer: issuing, reissuing the standing offer, and replacing it with a different one. `members.invite` therefore never grants more authority than the inviter has. Acceptance grants exactly the roles that were offered and does not re-evaluate them, because the inviter may no longer exist; a role whose permissions widen between the offer and its acceptance is a deferred control, named here rather than claimed.

### Session and sign-in

- **IA-REQ-019:** local sign-in uses email and password over HTTPS, applies rate limiting by IP and account, applies lockout, and returns generic responses.
- **IA-REQ-020:** only an active identity with confirmed email can obtain a session.
- **IA-REQ-021:** authentication creates a new `UserSession` and regenerates its identifier. The protected cookie references that session but is not the durable permission source. `UserSession.ActiveTenantId` is set automatically only when exactly one active membership exists; otherwise it remains null until explicit selection.
- **IA-REQ-022:** the authentication cookie is `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, and has no `Domain`. Authenticated mutations require exact-origin validation and the antiforgery contract in section 8.
- **IA-REQ-023:** initial defaults are a 30-minute idle lifetime, a 12-hour absolute lifetime, and no remember-me option.
- **IA-REQ-024:** sign-out revokes the persisted session and deletes the authentication cookie. A revoked or expired session returns `401`.
- **IA-REQ-025:** React does not store authentication cookies, tickets, JWTs, invitation tokens, or antiforgery request tokens in `localStorage` or `sessionStorage`.

### Audit, outbox, and security

- **IA-REQ-026:** sign-in, confirmation, sessions, invitations, memberships, roles, and sensitive denials generate append-only `AuditEvent` records with a correlation ID and no secrets.
- **IA-REQ-027:** every reliable external effect writes an `OutboxMessage` in the same transaction as the business change.
- **IA-REQ-028:** the worker claims messages using a lease and compare-and-swap, retries with backoff, and uses idempotent handlers.
- **IA-REQ-029:** passwords, tokens, cookies, codes, connection strings, and PII do not appear in logs, Problem Details, audit records, or outbox payloads.
- **IA-REQ-030:** `401` means an absent or invalid identity; `403` means a valid identity without permission. Cross-tenant access may return `404` to avoid revealing a resource.
- **IA-REQ-031:** CORS does not use `AllowAnyOrigin`; a same-origin SPA needs no open production policy.
- **IA-REQ-032:** the project creates no default administrator or password, and startup never deletes the database.

### Persistence

- **IA-REQ-033:** PostgreSQL constraints protect uniqueness for email, slug, CUIT, membership, and token hash.
- **IA-REQ-034:** membership/role and invitation/role associations use composite keys and foreign keys containing `TenantId`, so PostgreSQL rejects cross-tenant combinations.
- **IA-REQ-035:** sensitive mutations use a concurrency token or conditional update and translate a lost update into `409 Conflict`.
- **IA-REQ-036:** every foreign key declares delete behavior; cascade is not used where it could erase identity, authorization, or audit history.
- **IA-REQ-037:** migrations are tested from an empty database and from the previous version. `EnsureDeleted`/`EnsureCreated` is not a normal migration strategy.

### API contract

- **IA-REQ-038:** expected Domain/Application business failures use typed `Result`/`Result<T>` values with a stable code and category; unexpected infrastructure or programmer failures remain exceptions. Web maps both paths to the external HTTP contract and never serializes the internal Result or a universal `{ success, data, error }` envelope. Body-bearing success returns an endpoint-specific DTO with semantic status and headers (`200`; `201` with `Location`; the documented neutral `202`; or bodyless `204`). Every non-success is RFC 9457 `application/problem+json` with matching status, stable `code`, opaque `traceId`, optional safe `detail`, and field-indexed `errors` only for validation; a generic safe `500` exposes no internal diagnostics. Generated `401`, `403`, and `429` use the same Problem Details writer, and `429` includes `Retry-After`. OpenAPI declares each endpoint's success schema/status/required headers and supported error statuses/shapes/codes; contract tests reject drift across runtime, OpenAPI, and React. React consumes one typed API boundary, knows nothing of internal Result, and adds no pagination envelope to identity endpoints.

### Platform operations and MFA

- **IA-REQ-039:** exactly one reserved `TenantType.Platform` tenant may exist. A Platform owner is a normal global `ApplicationUser` with an active Platform `TenantMembership`; every Platform request resolves that active tenant and explicit `platform.*` permission through the normal evaluator. The system MUST NOT use `IsSuperAdmin`, a global claim/role, or a context bypass.
- **IA-REQ-040:** a deployment owner may explicitly configure one bootstrap email. Only while the Platform tenant is absent, an idempotent transaction creates the singleton tenant, system owner role, pending owner invitation, audit event, and outbox intent without a password or source/config secret. Before owner activation, a bodyless same-origin, antiforgery-protected, rate-limited, neutral recovery request may rotate/reissue only an expired or permanently delivery-failed pending owner invitation. It accepts no email, identity, or replacement recipient: the destination is derived only from the unchanged configured and pending normalized recipient, so it also works before an `ApplicationUser` exists. It invalidates the prior token, preserves one pending owner, records outbox/audit evidence, and has no membership activation or elevation effect. Concurrent recovery attempts yield one current invitation/effect set. A configuration change MUST NOT replace or elevate the pending owner. First owner activation permanently closes bootstrap; later configuration changes MUST NOT elevate an identity.
- **IA-REQ-041:** Platform invitation registration and confirmation are token-aware for both a bootstrap owner and a later Platform administrator. When the matching global `ApplicationUser` is absent, registration accepts validated credential-registration input and creates that identity with the submitted password through configured `PasswordOptions`; it never generates or stores a default password. When the matching identity already exists, credential material is ignored and the neutral flow gives only generic sign-in/confirmation behavior. Both branches issue/consume confirmation through the transactional outbox and never create or activate a Platform membership. Platform membership activation requires that confirmed identity, completed encrypted TOTP enrollment, acknowledged hashed one-time recovery codes, and an MFA-authenticated session. Every Platform MFA enrollment, verification, and recovery-acknowledgement request requires antiforgery plus an authenticated, confirmed identity whose normalized email matches the pending Platform invitation and its bound one-time token; it is never globally public or token-only. That identity may hold an authenticated session with no active Platform tenant until activation. Platform mutations require recent MFA step-up; TOTP secrets are encrypted and recovery codes are stored only as hashes.
- **IA-REQ-042:** Platform administrators are invited and revoked through `PlatformAdminInvitation` and Platform memberships, never directly elevated. A bootstrap owner and a new administrator can use the token-aware register/confirm path—with a submitted password only when creating a missing identity—before normal password sign-in and invitation-bound MFA, but neither gains active Platform membership before those gates. The system MUST preserve at least one active Platform owner and reject a last-owner revocation.
- **IA-REQ-043:** a Platform administrator with the appropriate permission may suspend or reactivate an Organization tenant through a reasoned conditional mutation. It is concurrency-safe, immediately effective in the normal authorization evaluator, and audited. Platform itself MUST NOT be suspended or deleted through these endpoints.
- **IA-REQ-044:** Platform organization, identity, administrator, and global security/audit views are read-only allowlisted operational projections. Organization responses contain only `tenantId`, normalized slug, type, status, created/updated timestamps, suspension state/reason/timestamp, and concurrency version. Identity/admin responses contain only user/membership IDs, normalized email behind the explicit directory permission, email-confirmed/account/membership/MFA status, owner flag, and operational timestamps. Audit responses contain only event ID/type, occurred timestamp, correlation ID, actor/tenant identifiers, outcome, and allowlisted reason/code metadata. They MUST NOT expose CUIT, tenant business/domain rows, arbitrary profile payloads, credentials, secrets, raw tokens, extra PII, or mutable audit payloads.
- **IA-REQ-045:** the typed React client exposes a Platform panel only for an MFA-authenticated Platform session. It uses declared DTOs and Problem Details for organization/identity/administrator projections, tenant lifecycle, administrator invitation/revocation, and global audit; it never obtains a bypass or private tenant data. Platform directories are distinct `/api/platform/*` resources: each accepts bounded `limit` (1–100) and opaque `cursor`, and returns its endpoint-specific typed `{ items, nextCursor }` response. This does not change the no-pagination-envelope contract for `/api/identity/*` endpoints.
- **IA-REQ-046:** Platform operations MUST NOT implement impersonation, destructive deletion, arbitrary cross-tenant context selection, tenant-private/business-data reading, default credentials, or any global authorization bypass. Architecture, functional, OpenAPI, React, and E2E tests reject these capabilities.

## 5. Conceptual model

```text
ApplicationUser 1---0..1 PersonProfile
ApplicationUser 1---* TenantMembership *---1 Tenant
Tenant 1---0..1 OrganizationProfile
Tenant 1---* Role *---* Permission
TenantMembership *---* Role
Tenant 1---* Invitation *---* Role
ApplicationUser 1---* UserSession
ApplicationUser 1---0..1 PlatformMfaEnrollment 1---* PlatformRecoveryCode
Invitation 0..1---1 ApplicationUser (AcceptedBy)
Tenant (Platform) 1---* PlatformAdminInvitation 0..1---1 ApplicationUser (BoundUser)
OutboxMessage 1---0..1 OutboxSecret
Tenant 1---* AuditEvent
```

Initial aggregates:

- `Tenant`: type, state, slug, and authorization version.
- `TenantMembership`: membership, state, and role assignments.
- `Role`: name, `SystemCode`, and permissions within one tenant.
- `Invitation`: recipient, token hash, expiry, state, and offered roles.
- `PlatformAdminInvitation`: Platform-only recipient, token hash, expiry/delivery state, and optional bound user; it is not an Organization `Invitation`.
- `UserSession`: idle/absolute lifetime, active tenant, and revocation.
- `PlatformMfaEnrollment`: encrypted TOTP secret, enrollment state, and step-up evidence.
- `PlatformRecoveryCode`: hashed one-time recovery material bound to one enrollment.

## 6. Initial HTTP contract

Routes are contractual drafts; generated OpenAPI becomes the implementation source of truth.

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/identity/antiforgery` | Public bootstrap | `200` request-token DTO + antiforgery cookie |
| `POST /api/identity/organizations/register` | Public or authenticated + antiforgery | neutral bodyless `202` |
| `POST /api/identity/confirm-email` | Public + token + antiforgery | idempotent bodyless `204` |
| `POST /api/identity/sessions` | Public + antiforgery | bodyless `204` + cookie or Problem Details |
| `DELETE /api/identity/sessions/current` | Authenticated + antiforgery | bodyless `204`; a session already revoked by a parallel request is `401` `invalid_session` and still deletes the cookie; a lost update that never settles is `409` `session_concurrency_conflict` |
| `GET /api/identity/context` | Authenticated | `200` identity-context DTO |
| `PUT /api/identity/context/tenant` | Authenticated + antiforgery | `200` updated identity-context DTO; a lost update that never settles is `409` `session_concurrency_conflict` |
| `POST /api/tenants/{tenantId}/invitations` | `members.invite` + antiforgery | `201` invitation DTO + `Location` |
| `POST /api/invitations/register` | Public + invitation token + antiforgery | neutral bodyless `202`; registration/confirmation only |
| `POST /api/invitations/accept` | Authenticated + token + antiforgery | idempotent `200` acceptance DTO |
| `POST /api/platform/bootstrap/recover` | public bodyless same-origin + antiforgery + rate limit; no identity, email, or replacement recipient input | valid opaque states: neutral bodyless `202`; missing/malformed antiforgery: `400` Problem Details `antiforgery_validation_failed`; exhausted limit: `429` Problem Details `rate_limit_exceeded` + `Retry-After` |
| `POST /api/platform/invitations/register` | public + Platform invitation token + credential-registration DTO + antiforgery | neutral bodyless `202`; missing identity uses submitted PasswordOptions-valid password, existing identity ignores credentials; issue confirmation, never membership |
| `POST /api/platform/invitations/confirm` | public + Platform invitation token + email-confirmation token + antiforgery | idempotent bodyless `204`; confirms identity, never membership |
| `POST /api/platform/mfa/enroll`, `/verify`, and `/recovery-acknowledge` | authenticated, confirmed pending Platform invitee whose normalized email matches its bound one-time invitation token + antiforgery; no active Platform tenant is required before activation | enrollment DTO, then bodyless `204` |
| `POST /api/platform/mfa/step-up` | Platform administrator + antiforgery | bodyless `204` or Problem Details |
| `GET /api/platform/organizations` with `limit`/`cursor` | active Platform tenant + `platform.organizations.read` | `200` typed `{ items: PlatformOrganizationResponse[], nextCursor }` |
| `GET /api/platform/identities` with `limit`/`cursor` | active Platform tenant + `platform.identities.read` | `200` typed `{ items: PlatformIdentityResponse[], nextCursor }` |
| `GET /api/platform/admins` with `limit`/`cursor` | active Platform tenant + `platform.admins.read` | `200` bounded `{ items, nextCursor }` administrator directory DTO |
| `GET /api/platform/audit` with `limit`/`cursor` | active Platform tenant + `platform.audit.read` | `200` typed `{ items: PlatformAuditEventResponse[], nextCursor }` |
| `POST /api/platform/organizations/{tenantId}/suspend`, `/reactivate` | `platform.tenants.manage` + recent MFA | bodyless `204` or `409` Problem Details |
| `POST /api/platform/admins/invitations`; `POST /api/platform/admins/{membershipId}/revoke` | `platform.admins.manage` + recent MFA | neutral `202` / bodyless `204` |

All non-success responses follow IA-REQ-038. Sign-in, registration, recovery, and invitation flows do not unnecessarily reveal whether an email exists.

## 7. React context contract

`GET /api/identity/context` returns at least:

```json
{
  "user": { "id": "opaque", "displayName": "Ana", "emailConfirmed": true },
  "activeTenant": { "id": "uuid", "type": "Organization", "name": "Acme" },
  "availableTenants": [
    { "id": "uuid", "type": "Organization", "name": "Acme" }
  ],
  "permissions": ["members.view", "members.invite"],
  "session": { "expiresAt": "2026-08-31T18:00:00Z", "requiresTwoFactor": false }
}
```

The client uses `permissions` only for UX. It always handles `401`, `403`, `404`, `409`, and `429` because the backend reauthorizes every operation. Active tenant state is never derived from React state or a client-supplied header.

## 8. Initial web security contract

- Same-origin BFF; no open CORS and no bearer token for the SPA.
- `GET /api/identity/antiforgery` creates the `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, host-only cookie `__Host-XSRF-TOKEN` and returns `{ "requestToken": "..." }` with `Cache-Control: no-store`. React keeps that request token in memory only and sends it as `X-CSRF-TOKEN` on every state-changing request, including public credential or invitation submissions.
- The server rotates the antiforgery cookie/request-token pair whenever authentication state or the session identifier changes, including successful sign-in and sign-out. The client fetches a fresh pair after initial load, page reload, sign-in, sign-out, or a stable `antiforgery_validation_failed` response; old pairs are rejected after rotation. Neither token is logged or persisted by the client.
- A malformed or missing antiforgery pair on any state-changing Platform request returns RFC 9457 `400` with stable code `antiforgery_validation_failed`; it is never normalized to a business `202`. An exhausted Platform bootstrap recovery limit returns RFC 9457 `429` with `rate_limit_exceeded` and `Retry-After`; only valid state-obscuring recovery outcomes remain neutral `202`.
- Authenticated `POST`, `PUT`, `PATCH`, and `DELETE` requests require antiforgery and exact-origin validation.
- CSP and secure headers are defined before production.
- `PasswordOptions`, lockout, and rate limits are configured and tested.
- Public origin and email links come from allowlisted configuration, never from a request `Host` or `Origin`.
- `TimeProvider` and the cryptographic generator are injectable for tests.
- Secrets are supplied only through secure environment configuration.

## 9. Test strategy

- **Domain:** tenant, membership, role, invitation, and session state and invariants.
- **Application:** validation, permissions, idempotency, public-marker enforcement, and orchestration with TDD.
- **Functional:** use case + pipeline + EF Core + real PostgreSQL.
- **Infrastructure:** mappings, constraints, cookies, Identity, active-session context, and outbox claiming/delivery.
- **HTTP:** status codes, Problem Details, antiforgery bootstrap/rotation, and OpenAPI.
- **React/E2E:** registration, simulated confirmation, sign-in, tenant selector, invitation, acceptance, and isolation between two tenants.
- **Platform:** bootstrap ceremony, MFA enrollment/step-up, last-owner protection, safe projections, conditional Organization suspension, and prohibited-capability negatives.
- **Architecture:** Domain has no external references; endpoints do not access EF directly; no Application business request is public by omission.

Minimum matrix for each protected operation: unauthenticated `401`; missing permission `403`; permitted custom role; suspended membership/tenant; resource from another tenant; permission revoked after a previously authorized request.

## 10. First-increment acceptance criteria

```gherkin
Scenario: One identity operates in two organizations without mixing permissions
  Given a confirmed identity with membership in Organization A and Organization B
  And the identity has members.invite only in Organization A
  When the identity selects Organization B and attempts to invite
  Then the API responds 403
  And it creates no Invitation or OutboxMessage

Scenario: An invitation is accepted once
  Given an active invitation for ana@example.com
  And Ana is authenticated with that confirmed email
  When Ana accepts the same token twice
  Then exactly one membership exists
  And the second response is idempotent and creates no duplicate effects

Scenario: A revoked session no longer authenticates
  Given a valid cookie referencing an active UserSession
  When the session is revoked
  Then the next request responds 401

Scenario: Startup preserves data
  Given a migrated PostgreSQL database with an existing organization
  When the application restarts in Development
  Then the organization still exists
  And no default administrator is created

Scenario: Semantic success does not expose internal Result
  Given an endpoint completes successfully
  When Web writes its response
  Then it uses its declared DTO, semantic status, and required headers
  And it serializes neither internal Result nor a universal success envelope

Scenario: Expected failure becomes Problem Details
  Given Application returns a typed expected failure
  When Web maps the result
  Then the response is RFC 9457 application/problem+json with matching status, stable code, and opaque traceId
  And only validation failures contain field-indexed errors

Scenario: Unexpected failure is safe
  Given an unexpected infrastructure or programmer exception
  When the central handler writes the response
  Then it returns generic application/problem+json with status 500 and an opaque traceId
  And it exposes no secret or internal diagnostic

Scenario: Contract drift is rejected
  Given runtime responses, OpenAPI, and the React API boundary
  When their statuses, headers, schemas, or codes diverge
  Then contract verification fails before acceptance

Scenario: Bootstrap creates the first Platform owner once
  Given no Platform tenant and an explicitly configured deployment-owner email
  When the bootstrap ceremony runs twice
  Then one Platform tenant, pending owner invitation, outbox intent, and audit decision exist
  And changing the configured email after activation creates no administrator

Scenario: Platform mutation requires normal MFA-bound authority
  Given a confirmed Platform administrator with an active Platform membership
  And the administrator has completed TOTP, acknowledged recovery codes, and recently stepped up
  When the administrator suspends an Organization with the required permission
  Then the conditional change is audited and immediately affects authorization
  And no Platform tenant, private tenant data, or bypass is exposed

Scenario: Last Platform owner cannot be revoked
  Given exactly one active Platform owner
  When an administrator attempts to revoke that membership
  Then the API returns stable RFC 9457 Problem Details
  And the membership remains active

Scenario: Pending bootstrap invitation recovers without changing authority
  Given the configured bootstrap email has the only pending owner invitation and no owner activated
  And that invitation is expired or permanently failed
  And no ApplicationUser exists for that recipient
  When concurrent bodyless same-origin antiforgery recovery requests are rate-limited
  Then one replacement invitation/token, outbox effect, and audit record set is current
  And no requester email, identity, replacement recipient, membership activation, or elevation is accepted

Scenario: Platform invitee reaches MFA without early activation
  Given a bootstrap owner or later Platform administrator has a pending Platform invitation
  And no matching ApplicationUser exists, or the matching identity is unconfirmed
  When the recipient registers with the invitation token and a PasswordOptions-valid password, then confirms through the confirmation outbox
  Then the system creates the missing global identity with that submitted password, or ignores credentials for an existing identity, and returns neutral responses
  And after normal password sign-in the confirmed matching identity may complete invitation-bound MFA
  But no Platform membership becomes active before the MFA gates complete

Scenario: Platform directories are bounded operational projections
  Given a recent-MFA Platform administrator with the directory permission
  When the administrator requests `/api/platform/admins` with an opaque cursor and bounded limit
  Then the typed directory returns only its declared allowlisted fields and `nextCursor`
  And no `/api/identity/*` contract, private profile field, credential, token, CUIT, or audit payload is exposed
```

## 11. Deployment decisions and remaining open decisions

- Production email delivery uses Resend's REST API with an externally supplied domain-scoped Sending access API key. The maintainer delegated this implementation choice for the Task 10/11 corrections. No account, paid subscription, DNS change, or real send is part of local implementation. Identical message-ID replay is bounded by the provider's 24-hour retention; the worker fails closed after that window or on payload/credential drift. A credential-derived hash participates only in the combined request fingerprint; no API key is persisted with the message.
- `OutboxSecret` key wrapping outside explicit `Development`, `Test`, and `Testing` environments uses an externally configured X.509 certificate and a shared durable ASP.NET Core Data Protection key repository. This includes Production, Staging, and custom deployment environments. Web and worker use the exact same application discriminator; existing deployments preserve their previous discriminator and key material. Configuration and activation prerequisites are documented in [EMAIL-SETUP.md](EMAIL-SETUP.md).
- Legal PII policy before enabling `Personal` tenants and real DNI values.
- Redis or another distributed cache; the first version may resolve permissions from PostgreSQL and add caching only after measurement.

The remaining open decisions must be resolved before the slice that consumes them. Deployment prerequisites and provider activation remain operator-owned and do not authorize insecure fallbacks.

## 12. SDD as the repository's standard practice

For every feature or slice:

1. **Specify:** create or update a specification with requirements, invariants, contracts, errors, and acceptance examples.
2. **Approve:** a person approves scope and decisions affecting business or security. `Proposed` status does not authorize dependent code.
3. **Plan:** write small tasks with files, a RED test, the GREEN implementation, refactoring, and a verification command.
4. **Implement:** execute one vertical slice at a time with TDD.
5. **Trace:** maintain requirement → task → test → evidence.
6. **Verify:** run build, unit tests, functional tests against real PostgreSQL, contract tests, and E2E tests according to risk.
7. **Record:** append evidence and update the specification/ADR when accepted behavior changes.

This protocol is independent from the reference repository's workflow and preserves its most important property: behavior is specified and approved before code is written.

## 13. Definition of Done per slice

- requirement and permission are traced;
- the test is written first and RED/GREEN evidence is recorded;
- tenant validation and cross-tenant cases are covered;
- constraints, indexes, delete behavior, and concurrency are explicit;
- API, Problem Details, and OpenAPI are updated;
- a React screen is included when behavior is user-visible;
- secrets and PII are absent from logs and contracts;
- audit and outbox behavior are included when applicable;
- build completes without warnings and relevant suites pass;
- deferred controls and limitations are documented without false compliance claims.
