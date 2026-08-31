# Multitenant Identity Access — Specification

**Status:** Proposed for approval  
**Date:** 2026-08-31  
**Functional source:** external `CleanArchitecture` reference repository, `docs/standards/identity-access`  
**Related decision:** [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md)

## 1. Objective

Turn this starter into a reusable identity and access foundation for multitenant SaaS applications with an ASP.NET Core backend, PostgreSQL, and React web. Identity is global; access is resolved within an active tenant through memberships, roles, and permissions. The system supports invitations, revocable sessions, and auditability without delegating business rules to ASP.NET Core Identity or the frontend.

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
10. deliver React screens and tests for the complete journey.

### 2.3 Required roadmap outside the first increment

The following capabilities remain part of the baseline but are delivered in later slices:

- `Personal` tenant, `PersonProfile`, and protected `AR/DNI` document data;
- password recovery and password change;
- TOTP, recovery codes, and reauthentication;
- Google OIDC sign-in and secure account linking;
- custom roles and complete membership administration;
- reserved `Platform` tenant, bootstrap without default credentials, and super-administration;
- lifecycle/recovery, retention, and advanced operational controls from the standard.

Until the applicable controls are complete, the starter must not claim full baseline compliance or enable real PII or production use.

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

`TenantId` is used for tenancy. `ClientId` remains reserved for OAuth/OIDC. B2C and B2B describe operating contexts, not user types.

## 4. Normative requirements

### Identity and registration

- **IA-REQ-001:** one normalized email identifies at most one active local identity.
- **IA-REQ-002:** `ApplicationUser` does not contain `TenantId`, role, DNI, CUIT, or a B2C/B2B discriminator.
- **IA-REQ-003:** organization registration deterministically follows the caller state below. Every branch that creates an organization writes the identity decision, tenant, organization profile, responsible membership, initial roles, audit, and applicable confirmation/outbox intent in one consistency boundary.
  - An anonymous request for an email with no identity creates an unconfirmed identity plus a pending organization and responsible membership, then returns the neutral `202` response.
  - An anonymous request for an email that already belongs to an identity creates no identity, tenant, membership, or role. It returns the same neutral `202` response and may enqueue a generic sign-in or confirmation notice; the caller must authenticate before creating another organization.
  - An authenticated request creates another organization for the current identity. Any submitted email must normalize to that identity's email; otherwise the request is rejected. The new tenant and responsible membership belong to the authenticated identity only.
- **IA-REQ-004:** a partial failure never leaves an organization without its responsible membership. Repeated equivalent submissions are protected by database uniqueness and an application idempotency boundary.
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
- **IA-REQ-017:** creating or reissuing an invitation and its outbox message is atomic; unlimited equivalent active tokens are not allowed.
- **IA-REQ-018:** the usable email token never appears in an outbox payload. It is stored in a separate encrypted, expiring `OutboxSecret` envelope referenced by an opaque identifier. The delivery handler leases the outbox message, loads and decrypts the envelope only in memory, renders the configured email adapter or isolated test sink, and sends with the outbox message ID as its idempotency key. After acknowledged delivery, one local transaction marks the message delivered and terminalizes the envelope by clearing ciphertext and recording non-secret delivery evidence. Retries consult the message state and adapter delivery receipt so the same logical message is not sent twice. Expired or permanently failed envelopes are terminalized without exposing the token. Raw tokens are forbidden in outbox payloads, audit, application logs, Problem Details, and delivery telemetry.

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

## 5. Conceptual model

```text
ApplicationUser 1---0..1 PersonProfile
ApplicationUser 1---* TenantMembership *---1 Tenant
Tenant 1---0..1 OrganizationProfile
Tenant 1---* Role *---* Permission
TenantMembership *---* Role
Tenant 1---* Invitation *---* Role
ApplicationUser 1---* UserSession
Invitation 0..1---1 ApplicationUser (AcceptedBy)
OutboxMessage 1---0..1 OutboxSecret
Tenant 1---* AuditEvent
```

Initial aggregates:

- `Tenant`: type, state, slug, and authorization version.
- `TenantMembership`: membership, state, and role assignments.
- `Role`: name, `SystemCode`, and permissions within one tenant.
- `Invitation`: recipient, token hash, expiry, state, and offered roles.
- `UserSession`: idle/absolute lifetime, active tenant, and revocation.

## 6. Initial HTTP contract

Routes are contractual drafts; generated OpenAPI becomes the implementation source of truth.

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/identity/antiforgery` | Public bootstrap | `200` request-token DTO + antiforgery cookie |
| `POST /api/identity/organizations/register` | Public or authenticated + antiforgery | neutral bodyless `202` |
| `POST /api/identity/confirm-email` | Public + token + antiforgery | idempotent bodyless `204` |
| `POST /api/identity/sessions` | Public + antiforgery | bodyless `204` + cookie or Problem Details |
| `DELETE /api/identity/sessions/current` | Authenticated + antiforgery | bodyless `204` |
| `GET /api/identity/context` | Authenticated | `200` identity-context DTO |
| `PUT /api/identity/context/tenant` | Authenticated + antiforgery | `200` updated identity-context DTO |
| `POST /api/tenants/{tenantId}/invitations` | `members.invite` + antiforgery | `201` invitation DTO + `Location` |
| `POST /api/invitations/register` | Public + invitation token + antiforgery | neutral bodyless `202`; registration/confirmation only |
| `POST /api/invitations/accept` | Authenticated + token + antiforgery | idempotent `200` acceptance DTO |

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
```

## 11. Open decisions that do not block the first plan

- Production email provider and its managed identity.
- Key-wrapping provider for `OutboxSecret` outside Development/Test.
- Legal PII policy before enabling `Personal` tenants and real DNI values.
- Redis or another distributed cache; the first version may resolve permissions from PostgreSQL and add caching only after measurement.

These decisions must be resolved before the slice that consumes them. They do not authorize insecure fallbacks.

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
