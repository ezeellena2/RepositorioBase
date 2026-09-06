# Multitenant Identity Access — Specification

**Status:** Proposed for approval  
**Date:** 2026-08-31  
**Functional source:** external `CleanArchitecture` reference repository, `docs/standards/identity-access`  
**Related decision:** [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md)

Implementation status: the defects R1-R7 and L1 of the [2026-09-05 direct review](CODE-REVIEW-2026-09-05.md) are
corrected, each with regression evidence recorded in [TRACEABILITY.md](TRACEABILITY.md). This specification remains
the target; correcting them does not establish full B2B/B2C baseline compliance, and IA-010, IA-011, IA-013 and
IA-015 remain unimplemented.

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

Until the applicable controls are complete, the starter must not claim full baseline compliance or enable real PII or production use. The contracts these slices need are proposed for approval in §14 and are not approved by being written there.

### 2.4 Explicit non-goals

The initial Platform slice does not permit impersonation, destructive deletion, tenant-private or business-data reading, a client-selected tenant context, a global boolean/claim/role bypass, or a default administrator credential.

### 2.5 Continuation scope and its three separate gates

The roadmap in §2.3 is now planned as Tasks 17–28 of the
[implementation plan](../../superpowers/plans/2026-08-31-identity-access-foundation.md#continuation-to-local-b2bb2c-functional-completion).
Task 17 is the decision: §14 below holds its proposed contracts, and none of them is approved by being written
down. Until a contract is approved, its dependent task is not `Ready`; if one is declined or amended, only the
tasks that consume it change.

Three outcomes are deliberately kept apart, and reaching one never authorizes the next:

1. **Local functional closure with synthetic data.** Every journey works end to end against fictitious people,
   fictitious documents and an isolated mail sink. This is what Tasks 18–28 can close on their own.
2. **Use of real personal data.** Requires the responsible human or legal owner to approve purpose, field scope,
   access, retention periods, legal holds, deletion evidence and backup/restore treatment (IA-REQ-056). Until
   then `Personal` runs on synthetic fixtures and real collection is refused by configuration that fails closed.
3. **Production deployment and full reference compliance.** Requires the restore admission authority and its
   external record (IA-REQ-055), per-environment key and certificate ownership, and the abuse budgets recorded
   for that environment. A green local suite is evidence for outcome 1 only.

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
- **IA-REQ-003:** organization registration deterministically follows the caller state below, and no branch reachable without proof of control of the submitted address creates an exclusive durable claim on a CUIT, identity, tenant, organization profile, membership or role.
  - An anonymous request records one bounded, expiring `PendingRegistrationIntent`, enqueues one outbox message to the submitted address and returns the neutral `202`. It creates no identity, tenant, organization profile, membership, role or CUIT claim, and no state another caller can observe or be refused by. It behaves identically whatever the system knows of that address and that CUIT; only the delivered message differs — a confirmation link carrying the intent's single-use token when the address has no identity, and a tokenless sign-in notice when it already has one, which only that address can read.
  - An authenticated request creates another organization for the current identity. Any submitted email must normalize to that identity's email; otherwise the request is rejected, and a session the server cannot validate is rejected as invalid rather than treated as anonymous. The new tenant and responsible membership belong to the authenticated identity only. A request whose normalized CUIT already belongs to an organization creates nothing and returns `409` `registration_conflict`: that caller may register only for the address their own session proves, so nothing they can vary asks about anybody else — and it can no longer be composed with an anonymous probe, because the anonymous phase leaves no claim for this answer to depend on.
  - Spending an intent's token proves control of the submitted address, and only that proof may create the identity, tenant, organization profile, responsible membership, initial roles, audit and outbox effects. They are written in one consistency boundary: every branch that creates an organization writes that whole graph or none of it. A proof that arrives after the CUIT was taken still creates and activates the identity, because the person proved their own address; the organization is refused with `409` `registration_conflict`. A proof that arrives after the address gained an identity creates nothing and never applies the submitted password to an account the caller may not own.
- **IA-REQ-048:** an exclusive durable reservation — a normalized CUIT, an organization profile, a tenant, a membership, a role, a protected documentary identity or a global email identity — is created only by a request that has proved control of the identity it will belong to, and exactly two proofs qualify: a validated persisted session, or a single-use token delivered to that address and spent by the request. What an unproven request leaves behind is observable to nobody but the address owner and an operator: no other caller may be refused by it, answered differently because of it, or able to read it.
- **IA-REQ-004:** a partial failure never leaves an organization without its responsible membership, and neither registration phase replays into a second graph. The Application registration service derives a canonical equivalent-submission key from normalized caller scope and normalized registration intent, claims it before effects, and stores the completed neutral response, so replayed anonymous initiation returns the same bodyless `202` and produces one intent, one outbox message and one audit record. Finalization is idempotent on its single-use token: the first spend records the intent's terminal outcome and every later spend returns that recorded outcome — including the conflict, because both terminal outcomes spend the envelope and only the intent distinguishes them. Database uniqueness remains a backstop, not the idempotency mechanism.
- **IA-REQ-005:** email must be confirmed before inviting members, administering roles, accepting an invitation, or performing a sensitive operation. The delivered confirmation link resolves to a screen that spends its token against `POST /api/identity/confirm-email`; the confirmation messages — an anonymous registrant's, an authenticated registrant's and an invited member's — carry the same link, because all three consume that endpoint.
- **IA-REQ-050:** an identity that owns a `Personal` tenant has exactly one `PersonProfile`, keyed by the identity and readable and editable only by its owner. Self-service editing covers `FullName` and `DisplayName` and nothing else — never the email, the profile's ownership, the document country, type or number — and a request naming any member outside the accepted set is refused whole. The documentary identity is an authenticated-encryption ciphertext of the canonical `country|type|number` tuple plus one keyed, versioned fingerprint row per retained key version; the plaintext leaves the protector at exactly two named seams and reaches no log, audit record, outbox payload, Problem Details body, OpenAPI example or response. The owner sees only a masked status, and no route outside the owner's own resolves a `PersonProfile` or an `IdentityDocument`. A documentary identity is unique among unpurged rows across all retained key versions, and every refusal that would otherwise disclose another identity's data — a duplicate document, or an identity that already owns a `Personal` — answers one indistinguishable `409` `personal_registration_conflict`. The route table, contention table and permission set are in [section 14.3](#143-c3--own-profile-and-protected-ardni-documentary-identity).
- **IA-REQ-058:** a recorded documentary identity is corrected only by a verified two-party process and never edited in place: the owner opens a dispute over their own document under a live recent proof and never writes a document value, and a Platform operator resolves it under a distinct permission, a recent second factor and an external evidence reference — never for their own identity, and never without a stored dispute. No route writes a document value outside that process. A `corrected` resolution replaces the ciphertext and every retained fingerprint row in one transaction and writes an append-only correction record holding no document value. At most one dispute per identity is open at a time, and a dispute changes nothing about access while it is open. The full contract is in [section 14.3](#143-c3--own-profile-and-protected-ardni-documentary-identity).

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
- **IA-REQ-049:** an identity may hold at most five live `UserSession` rows at any committed instant **(product default)**. Issuance is serialized per identity; at the cap it revokes the oldest live sessions in `(CreatedAt ascending, Id ascending)` order until four remain and commits the new session in the same consistency boundary. Authentication does not revoke the identity's other live sessions. An identity may list, revoke individually and revoke collectively its own sessions through self-service requests that resolve the owner only from the validated persisted session and address rows by an opaque reference that is never the identifier the cookie carries. A password reset revokes every persisted session and issues none; an authenticated password change revokes every other live session and rotates the acting one into a new row that inherits nothing — not its identifier, its antiforgery pair, a recent identity proof or second-factor evidence. Every transition that happened is audited once and a denied revoke once as a denial, and no such record carries an IP address, a raw `User-Agent`, a cookie, a ticket, a proof value or another identity's row. The route table, contention table and device-label rules are in [section 14.2](#142-c2--session-coexistence-deterministic-cap-eviction-and-own-session-revocation).
- **IA-REQ-051:** a sensitive self-service change requires a recent identity proof: a single-use server-side record bound to one identity, one `UserSession`, one action and the identity's security version at issue, living five minutes **(product default)**. A valid session cookie alone is never proof. Only the identity's current password or a fresh challenge to a provider already linked to it issues one, both requiring a confirmed identity, and a proof never travels to the client: the request looks up the live unconsumed proof for `(identity, current session, action)` and consumes it with a conditional update. Every credential or authenticator change increments a Domain-owned security version that invalidates every outstanding proof. Platform step-up stays separately session-bound and neither proof satisfies the other. The proof-requiring actions and the contention rules are in [section 14.4](#144-c4--recent-identity-proof-password-recovery-and-provider-linking-with-a-two-part-callback-carve-out).
- **IA-REQ-052:** an identity may hold at most one link per external provider, established only by explicit consent plus a recent primary proof, a confirmed identity and a provider-verified email. A provider identity is never auto-linked by a matching email address (BR-ID-005/006) — and an authenticated, confirmed identity that links its own provider account with consent and a live proof is not auto-linking, so it is never refused merely because the provider's verified address is the one it already owns. No unlink may leave an identity without a usable authenticator. `Login`, `Link`, `Proof` and `Recovery` purposes live in server-side state and never cross; `Recovery` is the one purpose bound to no session, and it exists for a person who cannot obtain one. The provider callback is the one documented exception to IA-REQ-022, to section 8 and to the rule that a usable token never travels in a URL query string: it performs no business mutation, and the validations replacing the origin check are one-use purpose-bound `state`, the framework correlation cookie, `nonce`, PKCE `S256` with a server-held verifier, and validated issuer, audience, signature and expiry.

### Audit, outbox, and security

- **IA-REQ-026:** sign-in, confirmation, sessions, invitations, memberships, roles, and sensitive denials generate append-only `AuditEvent` records with a correlation ID and no secrets.
- **IA-REQ-027:** every reliable external effect writes an `OutboxMessage` in the same transaction as the business change.
- **IA-REQ-028:** the worker claims messages using a lease and compare-and-swap, retries with backoff, and uses idempotent handlers.
- **IA-REQ-029:** passwords, tokens, cookies, codes, connection strings, and PII do not appear in logs, Problem Details, audit records, or outbox payloads.
- **IA-REQ-030:** `401` means an absent or invalid identity; `403` means a valid identity without permission. Cross-tenant access may return `404` to avoid revealing a resource.
- **IA-REQ-031:** CORS does not use `AllowAnyOrigin`; a same-origin SPA needs no open production policy.
- **IA-REQ-032:** the project creates no default administrator or password, and startup never deletes the database.
- **IA-REQ-057:** every attempt budget that bounds guessing is held in shared PostgreSQL state, one budget across instances and restarts, behind the port `ISharedAttemptBudget` with `PostgreSqlAttemptBudget` as its only adapter over the existing database; no new infrastructure product is introduced. An unavailable store fails every budget closed — no attempt is admitted — and the caller is told what actually happened: `503` Problem Details `service_unavailable` with `Retry-After`, never `429` `rate_limit_exceeded`, which stays reserved for a budget a caller really did exhaust. Every route that names a budget carries both answers. Budget writes commit outside any business transaction, and numerical budgets are product defaults recorded per environment.

### Persistence

- **IA-REQ-033:** PostgreSQL constraints protect uniqueness for email, slug, CUIT, membership, and token hash.
- **IA-REQ-034:** membership/role and invitation/role associations use composite keys and foreign keys containing `TenantId`, so PostgreSQL rejects cross-tenant combinations.
- **IA-REQ-035:** sensitive mutations use a concurrency token or conditional update and translate a lost update into `409 Conflict`.
- **IA-REQ-036:** every foreign key declares delete behavior; cascade is not used where it could erase identity, authorization, or audit history.
- **IA-REQ-037:** migrations are tested from an empty database and from the previous version. `EnsureDeleted`/`EnsureCreated` is not a normal migration strategy.
- **IA-REQ-056:** the deployment holds exactly one personal-data mode, and the mode — never a caller, never a request field — determines what the system may do with the personal data it stores. `IdentityAccess:PersonalData:Mode` is `Synthetic` or `Real`; absent, blank or unparseable is `Synthetic`, so real handling is never reached by omission or by a typo. Every `PersonProfile` and document row is stamped at creation with the mode as a persisted server-derived `DataClassification`, a column on no request DTO and never on `ApplicationUser`. A retention policy is configuration, never code, and contains no period, threshold or jurisdictional number; with none configured the system performs no destructive action in either mode. A purge erases ciphertext and keyed fingerprint, writes a durable non-audit erasure record and leaves a non-identifying tombstone, so a purged number is reclaimable. A legal hold stops erasure and nothing else: it is not an account state, it suspends nobody, it refuses no sign-in and no authorization decision reads it. The retention endpoints, hold record and erasure evidence are in [section 14.7](#147-c7--personal-data-mode-retention-and-erasure-evidence-and-shared-abuse-control-state).

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
| `POST /api/identity/organizations/register` | Public or authenticated + antiforgery | neutral bodyless `202` for every anonymous request, whatever the address and whatever the CUIT, reserving nothing; an authenticated request for an already registered CUIT is `409` Problem Details `registration_conflict`; a submitted email that does not normalize to the session's is `400` `invalid_registration`; a session the server cannot validate is `401` `invalid_session` |
| `POST /api/identity/personal/register` | Public + antiforgery, `IPublicRequest` | neutral bodyless `202` for every anonymous request, whatever the address, reserving no documentary identity — the sealed document is held in the intent and no fingerprint row is written until the address is proved; `400` `invalid_registration` for malformed input, for a password the policy refuses and for an authenticated caller, who has a route that needs no mailed token; `401` `invalid_session` for a session the server cannot validate |
| `POST /api/identity/personal` | Authenticated + `identity.profile.manage` (`RequiresTenant=false`) + antiforgery | bodyless `204`; `409` `personal_registration_conflict` for every reason the context cannot be recorded, indistinguishably; `429` `rate_limit_exceeded` + `Retry-After` for an exhausted claim budget; `503` `service_unavailable` + `Retry-After` when the shared budget store is unreachable |
| `GET /api/identity/profile` | Authenticated + `identity.profile.read` (`RequiresTenant=false`); no subject parameter | `200` `PersonalProfileResponse` carrying `fullName`, `displayName`, `email`, `personalTenantId`, `document { country, type, status, maskedNumber, correctionAvailable }` or `null`, an opaque `version` and `updatedAt`; `404` `personal_profile_not_found` |
| `PUT /api/identity/profile` | Authenticated + `identity.profile.manage` (`RequiresTenant=false`) + antiforgery | `200` updated `PersonalProfileResponse`; `400` `profile_field_not_editable` naming the rejected member and never its value; `404` `personal_profile_not_found`; `409` `personal_profile_concurrency_conflict` |
| `POST /api/identity/confirm-email` | Public + token + antiforgery | bodyless `204` when the registration finalizes or already did; `409` Problem Details `registration_conflict` when the CUIT was taken first, when the address gained an identity first, or when the envelope expired — including on replay; `400` `invalid_confirmation` for an unknown or malformed token |
| `POST /api/identity/sessions` | Public + antiforgery | bodyless `204` + cookie or Problem Details |
| `DELETE /api/identity/sessions/current` | Authenticated + antiforgery | bodyless `204`; a session already revoked by a parallel request is `401` `invalid_session` and still deletes the cookie; a lost update that never settles is `409` `session_concurrency_conflict` |
| `GET /api/identity/context` | Authenticated | `200` identity-context DTO |
| `POST /api/identity/credentials/password/recovery`; `POST /api/identity/credentials/password/reset` | Public + antiforgery; recovery rate-limited | neutral bodyless `202` whatever the address and whatever the account state; bodyless `204`, `400` `invalid_credential_token` for unknown, expired, consumed, superseded or wrong-purpose, `400` `validation_failed` for a password the policy refuses |
| `POST /api/identity/credentials/reauthenticate`; `PUT /api/identity/credentials/password` | Authenticated + antiforgery + `identity.credentials.manage`, `RequiresTenant=false`; the change additionally needs a live proof | bodyless `204`; `400` `invalid_credential_proof`; `401` `recent_proof_required`; the change answers `204` with a rotated session cookie and antiforgery pair |
| `GET /api/identity/credentials` | Authenticated + `identity.credentials.manage`, `RequiresTenant=false`; no subject parameter | `200` `{ hasPassword, passwordUpdatedAt }` and nothing else — never a hash, an address or a provider name, and never about anybody but the caller |
| `POST /api/identity/external/{provider}/login/start` | Public + antiforgery, rate-limited | `200` `{ authorizationRequestUri }` + `Cache-Control: no-store` and a sealed handoff cookie; `400` `invalid_external_login` for a provider this deployment does not offer |
| `POST /api/identity/external/{provider}/link/start`, `/proof/start` | Authenticated + antiforgery + recent proof (link) + `identity.external.manage` / `identity.credentials.manage`, `RequiresTenant=false` | `200` `{ authorizationRequestUri }`; `401` `recent_proof_required`; `409` `provider_already_linked`; `404` `not_found` when a proof is asked for a provider this identity has not linked |
| `POST /api/identity/external/complete` | Same-origin + antiforgery + the sealed handoff cookie, which is also what selects the request; no body | per the purpose table in [14.4](#144-c4--recent-identity-proof-password-recovery-and-provider-linking-with-a-two-part-callback-carve-out) |
| `GET /api/identity/external`; `DELETE /api/identity/external/{provider}` | Authenticated + `identity.external.manage`, `RequiresTenant=false`; the delete additionally needs antiforgery and a recent proof | `200` `{ items: [{ handle, provider, providerEmail, linkedAt }] }`; bodyless `204`, `409` `last_authenticator_required`, `401` `recent_proof_required`, `404` `not_found` |
| `PUT /api/identity/context/tenant` | Authenticated + antiforgery | `200` updated identity-context DTO; a lost update that never settles is `409` `session_concurrency_conflict` |
| `POST /api/tenants/{tenantId}/invitations` | `members.invite` + antiforgery | `201` invitation DTO + `Location` |
| `POST /api/invitations/register` | Public + invitation token + antiforgery | neutral bodyless `202`; registration/confirmation only |
| `POST /api/invitations/accept` | Authenticated + token + antiforgery | idempotent `200` acceptance DTO |
| `POST /api/platform/bootstrap/recover` | public bodyless same-origin + antiforgery + rate limit; no identity, email, or replacement recipient input | valid opaque states: neutral bodyless `202`; missing/malformed antiforgery: `400` Problem Details `antiforgery_validation_failed`; exhausted limit: `429` Problem Details `rate_limit_exceeded` + `Retry-After` |
| `POST /api/platform/invitations/register` | public + Platform invitation token + credential-registration DTO + antiforgery | neutral bodyless `202`; missing identity uses submitted PasswordOptions-valid password, existing identity ignores credentials; issue confirmation, never membership |
| `POST /api/platform/invitations/confirm` | public + one `confirmationToken` + antiforgery; its sealed envelope identifies the invitation and bound recipient | idempotent bodyless `204`; validates the pending invitation/identity binding and confirms identity, never membership |
| `POST /api/platform/mfa/enroll`, `/verify`, and `/recovery-acknowledge` | authenticated, confirmed pending Platform invitee whose normalized email matches its bound one-time invitation token + antiforgery; no active Platform tenant is required before activation | enrollment DTO, then bodyless `204`; `/verify` additionally answers `429` Problem Details `rate_limit_exceeded` + `Retry-After` once the identity's verification attempts are exhausted |
| `POST /api/platform/mfa/step-up` | Platform administrator + antiforgery | bodyless `204` or Problem Details, including `429` `rate_limit_exceeded` + `Retry-After` on the same per-identity budget as `/verify` |
| `GET /api/platform/organizations` with `limit`/`cursor` | active Platform tenant + `platform.organizations.read` + this session has proved the second factor | `200` typed `{ items: PlatformOrganizationResponse[], nextCursor }`, or `401` Problem Details `recent_mfa_required` |
| `GET /api/platform/identities` with `limit`/`cursor` | active Platform tenant + `platform.identities.read` + this session has proved the second factor | `200` typed `{ items: PlatformIdentityResponse[], nextCursor }`, or `401` Problem Details `recent_mfa_required` |
| `GET /api/platform/admins` with `limit`/`cursor` | active Platform tenant + `platform.admins.read` | `200` bounded `{ items, nextCursor }` administrator directory DTO |
| `GET /api/platform/audit` with `limit`/`cursor` | active Platform tenant + `platform.audit.read` | `200` typed `{ items: PlatformAuditEventResponse[], nextCursor }` |
| `POST /api/platform/organizations/{tenantId}/suspend`, `/reactivate` | `platform.tenants.manage` + recent MFA | bodyless `204` or `409` Problem Details |
| `POST /api/platform/admins/invitations`; `POST /api/platform/admins/{membershipId}/revoke` | `platform.admins.manage` + recent MFA | neutral `202` / bodyless `204` |

All non-success responses follow IA-REQ-038. Sign-in, registration, recovery, and invitation flows do not unnecessarily reveal whether an email exists.

Reading a Platform directory requires that the requesting session has proved the second factor at least once; changing something additionally requires that it did so recently (IA-REQ-041/045). The two are deliberately different questions of the same evidence: password sign-in selects a sole active tenant on its own, so tenant and permission alone would admit a session that proved nothing, while asking for freshness on every read would re-prompt an administrator mid-task. `POST /api/platform/mfa/step-up` is what a session with neither answers, and it requires no Platform tenant of its own so it stays reachable.

An authentication cookie whose session is rejected is deleted in the same response that rejects it. Rejection is unchanged — the request stays anonymous and no rejected session is ever accepted — but the browser stops presenting a ticket it cannot use and public flows, which refuse a cookie they cannot validate, become reachable again.

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
  "session": { "expiresAt": "2026-08-31T18:00:00Z", "requiresTwoFactor": false },
  "personalData": { "mode": "Synthetic" }
}
```

`displayName` prefers the person's own `PersonProfile.DisplayName` and falls back to the email only for an identity
that has not told us a name. The email stays the identifier; it is not a name, and showing it where a name belongs
puts an address on a screen somebody else can see (IA-REQ-050).

`personalData.mode` is the deployment's own, derived by the server and never a request field, so a client can say
what the deployment is doing with personal data instead of assuming (IA-REQ-056).

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
- Legal PII policy before enabling `Personal` tenants and real DNI values. Proposed IA-REQ-056 (§14.7) states the
  contract this policy must fill and keeps `Personal` on synthetic fixtures until a named owner approves it; the
  policy itself is not proposed here, and no jurisdictional period is invented.
- Redis or another distributed cache; the first version may resolve permissions from PostgreSQL and add caching only after measurement. Proposed IA-REQ-057 (§14.7) keeps shared abuse-control state on the existing PostgreSQL stack for the same reason and adds no product.
- Per-environment records that proposed §14 requires before the slice that consumes them: the abuse budgets and store timeout (IA-REQ-057), Data Protection key and certificate ownership (already §11 above), the OIDC client registration and its redirect URIs (IA-REQ-052), and the restore admission authority and its key (IA-REQ-055). Each is operator-owned; none is satisfied by a local run.

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

## 14. Task 17 decision package (proposed, not approved)

> **C1, C2, C3, C4 AND C7 ARE ACCEPTED (2026-09-06). C5 AND C6 ARE NOT.** Sections 14.1, 14.2, 14.3, 14.4 and 14.7
> are accepted and their requirements now live in section 4; those subsections are kept as the record of the
> decisions that produced them, and section 4 governs where the wording differs. Every acceptance is **for
> implementation and verification against synthetic data only**: real personal data and production deployment are two
> separate gates, and neither is granted by any of them. Sections 14.5 and 14.6 are still only proposed. Amendments
> A1–A5 from
> [ADR-004's decision record](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md#decision-record--2026-09-06)
> were folded into 14.3, 14.4, 14.6 and 14.7 before any of them was decided; A2 and A3 are part of what C4's
> acceptance carries, while A3's other half and A4 still await C6. Read the rest of this banner as applying to 14.5
> and 14.6 only. This section is the Task 17 decision package, entries C1–C7,
> put forward for approval. No behaviour described here exists; no requirement, permission code, stable error code,
> route, DTO field, enum member or state name here is implemented; no test named here has been written or run, so
> nothing here is evidence, and `Proposed` status authorizes no dependent code (section 12). Numbers marked
> **(product default)** are this project's choices, not legal or external-standard requirements. IA-REQ-048..058
> are allocated once here; C7's fragment numbered its two requirements 048/049 provisionally, and IA-REQ-058 was
> added on 2026-09-06 by amendment A1.

### 14.1 C1 — Registration reservation: no exclusive durable claim before proved control of the address

> **Accepted 2026-09-06 and implemented by Task 18.** Section 4 now carries IA-REQ-048 and the amended
> IA-REQ-003/004/005; what follows is the decision record that produced them, kept for its state tables and its
> account of the defect. Where this section and section 4 differ in wording, section 4 governs.

- **IA-REQ-048 (accepted; section 4 governs):** an exclusive durable reservation — a normalized CUIT, an organization profile, a tenant,
  a membership, a role, a protected documentary identity or a global email identity — is created only by a request
  that has proved control of the identity it will belong to, and exactly two proofs qualify: a validated persisted
  session, or a single-use token delivered to that address and spent by the request. What an unproven request leaves
  behind must be observable to nobody but the address owner and an operator: no other caller may be refused by it,
  answered differently because of it, or able to read it.
- **IA-REQ-003 (accepted amended wording; section 4 governs):** organization registration deterministically follows the caller
  state below, and no branch reachable without proof of control of the submitted address creates an exclusive
  durable claim on a CUIT, identity, tenant, organization profile, membership or role.
  - An anonymous request records one bounded, expiring, protected `PendingRegistrationIntent`, enqueues one
    outbox message to the submitted address and returns the neutral `202`, creating no identity, tenant,
    organization profile, membership, role or CUIT claim and no state another caller can observe or be refused
    by. It behaves identically whatever the system knows of that address and that CUIT, except in the delivered
    message, the intent's recorded outcome and that message's envelope.
  - An authenticated request creates another organization for the current identity, unchanged: a submitted
    email must normalize to that identity's email, an unvalidatable session is rejected as invalid, and an
    already registered CUIT creates nothing, returning `409` `registration_conflict`.
- **IA-REQ-004 (accepted amended wording; section 4 governs):** a partial failure never leaves an organization without its responsible
  membership, and neither phase replays into a second graph. The canonical equivalent-submission key, derived from
  normalized caller scope and registration intent, is claimed before effects and stores the completed neutral
  response, so replayed anonymous initiation returns the same bodyless `202` and produces one intent, one outbox
  message and one audit record. Finalization is idempotent on its single-use token: the first spend records the
  terminal outcome and every later spend returns it. Database uniqueness is a backstop, not the mechanism.

| # | Phase and caller | Address and CUIT at this moment | Durable effect | Response |
|---|---|---|---|---|
| 1–2 | Initiation, anonymous | no identity; CUIT free or already registered | one `PendingRegistrationIntent` (`Pending`), one `OutboxMessage` `identity.registration.confirmation.requested`, one `OutboxSecret` sealing its single-use token, one tenantless audit event | neutral bodyless `202`. The intent stores `Id`, the owning `RegistrationSubmission` canonical key, `NormalizedEmail`, `NormalizedLegalName`, `NormalizedCuit`, `PasswordHash`, `CreatedAt`, `ExpiresAt` = `CreatedAt + 24 hours` (**product default**; the envelope's governs) and nullable `Outcome`/`CompletedAt`, and carries no unique index on `NormalizedCuit` or `NormalizedEmail`. |
| 3–4 | Initiation, anonymous | has an identity; CUIT free or already registered | one `PendingRegistrationIntent` recorded terminal `Notified`, one `OutboxMessage` `identity.registration.signin.notice.requested` and no `OutboxSecret`, one tenantless audit event | neutral bodyless `202` |
| 5 | Initiation, authenticated, session email matches | the caller's own, proven by the validated session; CUIT free | identity decision, tenant, organization profile, responsible membership, initial roles, audit, confirmation outbox — unchanged, including the `PendingConfirmation` tenant and membership | neutral bodyless `202` |
| 6–8 | Initiation, authenticated: CUIT already registered; submitted email not normalizing to the session's; or a session the server cannot validate or a half-populated one | the caller's own | none in all three | `409` `registration_conflict`; `400` `invalid_registration`; `401` `invalid_session` respectively, the last failing closed and never treated as anonymous |
| 9 | Finalization, holder of the intent token | proven by the token; no identity exists; CUIT free | one transaction: identity created and activated, tenant, organization profile, responsible membership, initial roles, audit, intent `Created` | bodyless `204` |
| 10–11 | Finalization, holder of the intent token | proven; no identity exists and the CUIT was taken since initiation, or an identity now exists for the address (CUIT free or taken) | the identity is created and activated in the first case only, intent `Conflicted`, audit — no tenant, profile, membership or role, no second identity, and the submitted password is never applied to a pre-existing identity | `409` `registration_conflict` |
| 12–14 | Finalization: the same token replayed; a token whose sealed envelope has expired; or an unknown, malformed or non-canonical token | — | the intent's recorded outcome; envelope terminalized `confirmation_expired` and intent `Expired`; nothing at all | `204` after row 9 and `409` `registration_conflict` after rows 10–11; `409` `registration_conflict`; `400` `invalid_confirmation` |
| 15 | Finalization, a pre-upgrade `identity.confirmation.requested` token | — | unchanged: identity activated, tenant and membership activated, secret consumed | bodyless `204`, and `204` on replay |

| Method and route | Access | Primary result |
|---|---|---|
| `POST /api/identity/organizations/register` | Public or authenticated + antiforgery; `IPublicRequest` + `ISensitiveRequest` | neutral bodyless `202` for every anonymous request, whatever the address and whatever the CUIT, reserving nothing; an authenticated request for an already registered CUIT is `409` Problem Details `registration_conflict`; a submitted email that does not normalize to the session's is `400` `invalid_registration`; a session the server cannot validate is `401` `invalid_session` |
| `POST /api/identity/confirm-email` | Public + intent token from the link fragment + antiforgery; `IPublicRequest` + `ISensitiveRequest` | bodyless `204` when the intent finalizes, when a pre-upgrade confirmation activates its identity/tenant/membership, or when either already did; `409` Problem Details `registration_conflict` when the CUIT was taken first, when the address gained an identity first, or when the envelope expired; `400` `invalid_confirmation` for an unknown or malformed token |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Two intents for the same normalized CUIT finalizing concurrently | the transaction committing the `OrganizationProfile` insert first, serialized by a **proposed** finalization-side `pg_advisory_xact_lock` pair over normalized email and normalized CUIT (`IRegistrationIdempotencyStore.CoordinateBusinessIntentAsync`, today called only from `RegisterOrganizationCommandHandler`), with the unique CUIT index as backstop | `409` `registration_conflict`, intent `Conflicted`; its identity is still created and activated, so the person can sign in |
| Two equivalent anonymous initiations, same canonical key | the claim owner, through the existing `INSERT … ON CONFLICT ("CanonicalKey") DO NOTHING` in `RegistrationIdempotencyStore.TryClaimAsync` | the same neutral bodyless `202` replayed from the recorded outcome; one intent, one message, one audit record |
| The same intent token spent twice, sequentially or concurrently; or an intent finalizing while its address gains an identity from an unrelated registration | the first spend, through the existing `IConfirmationSecretStore.GetByVersionedHashForUpdateAsync` row lock on the `OutboxSecret`; and whichever transaction commits the identity first, through the normalized-email half of the proposed lock pair plus the unique normalized-email index, that row lock guarding one token and not one address | the intent's recorded outcome — `204` after `Created`, `409` `registration_conflict` after `Conflicted` or `Expired`; and a `Conflicted` settlement with `409` `registration_conflict` and no second identity |

- **Permissions.** No new code; finalization is authorized by the token it spends. `members.invite` (EXISTS,
  tenant-scoped `Organization`) is the remedy named for a refused registrant, `platform.audit.read` (EXISTS,
  tenant-scoped `Platform`) alone reads the distinguishing outcome, and `identity.organizations.create` (PROPOSED,
  application-scoped) is declined.
- **Audit and outbox (proposed).** Audit `organization.registration.intent.created` (rows 1–4, tenantless,
  `outcome=pending_proof`), `organization.registration.requested` (`outcome=registered` at finalization,
  `outcome=pending_confirmation` on the authenticated branch — a change to today's single value),
  `organization.registration.conflicted` (rows 10–11, `outcome=cuit_taken` or `identity_exists`, never shown to the
  caller) and `identity.confirmed` unchanged, the tenantless events needing a tenantless factory. Outbox
  `identity.registration.confirmation.requested` `{ intentId }` with an `OutboxSecret`,
  `identity.registration.signin.notice.requested` `{ intentId }` without, `identity.confirmation.requested` unchanged;
  both need a handler reading `NormalizedEmail` from the intent.
- **Amends.** IA-REQ-003 loses its first bullet, generalizes its second and is **contradicted** in its third, every
  anonymous request — occupied CUIT included — now writing one intent, one outbox message and one tenantless audit
  event where the approved text promised none; IA-REQ-004's replay promise yields one intent, not one organization;
  IA-REQ-005 gains a third confirmation message type and the `409` answer on `confirm-email`.
- **Consumed by** Task 18; it supplies the registration seam Tasks 19, 20 and 23 build on.

### 14.2 C2 — Session coexistence, deterministic cap eviction, and own-session revocation

> **ACCEPTED 2026-09-06, for synthetic data only.** IA-REQ-049 is now normative in
> [section 4](#4-normative-requirements); what follows is the decision record that produced it, kept for its state,
> route and contention tables, which section 4 points at. Section 4 governs where the wording differs.

- **IA-REQ-049 (accepted 2026-09-06 for synthetic data; section 4 governs):** an identity may hold at most five live `UserSession` rows at any committed instant
  **(product default)**. Issuance is serialized per identity; at the cap it revokes the oldest live sessions in
  `(CreatedAt ascending, Id ascending)` order until four remain and commits the new session in the same
  `IApplicationTransaction`. An identity may list, revoke individually and revoke collectively its own sessions
  through self-service requests resolving the owner only from the validated persisted session. A password reset
  revokes every persisted session and issues none; an authenticated password change revokes every other live session
  and rotates the acting one into a new row inheriting no identifier, antiforgery pair, proof or second-factor
  evidence. Every transition that happened is audited once and a denied revoke once as a denial, and nothing here
  carries an IP address, a raw `User-Agent`, a cookie, a ticket, a proof value or another identity's row.

| Caller state at issuance | Live sessions at `now` | Result |
|---|---|---|
| Valid credentials, identity active and email confirmed | 0–4 | one new live session; nothing revoked; other devices stay signed in |
| Valid credentials | 5, or more than 5 from a defect or a lowered cap | the oldest `max(0, live − 4)` live sessions are revoked (`session.revoked` / `evicted`), then one new live session; five remain |
| Valid credentials, per-identity lock not granted within the bounded wait | any | no session is issued; `429` Problem Details `rate_limit_exceeded` with `Retry-After` |
| Valid credentials, security version changed between validation and issuance | any | no session; the generic `invalid_session` answer of IA-REQ-019. **Conditional on C4** supplying that version |
| Invalid credentials, unconfirmed email, locked out, or disabled identity | any | unchanged: no session, no eviction, one `signin.failed` audit row |
| Authenticated password change, or a consumed password reset (Task 22) | any | a change revokes every other live session (`password_changed`), revokes the acting session (`rotated`), issues one replacement and rotates the antiforgery pair, ending at one live session; a reset revokes every live session (`password_reset`) and issues none, the person signing in afterwards |

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/identity/sessions` | Authenticated + `identity.sessions.manage`, `RequiresTenant=false` | `200` `SessionListResponse` carrying exactly `sessionRef`, `isCurrent`, `deviceLabel`, `createdAt`, `lastSeenAt`, `expiresAt` (`IdleExpiresAt`) per live session of this identity, ordered `isCurrent`, then `lastSeenAt` descending, then `sessionRef`, minute-truncated, with no `limit`, `cursor` and no envelope. Backed by the additive `IdentitySessionCoexistence` columns `PublicRef` (128 bits, written once, unique, never the `UserSessionId` the ticket carries) and `DeviceLabel` (closed server-side allowlist, never a request field; the raw `User-Agent` is never persisted). |
| `DELETE /api/identity/sessions/{sessionRef}`; `DELETE /api/identity/sessions/others` | Authenticated + `identity.sessions.manage`, `RequiresTenant=false` + exact-origin + antiforgery; no body | bodyless `204` when a session of this identity carrying that reference is now revoked, including when it was already revoked or expired; `404` Problem Details `session_not_found` when no session of this identity carries it; `409` `session_concurrency_conflict` when the write never settles. Naming the caller's own current session produces exactly the `/sessions/current` contract, including its `401` `invalid_session`, and deletes the cookie and antiforgery pair in the same response. `/others` is idempotent — a repeat with nothing left to revoke is still `204`, writes no `session.revoked` row and never touches the acting session |
| `POST /api/identity/sessions` | Public + antiforgery, unchanged | its existing row, plus `429` `rate_limit_exceeded` with `Retry-After` on an exhausted lock wait; it no longer supersedes the identity's other sessions |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Two sign-ins of one identity at the cap | the first to commit, serialized by a per-identity `pg_advisory_xact_lock(int4, int4)` in a session lock space distinct from the existing one-argument space | both succeed: the second blocks, re-reads the committed set and evicts against it; five live sessions remain, never six |
| A sign-in whose lock wait exceeds `lock_timeout` (3 s, **product default**) | the lock holder | `429` `rate_limit_exceeded` with `Retry-After`, deliberately not a status only valid credentials can reach |
| Sign-in eviction vs. the evicted session's own sign-out, and `revoke-one` vs. `revoke-others` on one session | whichever conditional `ExecuteUpdate` on the liveness predicate matches rows; one transition and one `session.revoked` row | the other matches zero rows and writes no audit, a sign-out answering `401` `invalid_session` while still deleting the cookie; the losing revoke is a no-op and still answers `204`, the requested end state holding |
| Revocation vs. the target's in-flight `Touch`, vs. `PUT /api/identity/context/tenant`, a password-change rotation vs. cap eviction, and a password change or reset vs. a parallel sign-in | revocation on commit order; tenant selection uses the `Version` token and `SessionWriteRetry.Attempts` = 3; rotation runs in one lock and one transaction; a credential change wins when it commits first, through the same lock plus an in-lock security-version re-read | a later `Touch` matches zero rows and fails closed with `401`; a selection that never settles is `409` `session_concurrency_conflict`; no eviction runs after a rotation; the sign-in re-reads the changed version and issues nothing. **Conditional on C4**: validation runs before the lock |

- **Permissions.** `identity.sessions.manage` (EXISTS, application-scoped self-service, `RequiresTenant=false`, in
  `ApplicationScopedCodes` and `SelfServiceCodes`, correctly absent from `Permissions.Catalog` and required to stay
  absent) covers all three requests. No new code; sign-in stays `IPublicRequest`.
- **Audit and outbox (proposed).** All rows use `AuditEvent.CreateSessionEvent`, whose `OccurredAt` comes from
  `DateTimeOffset.UtcNow`, not `TimeProvider`. `session.revoked` gains `evicted`, `revoked_by_owner`,
  `password_changed`, `rotated`, `password_reset`; `superseded` is retired, existing rows unrewritten; new
  `session.revoke_requested` carries `requested` (one row naming the acting session per request that revoked at least
  one) and `not_found` (a denial, the HTTP answer staying byte-identical `404`). **Outbox: none.**
- **Amends.** IA-REQ-021 gains two sentences: authentication does not revoke the identity's other live sessions, and a
  new session inherits nothing from a predecessor — not its identifier, C4's recent-identity proof, Platform
  second-factor evidence or its antiforgery pair. IA-REQ-023's four values stand unchanged, pointing at IA-REQ-049.
- **Reconciled with C4.** C4 also requires a recent proof (IA-REQ-051) for `sessions.revoke-others` and for revoking
  a non-current session, and names that route `{handle}`. Decided: the proof requirement is kept and the route stays
  `{sessionRef}`. C4's `{handle}` spelling is withdrawn; there is one route and one requirement, not two readings.
- **Consumed by** the session half of Tasks 21, 22 and 23.

### 14.3 C3 — Own profile and protected AR/DNI documentary identity

> **ACCEPTED 2026-09-06, for synthetic data only.** IA-REQ-050 and IA-REQ-058 are now normative in
> [section 4](#4-normative-requirements); what follows is the decision record that produced them, kept for its route,
> contention and permission tables, which section 4 points at. Where this section and section 4 differ in wording,
> section 4 governs. The acceptance covers implementation and verification with fabricated documents: it does not
> enable real personal data, and it does not accept the residual named in the contention table below, which stays
> with the G2 gate.
>
> **Amended 2026-09-06 (A1) before that acceptance.** Correction and dispute are now defined and the
> "ship no correction route" recommendation is withdrawn: IA-REQ-058 below carries a two-party verified process, and
> the duplicate-document answer is settled in the contention table rather than left as a gap. Neither free editing
> nor a support bypass survives — the owner may open a dispute but never write a document, and an operator may
> resolve one only against a stored dispute and an external evidence reference, never for their own identity.

- **IA-REQ-050 (accepted 2026-09-06 for synthetic data; section 4 governs):** an identity that owns a `Personal` tenant has exactly one `PersonProfile`, keyed by the
  identity and readable and editable only by its owner. Self-service editing covers `FullName` and `DisplayName` and
  nothing else — never the email, the profile's ownership, the document country, type or number — and a request naming
  any member outside the accepted set is refused whole. The documentary identity is an authenticated-encryption
  ciphertext of the canonical `country|type|number` tuple plus one keyed, versioned fingerprint row per retained key
  version; the plaintext leaves the protector at exactly two named seams and reaches no log, audit record, outbox
  payload, Problem Details body, OpenAPI example or response. The owner sees only a masked status, no route outside
  the owner's own resolves a `PersonProfile` or an `IdentityDocument`, and a recorded document is never edited in
  place — not by its owner, not by an operator acting alone — but only corrected through IA-REQ-058, which exists
  before real personal data is enabled rather than after it.
- **IA-REQ-058 (accepted 2026-09-06 for synthetic data; section 4 governs):** a recorded documentary identity is corrected only by a verified two-party process, and
  the two parties are the owner and a Platform operator holding an external case reference. The owner opens a dispute
  over their own document at `POST /api/identity/profile/document/disputes` — authenticated, confirmed,
  `identity.document.dispute`, `RequiresTenant=false`, antiforgery, plus a live C4 proof for action
  `identity.document.dispute` — with `OpenDocumentDisputeRequest { claimedCountry, claimedType, claimedNumber,
  reasonCode }`, whose claimed tuple is protected on arrival exactly like the recorded one and is never echoed,
  logged, audited, returned or written into an outbox payload; the answer is `201` carrying an opaque `disputeId`,
  `409` `document_dispute_conflict` when one is already open, `401` `recent_proof_required`, and `404`
  `personal_profile_not_found` when this identity records no document. A Platform operator resolves it at
  `POST /api/platform/identities/{identityId}/document-disputes/{disputeId}/resolve` —
  `platform.identities.documents.resolve`, an active `Platform` tenant, recent MFA step-up and antiforgery — with
  `ResolveDocumentDisputeRequest { outcome, evidenceReference }`, `outcome` from the closed set `corrected` and
  `rejected` and `evidenceReference` matching `^[A-Za-z0-9._:-]{1,64}$` (**product default** on the 64), carrying no
  free text and no personal data and naming a case held outside this system. No route writes a document without a
  stored dispute, and an operator may not resolve a dispute whose subject is their own identity (`403`
  `self_resolution_refused`): those two absences are what "no support bypass" means here. A `corrected` resolution
  replaces the ciphertext and every retained fingerprint row in one transaction under the same uniqueness that refuses
  a duplicate at creation — answering the operator `409` `document_already_recorded` when the claimed tuple belongs to
  another identity, a fact an audited, MFA-proved operator may be told and no self-service caller ever is — and writes
  an append-only `IdentityDocumentCorrectionRecord { RecordId, SubjectIdentityId, DisputeId, ResolvedAt,
  ResolvedByMembershipId, EvidenceReference, PreviousKeyVersions }` holding no document value. At most one dispute per
  identity is open at a time, and the owner's own `PersonalProfileResponse` reports `document.correctionAvailable`
  `false` while one is open and `true` otherwise — their own data, saying nothing about anyone else. A dispute changes
  nothing about access: the account keeps signing in, the `Personal` tenant keeps working, and the recorded document
  keeps its value until a resolution commits.

| Caller | Personal state | `GET /api/identity/profile` | `PUT /api/identity/profile` | Can read the document number |
|---|---|---|---|---|
| Anonymous | any | `401` `invalid_session` | `401` `invalid_session` | no |
| Authenticated, no `Personal` tenant | absent | `404` `personal_profile_not_found` | `404` `personal_profile_not_found` | no |
| Authenticated owner, with no active tenant selected or with that `Personal`, an `Organization` or `Platform` active | `Personal` exists | `200` `PersonalProfileResponse`, masked document — one identical body in all four cases | `200` updated `PersonalProfileResponse` | no — masked only |
| Another identity; an Organization operator holding `members.read`/`members.manage` in the owner's Organization; or a Platform operator holding `platform.identities.read` with a proved second factor | any | its own `200`/`404`, never the target's | its own, never the target's | no — no route exists |

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/identity/profile` | Authenticated + `identity.profile.read` (application-scoped, `RequiresTenant=false`); no subject parameter | `200` `PersonalProfileResponse` carrying exactly `fullName`, `displayName`, `email`, `personalTenantId`, `document { country, type, status, maskedNumber, correctionAvailable }` or `null`, `version` (opaque) and `updatedAt`, where `maskedNumber` is `(length − 2)` `U+2022` bullets plus the final two digits and `documentNumber`, `cuit`, `fingerprint`, `keyVersion`, `digitCount`, `identityId` and `ownerId` never appear; `401` `invalid_session`; `404` Problem Details `personal_profile_not_found` |
| `PUT /api/identity/profile` | Authenticated + `identity.profile.manage` (application-scoped, `RequiresTenant=false`) + antiforgery + exact origin; `UpdatePersonalProfileRequest` accepts exactly `fullName` (1–200 characters, **product default**), `displayName` (1–60, **product default**) and `version` | `200` updated `PersonalProfileResponse`; `400` `validation_failed` with field-indexed `errors`, including a `version` this server never issued; `400` `profile_field_not_editable` naming the rejected member and never its value, mapped deliberately by Web from `UnmappedMemberHandling = Disallow`; `400` `antiforgery_validation_failed`; `401` `invalid_session`; `404` `personal_profile_not_found`; `409` `personal_profile_concurrency_conflict` |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Two profile edits on one row | the first to commit, through a conditional update against the `xmin` row version echoed as the opaque `version` | `409` `personal_profile_concurrency_conflict`; no field is written — no merge, no partial write. The additive `PersonalIdentity` migration creates `PersonProfiles` (PK `IdentityId`, `FullName` varchar 200, `DisplayName` varchar 60, `CreatedAt`, `UpdatedAt`, shadow `xmin` `Version`), `IdentityDocuments` (PK `IdentityId`, `Country` `CHECK = 'AR'`, `DocumentType` `CHECK = 'DNI'`, `Ciphertext`, `RecordedAt`, `PurgedAt`, `Version`; no `DigitCount`) and `IdentityDocumentFingerprints` (PK `(IdentityId, KeyVersion)`, `Fingerprint` varchar(64) = `"k" + KeyVersion + ":v1:" + Base64(HMACSHA256(fingerprintKey[KeyVersion], canonical tuple))` under a format `CHECK`), one fingerprint row per retained key version, backfilled before that version becomes the insert version. |
| Two identities recording the same normalized document at Personal creation | the first to commit, through `UX_IdentityDocumentFingerprints_Fingerprint` | `409` `personal_registration_conflict` — the one code shared by the two refusals that would otherwise disclose somebody else's data: this identity already owns a `Personal` tenant, or the document is already recorded. No field, header or status distinguishes those two, and IA-REQ-058's dispute route is offered from the profile screen unconditionally rather than from this response, so the answer never says which of them occurred. A refused budget is a different question and answers differently — `429` `rate_limit_exceeded`, or `503` `service_unavailable` when the store is unreachable (IA-REQ-057) — because that answer counts only the caller's own attempts and therefore says nothing about any document or any other identity. Claims are bounded by C7's `ISharedAttemptBudget` under scope `personal.document.claim`, 3 per identity per 24 hours (**product default**), and every refusal is audited. **Residual, named rather than hidden:** a person spending one of those three attempts still learns that the number they typed is recorded somewhere. It is bounded, costed — each identity costs a confirmed address — and audited, but it is not closed; accepting it belongs to the G2 real-personal-data gate, not to this decision. |

- **Permissions.** `identity.profile.read`, `identity.profile.manage` (PROPOSED, application-scoped,
  `RequiresTenant=false`, in `ApplicationScopedCodes` and `SelfServiceCodes`, never in `Permissions.Catalog`);
  `identity.context.read`, `platform.identities.read` (tenant-scoped `Platform`) and `members.read` (tenant-scoped
  `Organization`) EXIST, unchanged and not widened. IA-REQ-058 adds `identity.document.dispute` (PROPOSED,
  application-scoped self-service, in `ApplicationScopedCodes` and `SelfServiceCodes`) and
  `platform.identities.documents.resolve` (PROPOSED, tenant-scoped `Platform`, in `Permissions.Catalog`, implied by
  neither `platform.identities.read` nor C6's `platform.identities.manage`). A permission that would let anybody write
  a document value directly is still declined, and `PUT /api/identity/profile` still refuses every document member
  with `profile_field_not_editable`.
- **Audit and outbox (proposed).** One event, `identity.profile.updated`, written only when a field actually changed,
  with `code = identity.profile.updated`, `outcome` from the closed set `full-name`, `display-name`,
  `full-name.display-name`, the owner as actor, the current `SessionId`, `TenantId` null, inside the update
  transaction; a refused or no-op edit writes none. IA-REQ-058 adds `identity.document.dispute.opened` (`opened`),
  `identity.document.dispute.resolved` (`corrected`, `rejected`), `identity.document.dispute.refused`
  (`already_open`, `self_resolution`, `already_recorded`, `proof_stale`) and `personal.document.claim.refused`
  (`conflict`), each carrying identifiers only — never a document value, a claimed value or a fingerprint — with the
  resolution events also carrying the `Platform` tenant id and the resolving membership. **Outbox:**
  `identity.document.dispute.resolved.notice.requested`, tokenless, telling the owner their dispute was decided.
- **Amends.** Extends IA-REQ-026, whose list omits profile changes, and changes the meaning of section 7,
  where `GET /api/identity/context` now prefers `PersonProfile.DisplayName` over the email it returns today;
  it adds no exception to IA-REQ-044 and must not increment `Tenant.AuthorizationVersion`. IA-REQ-058 adds the
  stable codes `document_dispute_conflict`, `self_resolution_refused`, `document_already_recorded` and
  `personal_registration_conflict`, the last of which is deliberately shared by the two refusals that would otherwise
  disclose another identity's data, and by those two only; none of them widens IA-REQ-030's `401`/`403`/`404`
  meanings.
- **Reconciled with C7.** C7's fragment put documentary uniqueness in a partial index on `IdentityDocuments` and
  called the ciphertext `ProtectedNumber`. Decided: C3's child-table `UNIQUE` and `Ciphertext` stand, because one
  partial index cannot hold two retained key versions; C7's spelling and index are withdrawn, and 14.7 says so.
- **Consumed by** Tasks 19 and 20, and the data contracts in Tasks 26 and 27. Task 19 needs C7 accepted alongside
  this entry, because the classification stamp on every profile and document row and the budget that bounds a claim
  are both persistence and neither can be retrofitted. IA-REQ-058 is consumed by Task 26 **as a whole** — both halves
  — because the owner's dispute requires a live C4 recent proof, which does not exist before Task 22; Tasks 19 and 20
  record a document and never offer a way to correct one, and `correctionAvailable` reads `false` until Task 26.

### 14.4 C4 — Recent identity proof, password recovery, and provider linking with a two-part callback carve-out

> **ACCEPTED 2026-09-06, for synthetic data only.** IA-REQ-051 and IA-REQ-052 are now normative in
> [section 4](#4-normative-requirements); what follows is the decision record that produced them, kept for its
> caller-state, route and contention tables. Section 4 governs where the wording differs. Two things this acceptance
> does not carry: live provider registration, which is per-environment, and the reactivation half of the `Recovery`
> purpose, which belongs to 14.6 and to Task 26 — accepting C4 does not accept C6.
>
> **Amended 2026-09-06 (A2, A3) before that acceptance.** Automatic linking and explicit linking are now
> separate rows: a `Login` is still refused when an unlinked subject's address already belongs to a local identity,
> and a `Link` by a confirmed, authenticated identity whose own address is the match is now explicitly allowed — that
> is the ordinary case, not the forbidden one. Recovery is reconciled with 14.6: two purposes, two route pairs, one
> stated answer per account state, a `Recovery` OIDC purpose that needs no session, and a reset that never lifts a
> suspension and never skips a second factor.

- **IA-REQ-051 (accepted 2026-09-06 for synthetic data; section 4 governs):** a sensitive self-service change requires a recent identity proof: a single-use
  server-side record bound to one identity, one `UserSession`, one action and the identity's security version at
  issue. A valid session cookie alone is never proof, and only the identity's current password or a fresh challenge to
  a provider already linked to it (IA-REQ-052, purpose `Proof`) issues one, both requiring a confirmed identity; the
  lifetime is five minutes **(product default)**. A proof never travels to the client: the request looks up the live
  unconsumed proof for `(identity, current session, action)` and consumes it with a conditional update.
  Proof-requiring actions: `credentials.password.change`, `external.link`, `external.unlink`,
  `sessions.revoke-others`, `sessions.revoke-one` when the handle is not the current session, `ownership.transfer`
  (C5), `identity.lifecycle` and `platform.mfa.recover` (C6); Platform step-up stays separately session-bound and
  neither proof satisfies the other.
- **IA-REQ-052 (accepted 2026-09-06 for synthetic data; section 4 governs):** an identity may hold at most one link per external provider, established only by explicit
  consent plus a recent primary proof, a confirmed identity and a provider-verified email. `Login`, `Link`, `Proof`
  and `Recovery` purposes live in server-side state and never cross; a provider identity is never auto-linked by a
  matching email address (BR-ID-005/006) — and an authenticated, confirmed identity that links its own provider
  account with consent and a live proof is not auto-linking, so it is never refused merely because the provider's
  verified address is the one it already owns; no unlink may leave an identity without a usable authenticator.
  `Recovery` is the one purpose bound to no session, because it exists for a person who cannot obtain one: it is
  started publicly against a pending 14.6 reactivation ticket — the only ticket that asks for a current
  authenticator rather than mailbox control alone — and completing it proves only that the caller controls a
  provider subject already linked to the identity that ticket names. It issues no session and no cookie, grants no `RecentIdentityProof`, satisfies no other action, and is
  consumed by the one ticket it was started for. The provider callback is
  the one documented exception to IA-REQ-022 and section 8 and to the rule that a usable token never travels in a URL
  query string; it performs no business mutation, and the validations replacing the origin check are one-use
  purpose-bound `state` (10 minutes, **product default**), the framework correlation cookie (`Secure`, `HttpOnly`,
  `SameSite=Lax`, host-only, deleted on use), `nonce`, PKCE `S256` with a server-held verifier, and validated issuer,
  audience, signature and expiry.

| Purpose | Caller and provider state at `POST /api/identity/external/complete` | Effect | Response |
|---|---|---|---|
| any | handoff record missing, expired, consumed or purpose-mismatched; `email_verified = false`; a `Login` whose linked identity is unconfirmed (IA-REQ-020) or disabled; a `Proof` whose subject matches no link on this identity; or a `Recovery` whose ticket is spent, expired or terminal, or whose named identity holds no link for this provider | none | `400` `invalid_external_login` |
| `Login` | verified, subject already linked, identity active and email-confirmed | issue session through `SessionIssuer` (C2 cap applies); rotate the antiforgery pair; audit `session.created`, `identity.external.login.succeeded` | `204` + session cookie |
| `Login` | verified, subject not linked, no local identity for that email | create the identity with `EmailConfirmed = true` from the provider assertion, no password, no tenant, no membership; create the link; issue the session; audit `identity.created`, `identity.external.linked`, `session.created` | `204` + session cookie |
| `Login` | verified, an unlinked subject whose email already belongs to a local identity | none — no link, no session, no identity: BR-ID-005/006 forbid linking a provider identity **automatically** on a matching address, and an unattended `Login` is exactly that path | `409` `external_login_conflict` |
| `Login` or `Link` | verified, a subject already owned by another identity | none | `409` `external_login_conflict` |
| `Link` | verified, subject unowned, but the provider's verified address belongs to a **different** local identity than the caller's | none — linking would give this identity a sign-in path carrying somebody else's address | `409` `external_login_conflict` |
| `Link` | authenticated but unconfirmed (IA-REQ-005); or confirmed + live proof for `external.link` + consent with `email_verified = false`; or confirmed + proof + consent, verified, subject unowned and no link for this provider — **including, and most commonly, when the provider's verified address is this identity's own**, which is explicit linking by the person who owns both sides and is not what BR-ID-005/006 forbid | nothing in the first two; in the third, create the link, consume the proof, increment the security version, revoke the identity's other sessions, audit `identity.external.linked`, notify | `403` `email_confirmation_required`; `400` `invalid_external_login`; `204` |
| `Link` | same, subject already linked to this identity | none, idempotent | `204` |
| `Link` | same, subject unowned, identity already has a different link for this provider | none | `409` `provider_already_linked` |
| `Link` | proof expired, consumed or bound to another session or action; or anonymous, the starting session being gone | none | `401` `recent_proof_required`; `401` `authentication_required` respectively |
| `Proof` | authenticated + confirmed, provider already linked to this identity, subject matches that link | issue a `RecentIdentityProof` with `Method = ExternalProvider` for the action named in the handoff record | `204` |
| `Recovery` | anonymous or authenticated; a live handoff whose ticket is still pending, whose identity holds a link for this provider, and whose subject matches that link | mark that one ticket's provider proof satisfied — no session, no cookie, no `RecentIdentityProof`, no lifecycle change | `204` |

| Method and route | Access | Primary result |
|---|---|---|
| `POST /api/identity/credentials/reauthenticate`; `PUT /api/identity/credentials/password` | both authenticated + antiforgery, `RequiresTenant=false`, `identity.credentials.manage`; `ReauthenticateCommand { action, password }` and `ChangePasswordCommand { newPassword }` — no `currentPassword` field exists anywhere — both `ISensitiveRequest`, the change additionally requiring a live proof | bodyless `204`; `400` `invalid_credential_proof` for a wrong password or unknown action; `403` `email_confirmation_required`; `429` `rate_limit_exceeded` + `Retry-After` for an exhausted budget and `503` `service_unavailable` + `Retry-After` when the shared budget store is unreachable (IA-REQ-057). The change answers bodyless `204` plus a rotated session cookie and antiforgery pair, `401` `recent_proof_required`, `409` `session_concurrency_conflict` |
| `GET /api/identity/credentials`; `GET /api/identity/external` | Authenticated + `identity.credentials.manage` / `identity.external.manage` | `200` `{ hasPassword, passwordUpdatedAt }`; `200` `{ items: [{ handle, provider, providerEmail, linkedAt }] }` with no envelope, `handle` opaque and no provider token, claim set or profile payload |
| `POST /api/identity/credentials/password/recovery`; `POST /api/identity/credentials/password/reset` | both public + antiforgery, `IPublicRequest`; recovery rate-limited with `RequestPasswordRecoveryCommand { email }`, reset carrying the fragment token with `ResetPasswordCommand { token, newPassword }`, `ISensitiveRequest` | recovery: neutral bodyless `202` for every valid request and every account state, `400` `antiforgery_validation_failed`, `429` `rate_limit_exceeded` + `Retry-After` on the caller budget only and never on the per-address budget, and `503` `service_unavailable` + `Retry-After` when the shared budget store is unreachable (IA-REQ-057). What is enqueued behind that one answer is fixed by account state and is stated once, here and in 14.6: `PendingConfirmation`, `Active`, `SelfDeactivated` and `AdministrativelySuspended` each enqueue a reset token; `Closed` enqueues nothing. Reset: bodyless `204`, `400` `invalid_credential_token` for unknown, expired, consumed, superseded or wrong-purpose, `400` `validation_failed` with field-indexed errors for `PasswordOptions`. A completed reset changes the password, increments the security version, revokes every session and issues none — and it changes no lifecycle state, so a suspended account is still suspended and a self-deactivated one is still deactivated; it never sets `LastVerifiedAt`, never marks a session as having proved a second factor and never removes a `PlatformMfaEnrollment`, so the next sign-in meets exactly the gates it met before |
| `POST /api/identity/external/{provider}/recovery/start` | Public + antiforgery, `IPublicRequest`, rate-limited; `StartExternalRecoveryCommand { ticket }`, the ticket being the fragment-delivered 14.6 reactivation token, hash-compared and never echoed | `200` `{ authorizationRequestUri }`, `Cache-Control: no-store`, for a live ticket whose identity holds a link for that provider; `400` `invalid_credential_token` for every other case, worded identically so the route reveals nothing about the ticket, the identity or its links; `429` `rate_limit_exceeded` + `Retry-After`; `503` `service_unavailable` + `Retry-After` when the shared budget store is unreachable |
| `POST /api/identity/external/{provider}/login/start`, `/link/start`, `/proof/start` | login public + antiforgery (`IPublicRequest`); link authenticated + antiforgery + proof + `StartExternalLinkCommand { consent: true }` + `identity.external.manage`; proof authenticated + antiforgery + `StartExternalProofCommand { action }` + `identity.credentials.manage`; all `RequiresTenant=false` | `200` `{ authorizationRequestUri }`, `Cache-Control: no-store`; `401` `recent_proof_required` on link; `404` `not_found` on proof when this identity has no link for that provider |
| `/api/identity/external/google/callback` — one literal path per provider, because a scheme has exactly one and it is the URI a deployment registers | The carve-out: no `Origin`, no antiforgery, no Application request sent | `302` to `/external/return` carrying one closed-set outcome slug, plus the sealed handoff cookie (10 minutes, **product default**, matching the handoff record's own lifetime) |
| `POST /api/identity/external/complete` | Same-origin + antiforgery + handoff cookie; no body fields; four requests selected by the record's purpose — `CompleteExternalLoginCommand` and `CompleteExternalRecoveryCommand` (both `IPublicRequest`), `CompleteExternalLinkCommand` and `CompleteExternalProofCommand` (both `[Authorize(..., requiresTenant: false)]`) | per the caller/state table above |
| `DELETE /api/identity/external/{provider}` | Authenticated + antiforgery + proof, `RequiresTenant=false`, `identity.external.manage` | bodyless `204`; `409` `last_authenticator_required`; `401` `recent_proof_required`; `404` `not_found` |

**Built 2026-09-06, and where it differs from the paragraphs above.** Three corrections, each because the written
form could not be built as written:

- *The handoff cookie is this application's own, not the framework's external-scheme one.* No external sign-in
  cookie is written at all: the callback is handled inside `OnTicketReceived`, so nothing is ever signed into the
  external scheme and there is no framework handoff to carry. What carries the round trip instead is
  `__Host-ia-external`, a Data Protection **time-limited** payload holding the handoff identifier and the purpose
  the callback settled, `Secure`, `HttpOnly`, `SameSite=None` (the return leg is a cross-site form post, and
  anything stricter drops the cookie on the one request it exists for), sealed at the start and re-sealed by the
  callback, and deleted by the completion. The purpose living in that cookie is what makes the single `/complete`
  route safe: the caller names no effect, so a `Login` cannot be completed as a `Link`.
- *The outcome slug set is `signed_in`, `linked`, `proved` and `refused`.* `recovered` belongs to C6 and Task 26.
  `onboarding_required` and `link_required` are not produced by the redirect because the states they described
  are answered by `/complete` itself — `409` `external_login_conflict` — where a client is already listening.
  Every protocol failure and a person who declines arrive as the same `refused`.
- *Two concurrent unlinks are serialized by the per-identity advisory lock, not by taking the
  `IdentitySecurityState` row.* An identity at security version zero has no such row to take, so the row lock has
  a hole exactly where a provider-only identity — the one the last-authenticator rule protects — sits. The lock
  used is `ISessionLock`, the same two-argument advisory space every session write already takes, so an unlink is
  serialized against session issuance as well as against another unlink.

**`GET /api/identity/credentials` is built (2026-09-06), and it is the account screens' honesty.** `hasPassword`
comes from ASP.NET Identity; `passwordUpdatedAt` is a nullable column on the Domain-owned `IdentitySecurityState`,
stamped only by the reset and the authenticated change. It is deliberately not `UpdatedAt`, which every
authenticator change advances — a page that reported linking a provider as a password change would be lying to
the one person who would notice. An identity that has never changed its password answers `null`, because a
creation date is not a change date. The route takes no subject parameter, so it can never be asked about somebody
else.

**Corrections from the adversarial review, 2026-09-06.** Four defects the tests did not catch, each now pinned by
a test verified against a reverted fix:

- *One provider account is claimed under its own lock, not under the identity's.* The identity lock serializes
  everything one person does and cannot serialize two different identities racing for one provider account: both
  read the subject as unowned, and the unique key then refuses the loser by raising inside a transaction it
  aborts — too late to record the refusal, so the loser met `500` instead of `external_login_conflict`.
  `IExternalSubjectLock` is a third advisory space keyed on (provider, subject), taken by the `Login` and `Link`
  completions, always after the identity lock so the order is the same everywhere.
- *A `Proof` challenge asks the provider for `prompt=login`.* Without it the provider answers from whatever
  session the browser already holds there, so the round trip proved possession of an unlocked device rather than
  presence of a person — which is not what IA-REQ-051 means by a recent identity proof. The purpose is sealed
  into the handoff cookie from the start, not only by the callback, so the challenge reads it from there and
  never from the route.
- *Linking is audited as `authenticator_linked`.* The sessions a link revokes were being recorded with the
  reason `password_changed`, telling an investigation that a password changed when none did.
- *A callback failure clears the handoff cookie only when it names a handoff.* Clearing unconditionally let any
  stranger posting to the callback path delete the cookie of a round trip somebody else had started.

**Not built here.** The `Recovery` purpose and its two routes are C6's and Task 26's.

| Contended write | Winner | Loser's answer |
|---|---|---|
| Proof consumption | first commit of `UPDATE "RecentIdentityProof" … WHERE "ConsumedAt" IS NULL AND "ExpiresAt" > now AND "SecurityVersion" = @v` with a live-session check, under the partial unique index on `(IdentityId, SessionId, Action) WHERE ConsumedAt IS NULL` | `401` `recent_proof_required` — terminal, not `409`, because retrying the same proof cannot succeed. `RecentIdentityProof` holds `Id`, `IdentityId`, `SessionId`, `Action`, `Method` (`Password` or `ExternalProvider`), `SecurityVersion`, `IssuedAt`, `ExpiresAt` = `IssuedAt + 5 min`, `ConsumedAt`, `ConsumedReason`, `Version`; `IdentitySecurityState` holds `IdentityId` PK, `SecurityVersion`, `UpdatedAt` and a concurrency token, is Domain-owned rather than a column on `ApplicationUser`, and is not backfilled, an absent row meaning `SecurityVersion = 0`. |
| Reset token consumption | first commit of the same conditional update on the reset row, whose token is stored only as `VersionedTokenHash` | `400` `invalid_credential_token`, indistinguishable from expired, superseded or unknown. The token is fragment-delivered, single-use and lives 30 minutes (**product default**). |
| Reset issuance vs. reissue | the later commit, under the partial unique index `("IdentityId") WHERE "Status" = 'Pending'`, inserting only after the prior record and its `OutboxSecret` are superseded (`Terminate(Failed, "superseded", now)`) in the same transaction | one bounded retry, then the same neutral `202`; never two live tokens |
| Reset vs. authenticated change, and reset vs. a concurrent sign-in | the first to commit; all take the `IdentitySecurityState` row for update | the loser's proof or token no longer matches `SecurityVersion` (`recent_proof_required` / `invalid_credential_token`); a sign-in that won is revoked by the change that follows |
| Two identities linking the same subject, and two links for one identity on one provider | first commit, under the existing `AspNetUserLogins ("LoginProvider","ProviderKey")` primary key and the new additive unique index `UX_AspNetUserLogins_LoginProvider_UserId` respectively | `409` `external_login_conflict`; `409` `provider_already_linked` |
| Two concurrent unlinks | first commit; the authenticator count is read after taking the per-identity advisory lock inside the transaction (see the corrections above) | `409` `last_authenticator_required` |
| Handoff record consumption | first commit of the conditional single-use update on the `ExternalAuthorizationRequest` record | `400` `invalid_external_login` |

- **Permissions.** `identity.credentials.manage`, `identity.external.manage` (PROPOSED, application-scoped
  self-service, in `ApplicationScopedCodes` and `SelfServiceCodes`, never in `Permissions.Catalog`);
  `identity.sessions.manage` (EXISTS) unchanged. `IPublicRequest` carries `RequestPasswordRecovery`, `ResetPassword`,
  `StartExternalLogin` and `CompleteExternalLogin`; the callback carries neither a permission nor the marker.
- **Audit and outbox (proposed).** Audit `identity.proof.issued`, `.consumed`, `.refused`,
  `identity.password.recovery.requested`, `identity.password.reset`, `identity.password.changed`,
  `identity.external.login.started`, `.callback.refused`, `.login.succeeded`, `.login.refused`,
  `identity.external.linked`, `.link.refused`, `identity.external.unlinked`, `identity.external.recovery.started`
  and `.recovery.completed`; refusals collapse into allowlisted
  `email_unverified`, `subject_owned_elsewhere`, `local_email_exists`, `provider_already_linked`, `purpose_mismatch`,
  `state_invalid`, `proof_stale`, `session_revoked`, `superseded`, `last_authenticator`, `email_unconfirmed`. Outbox
  `identity.password.recovery.requested` (the only one with an `OutboxSecret`), `identity.password.changed.notified`,
  `.external.linked.notified`, `.external.unlinked.notified`.
- **Amends.** IA-REQ-022 and section 8 gain one named exception at the provider callback, as does the rule that a
  usable token never travels in a query string; IA-REQ-019 becomes explicitly local; IA-REQ-020 is amended so a
  provider `email_verified = true` establishes local `EmailConfirmed` only for an identity created by that sign-in;
  IA-REQ-030 gains `recent_proof_required`; IA-REQ-035 is amended where a spent proof or token maps to `401`/`400`;
  section 6's neutrality sentence is amended; and section 5 gains `RecentIdentityProof`, `IdentitySecurityState`,
  `PasswordResetRequest`, `ExternalLoginLink` and `ExternalAuthorizationRequest`. Amendment A3 adds the `Recovery`
  purpose, which is what gives a provider-only identity a way back without the session it cannot obtain, and pins the
  per-state recovery answer that 14.6 repeats rather than restates differently.
- **Consumed by** Tasks 21, 22 and 23, and the recovery portion of Task 26. Its `Recovery` purpose is also what
  14.6's reactivation route depends on, so C6 cannot be implemented without this half of C4.

### 14.5 C5 — Delegated Organization administration

- **IA-REQ-053 (proposed):** delegated administration of an `Organization` is exercised through custom roles,
  membership lifecycle and one explicit ownership reference. A role is `Active` or `Retired`, retirement is terminal,
  `Role.IsSystem` is an orthogonal protection flag, and there is no `Draft` state. An actor may cause an identity to
  hold only permissions the actor itself effectively holds in that tenant at commit time and that
  `PermissionDefinition.AllowedTenantTypes` permits — evaluated on the added codes when a role's set changes, on the
  whole set when a role is assigned or offered, never at acceptance — so a code nobody holds can never be granted, and
  the system `Owner` role must be provisioned with every `Organization`-allowed catalogue code, existing tenants
  backfilled and every future addition carrying that step. No request may commit a state with zero effective
  administrators, computed from the flushed post-change state inside the same transaction against the permission
  projection. An `Organization` has exactly one owner held as a single tenant reference; transfer requires the current
  owner, the ownership permission, a confirmed active same-tenant recipient and recent primary proof, in one
  transaction or none. Widening or retiring a role cancels in that transaction every pending invitation offering it
  and retires any still-undelivered `OutboxSecret`. Every mutation leaves `Tenant.AuthorizationVersion` strictly
  greater than the value it loaded.

| Caller state | `roles.read` | `roles.manage` | `members.read` | `members.manage` | `members.invite` | `tenant.ownership.transfer` |
|---|---|---|---|---|---|---|
| Anonymous | `401` | `401` | `401` | `401` | `401` | `401` |
| Authenticated, no active tenant; permission absent; membership `Suspended`/`Revoked`; or tenant not `Active` | `403` | `403` | `403` | `403` | `403` | `403` |
| Active tenant is `Personal`, then `Platform` | `403`; `400` `invalid_role_operation` | `403`; `400` `invalid_role_operation` | `403`; `403` | `403`; `403` | unchanged (IA-REQ-014) | `403`; `403` |
| Active `Organization`, permission held, identity unconfirmed; or a route `tenantId` differing from the session's active tenant | `400` | `400` | `400` | `400` | unchanged (IA-REQ-005/014) | `400` |
| Active `Organization`, permission held, actor not the owner | n/a | n/a | n/a | n/a | n/a | `403` `owner_required` |

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/tenants/{tenantId}/permission-catalog`; `GET .../roles` with `limit` (1–100) / `cursor`; `GET .../roles/{roleId}` | `roles.read` | `200` `PermissionCatalogEntryResponse[] { code, grantable }`; `200` typed `{ items: RoleResponse[], nextCursor }`; `200` `RoleResponse { roleId, name, isSystem, isRetired, permissions, version }`, another tenant's role being `404` (IA-REQ-030) |
| `POST /api/tenants/{tenantId}/roles`; `POST .../roles/{roleId}/retire` | `roles.manage` + antiforgery; `CreateRoleRequest { name, permissions }`; `RetireRoleRequest { version }` | `201` `RoleResponse` + `Location`, or bodyless `204` for a retirement, already retired with a current `version` being an idempotent `204`; ceiling refusal `400` `invalid_role_operation`; duplicate normalized name or stale `version` `409` `role_concurrency_conflict`; `409` `last_administrator_required` |
| `PUT /api/tenants/{tenantId}/roles/{roleId}` | `roles.manage` + antiforgery; `UpdateRoleRequest { name, permissions, version }` | `200` `RoleResponse`; ceiling, system-role or retired-role refusal `400` `invalid_role_operation`; stale `version` `409` `role_concurrency_conflict`; removing the last effective administrator `409` `last_administrator_required` |
| `GET /api/tenants/{tenantId}/members`; `GET .../invitations`, both with `limit` (1–100) / `cursor` | `members.read` | `200` typed `{ items: MemberResponse[], nextCursor }` with `MemberResponse { membershipId, identityId, displayName, normalizedEmail, status, roleIds, isOwner, version }`; `200` typed `{ items: InvitationSummaryResponse[], nextCursor }` with `InvitationSummaryResponse { invitationId, normalizedEmail, status, createdAt, expiresAt, roleIds }` |
| `PUT /api/tenants/{tenantId}/members/{membershipId}/roles` | `members.manage` + antiforgery; `UpdateMemberRolesRequest { roleIds, version }` | `200` `MemberResponse`; ceiling refusal `400` `invalid_membership_operation`; stale `409` `membership_concurrency_conflict`; `409` `last_administrator_required` |
| `POST /api/tenants/{tenantId}/members/{membershipId}/suspend`, `/reactivate`, `/revoke` | `members.manage` + antiforgery; `MemberStatusRequest { version }` | bodyless `204`, idempotent for the already-held state with a current `version`; a `Revoked` membership reactivated, or the owner's membership suspended or revoked before transfer, is `400` `invalid_membership_operation`; `409` `membership_concurrency_conflict`; `409` `last_administrator_required` |
| `POST /api/tenants/{tenantId}/ownership/transfer` | `tenant.ownership.transfer` + antiforgery + recent proof (C4); `TransferOwnershipRequest { toMembershipId, version }` plus C4's proof field | bodyless `204`, idempotent when the recipient is already the owner and `version` matches; `403` `owner_required`; `400` `invalid_membership_operation`; `409` `membership_concurrency_conflict`; `409` `last_administrator_required` |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Two administrators editing one role's permissions, or a rename racing a permission edit | the first to commit; the arbiter is the forced conditional update of `Roles.xmin` when `version` is enforced, otherwise `Tenants.xmin`, which every such mutation writes and which is reached first | `409` `role_concurrency_conflict`; role edits are whole-role replacements, never a per-permission merge |
| Role edit racing an invitation acceptance | the first to commit, on `Tenants.xmin`, not `Invitations.xmin` | acceptance first: the widening answers `409` `role_concurrency_conflict` and its retry cancels nothing. Widening first: acceptance answers `409` `invitation_conflict`, and its retry reads `Cancelled` and answers `400` `invalid_invitation` |
| Two last-administrator-affecting changes, an ownership transfer racing a role edit, or two transfers | the first to commit, on `Tenants.xmin`, where `OwnerMembershipId` also lives. The floor is `{ roles.manage, members.manage }` in one identity, counted over DISTINCT identities by the proposed `IEffectiveAdministratorReader.CountAsync(TenantId, …)` after an explicit `SaveChangesAsync` flush inside the mutating transaction, lockout excluded. `Tenants.OwnerMembershipId` is an additive nullable column with composite FK `(TenantId, OwnerMembershipId) → TenantMemberships(TenantId, Id)`, `ON DELETE RESTRICT`; "an `Organization` always has an owner" is an Application transaction invariant plus a migration backfill, not a `CHECK`. Proposed transitions `TenantMembership.Reactivate`, `Revoke` and `Reinstate` — `Revoked → Active` only through a fresh invitation, after deleting every prior `MembershipRole` row | `409` `membership_concurrency_conflict`; a retry may then answer `409` `last_administrator_required` |
| Two acceptances of one offer | unchanged: `Invitations.xmin` and the `TenantMemberships` unique index | unchanged (IA-REQ-016/017) |

- **Permissions.** `roles.read`, `roles.manage`, `members.read`, `members.manage`, `members.invite` and
  `tenant.manage` all EXIST, tenant-scoped and unchanged — narrowing the first two would break
  `PermissionCatalogSynchronizer.ValidateCatalog` at startup, and `tenant.manage` is named only to say it is not the
  ownership permission; `tenant.ownership.transfer` is PROPOSED, tenant-scoped `Organization`, necessary but never
  sufficient. Every stable code here but `permission_denied`, `invalid_invitation`, `invitation_conflict` and
  `recent_mfa_required` is PROPOSED: the two `invalid_*_operation` codes are `Validation` (`400`), the two
  `*_concurrency_conflict` codes and `last_administrator_required` are `Conflict` (`409`), `owner_required` is
  `Authorization` (`403`).
- **Audit and outbox (proposed).** No new audit type: the interceptor keeps writing `role.changed`
  (`granted`/`revoked`/`changed`/`retired`) and `membership.changed` (`granted`/`revoked`/`changed`) per changed row
  with a null actor, while handlers write actor-attributed `membership.changed` with `suspended`, `reactivated`,
  `revoked`, `reinstated` or `ownership-transferred`, and `invitation.cancelled` with `role-widened` or
  `role-retired`, one per cancelled offer. Outbox adds `identity.ownership.transferred.notice.requested` — tenant and
  the two membership ids, no token, no `OutboxSecret`.
- **Amends.** Replaces IA-REQ-047's closing deferred-control clause: a widened role no longer reaches acceptance, the
  widening cancelling every pending offer referencing it while a delivered token is refused on status as `400`
  `invalid_invitation`. The bounded `limit`/`cursor` shape on `/api/tenants/*` amends IA-REQ-038 and IA-REQ-045.
- **Reconciled with C6.** C6 named the same refusal `tenant_last_administrator` and added an `expectedStatus`
  precondition plus a recent C4 proof on the membership routes. Decided: C5 owns the definition, so
  `last_administrator_required` and the echoed `version` stand and C6's second spelling is withdrawn; C6's
  `expectedStatus` precondition and proof requirement are kept, because they narrow rather than contradict.
- **Consumed by** Tasks 24 and 25.

### 14.6 C6 — Finite identity and membership lifecycle, and a fail-closed restore admission guard

> **Amended 2026-09-06 (A3, A4); still proposed, still undecided.** The provider-only return path is now defined
> and lives in C4 as the `Recovery` purpose, which binds to a ticket rather than to a session. Recovering a
> credential no longer touches lifecycle: a reset leaves a suspension standing and leaves every second factor in
> place. And a legal hold has left the state table entirely — it stops erasure, it does not stop access.

- **IA-REQ-054 (proposed):** identity account state and tenant membership state are finite, explicit, and the only
  source of the "active identity" condition IA-REQ-020 already states. Every transition is a conditional mutation,
  is audited, and either commits with its session, token and outbox effects or commits none of them. Every
  non-terminal disabled state has a named actor, a named proof and a named endpoint; a terminal state has none and
  says so. Retention is not lifecycle: a legal hold stops erasure and nothing else, is never an account state, never
  refuses a sign-in and never blocks a reactivation (amendment A4). Recovering a credential is not lifecycle either:
  a completed password reset or provider recovery changes what the person can prove, never what their account is
  allowed to do, so it lifts no administrative suspension and skips no second factor that would otherwise apply.
- **IA-REQ-055 (proposed):** a restored deployment admits no public ingress, issues no session, accepts no restored
  session or one-time token and dispatches no outbox delivery until an operator-controlled admission record held
  outside the restored database is verified against an operator-held key supplied by environment configuration.
  Absent, unreadable, expired, wrongly signed, wrong-deployment or non-advancing evidence keeps admission closed on
  every process start, not only the first, and no value read from the restored database or contained in the backup can
  open it.

| Identity account state | Meaning | May sign in (IA-REQ-020) | Reachable from |
|---|---|---|---|
| `PendingConfirmation` (existing) | created, email not confirmed | no | registration, invitation registration |
| `Active` (existing) | confirmed and usable | yes | confirmation; reactivation, which restores the pre-disable state rather than `Active` unconditionally |
| `SelfDeactivated` (proposed) | the person parked their own account | no | `Active`, by the person |
| `AdministrativelySuspended` (proposed **rename** of the existing `Suspended`) | a Platform operator stopped the account, under closed-set `IdentitySuspensionReason` `PolicyViolation`, `SecurityIncident`, `BillingHold`, `OperatorRequest`, recorded only in audit | no | `PendingConfirmation`, `Active` or `SelfDeactivated`, by a Platform operator |
| `Closed` (proposed) | erasure executed; terminal tombstone | never | `SelfDeactivated`, `AdministrativelySuspended` |

**A legal hold is not one of these states and no longer appears in this table (A4).** It is C7's `RetentionLegalHold`
record, placed and released by an operator holding `platform.retention.manage` under an active `Platform` tenant, a
recent MFA step-up and antiforgery — the actor, permission and endpoint this entry previously left unnamed. Its only
effect is to stop erasure: a held subject is skipped by every purge, and because `Closed` is reached only by an
executed erasure, a hold keeps that transition from happening at all — there is no close route for it to refuse. It
suspends nobody, refuses no sign-in, blocks no reactivation and takes part in no authorization decision.

**The three disabled cases, named once so nothing else has to say "case (a)".** (a) `SelfDeactivated`: the person
returns through the public reactivation pair below, proving a current authenticator — their password, or a provider
through C4's `Recovery` purpose, which needs no session. (b) `AdministrativelySuspended`: only a Platform operator
lifts it; no self-service route reaches it, and recovering a credential does not touch it. (c) `Closed`: terminal,
with no way back, which is what a tombstone means.

| Method and route | Access | Primary result |
|---|---|---|
| `POST /api/identity/account/deactivate` | Authenticated + `identity.account.manage`, `RequiresTenant=false` + antiforgery + recent C4 primary proof; `DeactivateAccountRequest { proofToken }` | bodyless `204`, revoking every persisted session, consuming C4 proofs, incrementing the security version, clearing `LastVerifiedAt`/`LastVerifiedSessionId` and terminalizing token-bearing intents over this identity's own credentials, while memberships and invitations addressed to its email stay untouched; `409` `identity_concurrency_conflict`; `409` `platform_last_owner`; `409` `last_administrator_required` (defined by C5) |
| `POST /api/identity/account/reactivation-requests`; `POST /api/identity/account/reactivate` | both `IPublicRequest` + antiforgery + rate limit; `RequestAccountReactivationRequest { email }`; `ReactivateAccountRequest { reactivationToken, password?, providerProofToken? }`, exactly one of the last two, the token single-use, hash-compared, 30 minutes **(product default)**, superseded on reissue; `providerProofToken` is the single-use handle returned by completing a C4 `Recovery` handoff started from this same reactivation ticket, which is how an identity that only ever signs in through a provider proves a current authenticator without the session it is being denied | neutral bodyless `202` for every valid request and every account state, only a `SelfDeactivated` identity enqueuing anything; then bodyless `204` with no session and no cookie, restoring the pre-disable state and never lifting an administrative suspension — a suspended or `Closed` identity has no self-service way back and this route does not become one; `400` `invalid_reactivation` for a bad, spent, expired or terminal-state token and for a failed proof, worded identically; `400` `antiforgery_validation_failed`; `429` `rate_limit_exceeded` + `Retry-After`; `503` `service_unavailable` + `Retry-After` when the shared budget store is unreachable (IA-REQ-057) |
| `POST /api/tenants/{tenantId}/members/{membershipId}/suspend`, `/reactivate` | `members.manage` (`Organization` only) + antiforgery + recent C4 proof; `SuspendMembershipRequest`/`ReactivateMembershipRequest { expectedStatus }` | bodyless `204`; `409` `membership_concurrency_conflict`; `409` `last_administrator_required` (defined by C5) |
| `POST /api/platform/identities/{identityId}/suspend`, `/reactivate`; `POST /api/platform/mfa/recover` | the first pair needs `platform.identities.manage` + active Platform tenant + recent MFA step-up + antiforgery, with `SuspendIdentityRequest { reason, expectedStatus }` and `ReactivateIdentityRequest { expectedStatus, acknowledgeSelfDeactivation }`; recovery needs an authenticated confirmed identity + `platform.mfa.enroll` + antiforgery + fresh C4 proof + one unused `PlatformRecoveryCode` (`RecoverPlatformMfaRequest { recoveryCode, proofToken }`) and no active Platform tenant | bodyless `204`; `409` `identity_concurrency_conflict`; `403` `identity_reactivation_unavailable` for `Closed` alone — a legal hold no longer blocks any reactivation (A4); `404` for an unknown identity id; `401` `recent_mfa_required`. Recovery answers `200` with the enrollment DTO shown once, the factor replaced in place on the single enrollment row by a proposed `Recover(...)` transition gated on `Status == Active` and a spent code, with no `Retired` status and no index change; `409` `platform_mfa_concurrency_conflict`; `429` `rate_limit_exceeded` + `Retry-After` on the same per-identity budget as `/verify`, and `503` `service_unavailable` + `Retry-After` when that shared store is unreachable (IA-REQ-057) |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Identity status change, and the same change vs. the last-administrator check | the first conditional update on `(identityId, status = expectedStatus)` guarded by the row's existing `ConcurrencyStamp`; the check and the write are one statement in one transaction, so a second transaction fails its own precondition | `409` `identity_concurrency_conflict`; `409` `platform_last_owner` or `409` `last_administrator_required` |
| A reactivation token, or a Platform recovery code with its in-place factor replacement | the first single-use consumption; the first conditional update on the single enrollment row, the code staying spent exactly once | `400` `invalid_reactivation`, indistinguishable from a forged token; `409` `platform_mfa_concurrency_conflict` |
| Membership suspend, reactivate or revoke | the first conditional update on `(membershipId, status = expectedStatus)`; the `AuthorizationVersion` increment is a consequence of the winning write, not its precondition | `409` `membership_concurrency_conflict` |
| Maintenance batch row | the first CAS on row status, under `LifecycleMaintenanceService`'s per-category advisory lock | the row is skipped, never deleted twice. `LifecycleMaintenanceService` is internal with no public route: every 15 minutes, 500 rows per category per pass, at most 10 passes per run, a 60-second budget, single-flight per category, and 90-day retention for revoked or expired `UserSession` rows — all **product defaults** — never deleting `AuditEvent`, delivery evidence, memberships, tenants or roles, and doing nothing when retention policy is absent. |
| Restore epoch advance at release | the first conditional update on `(deployment, storedEpoch = observedEpoch)`; exactly one release commits | the second read re-evaluates and settles at the more closed state. Admission is `Closed` (the default whenever evidence is insufficient: no public ingress — `503` Problem Details `recovery_admission_closed` + `Retry-After` — no session issued or accepted, no one-time token, no delivery, only dataless liveness and readiness probes), `Quarantined` (authentication and revalidation only; sessions created before the epoch stamp count as revoked and `OutboxSecret` rows predating it are refused and terminalized) or `Open` (requiring the record's `release` claim and completed reconciliation). The record carries `deployment`, `recoveryEpoch`, `backupId`, `issuedAt`, `expiresAt`, `release`, `signature`. |

- **Permissions.** `identity.account.manage` (PROPOSED, application-scoped self-service, in both code sets) and
  `platform.identities.manage` (PROPOSED, tenant-scoped `Platform`, distinct from `platform.identities.read` and
  `platform.tenants.manage`); `platform.mfa.enroll` (EXISTS, granting nothing, so the gate is the unused recovery
  code), `members.manage` (EXISTS, tenant-scoped `Organization` only, which stops generic membership reactivation
  reaching Platform) and `platform.admins.manage` (EXISTS, `Platform`) unchanged; the public reactivation half carries
  none.
- **Audit and outbox (proposed).** Audit `identity.lifecycle.changed` (`self_deactivated`,
  `administratively_suspended`, `reactivated`, `reactivated_over_self_deactivation`, `closed`),
  `identity.reactivation.requested` (`accepted`, `ignored`, only when the address matched an identity),
  `identity.reactivation.completed`/`.denied` (`token_invalid`, `proof_invalid`, `terminal`), `membership.changed`
  (new `suspended`, `reactivated`, `revoked`), `platform.mfa.recovered` (`factor_replaced`, `code_reused`,
  `proof_stale`), `retention.executed` (`purged`, `skipped_no_policy`, `skipped_legal_hold`, never for an idle pass)
  and `recovery.admission.evaluated` (`closed`, `quarantined`, `open`, `evidence_missing`, `evidence_stale`,
  `evidence_invalid`), identity-scoped ones needing a null-tenant factory. Outbox `identity.reactivation.requested`
  (token-bearing, sealed), `identity.lifecycle.notice.requested`, `platform.mfa.recovered.notice.requested`.
- **Amends.** IA-REQ-054 makes IA-REQ-020's "active identity" precise against a finite set whose added states are all
  non-`Active`, renames `IdentityAccountStatus.Suspended` (safe only while that enum has no persisted column and no
  reader), extends IA-REQ-042's last-owner rule to self-deactivation and administrative suspension under a new
  `platform_last_owner` code, and proposes that revocation write `MembershipStatus.Revoked` instead of `Suspend`,
  existing rows reclassified only by an operator decision. Amendment A4 removes the proposed `LegalHoldAt` marker
  from this entry altogether: retention holds are C7's record and C7's endpoints, and no lifecycle branch reads them.
- **Reconciled with C4 (A3).** Case (a)'s provider alternative needed an OIDC purpose bound to something other than
  a session, which C4 as first drafted did not contain. Decided: C4 now defines `Recovery`, started publicly against
  a pending ticket and consumed by it, issuing no session and granting no proof for anything else. A provider-only
  identity therefore has a self-service way back, and C6 cannot be implemented before that half of C4.
- **Consumed by** Tasks 26 and 27, and the production gate in Task 28.

### 14.7 C7 — Personal-data mode, retention and erasure evidence, and shared abuse-control state

> **ACCEPTED 2026-09-06, for synthetic data only.** IA-REQ-056 and IA-REQ-057 are now normative in
> [section 4](#4-normative-requirements); what follows is the decision record that produced them, kept for its route,
> contention and permission tables. Section 4 governs where the wording differs. The acceptance authorizes
> `Synthetic` mode alone: switching a deployment to `Real` is the G2 gate and is not granted here, and neither is
> production.
>
> **Amended 2026-09-06 (A4, A5) before that acceptance.** A legal hold now stops erasure and nothing
> else: it is defined here, it is placed and released here, and no lifecycle or authorization branch anywhere reads
> it. An unavailable abuse-control store still refuses every attempt, and now says so honestly with `503`
> `service_unavailable` and `Retry-After` instead of telling a person they tried too many times.

- **IA-REQ-056 (accepted 2026-09-06 for synthetic data; section 4 governs; numbered 048 in the C7 fragment):** the deployment holds exactly one personal-data mode, and
  the mode — never a caller, never a request field — determines what the system may do with the personal data it
  stores. `IdentityAccess:PersonalData:Mode` is `Synthetic` or `Real`; absent, blank or unparseable is `Synthetic`, so
  real handling is never reached by omission or by a typo. Every `PersonProfile` and document row is stamped at
  creation with the mode as a persisted server-derived `DataClassification`, a column on no request DTO and never on
  `ApplicationUser`. A retention policy is configuration, never code, and contains no period, threshold or
  jurisdictional number; with none configured the system performs no destructive action in either mode. A purge erases
  ciphertext and keyed fingerprint, writes a durable non-audit erasure record and leaves a non-identifying tombstone,
  so a purged number is reclaimable. A legal hold is this entry's `RetentionLegalHold` record and this entry's two
  endpoints, and its whole effect is to stop erasure: a held subject is skipped by every purge and therefore never
  reaches C6's `Closed`, which only an executed erasure produces. It is not an account state, it
  suspends nobody, it refuses no sign-in, it blocks no reactivation and no authorization decision reads it
  (amendment A4). The document rows it protects are C3's: the child fingerprint table with
  `UX_IdentityDocumentFingerprints_Fingerprint` and the `Ciphertext` column, this entry's earlier partial index on
  `IdentityDocuments` and its `ProtectedNumber` spelling being withdrawn.
- **IA-REQ-057 (accepted 2026-09-06 for synthetic data; section 4 governs; numbered 049 in the C7 fragment):** every attempt budget that bounds guessing — login by
  client address, login by normalized account, Platform second-factor verification and step-up, and Platform bootstrap
  recovery, plus C3's `personal.document.claim` — is held in shared PostgreSQL state, one budget across instances and
  restarts. The port is `ISharedAttemptBudget`, its only adapter `PostgreSqlAttemptBudget` over the existing
  database; no new infrastructure product is introduced. An unavailable store fails every budget closed — no attempt is admitted — and the caller is
  told what actually happened: `503` Problem Details `service_unavailable` with `Retry-After`, never `429`
  `rate_limit_exceeded`, which stays reserved for a budget a caller really did exhaust (amendment A5). Every route
  that names a budget carries both answers. Budget writes commit outside any business transaction, and numerical
  budgets are product defaults recorded per environment.

| Caller state | `GET .../retention/policy` | `POST .../retention/holds` | `DELETE .../retention/holds/{holdId}` |
|---|---|---|---|
| Anonymous | `401` | `401` | `401` |
| Authenticated, no active Platform tenant; or lacking the permission | `403` | `403` | `403` |
| Has permission, the session having never proved the second factor, or proved it but not recently | `401` `recent_mfa_required` until the factor has been proved once, then `200` | `401` `recent_mfa_required` | `401` `recent_mfa_required` |
| Has permission and recent step-up, with a missing or invalid antiforgery pair, then with a valid one | `200` (safe method) | `400` `antiforgery_validation_failed`, then `201` / `409` `retention_hold_conflict` | `400` `antiforgery_validation_failed`, then `204` |

| Method and route | Access | Primary result |
|---|---|---|
| `GET /api/platform/retention/policy` | active Platform tenant + `platform.retention.read` + this session has proved the second factor | `200` `PlatformRetentionPolicyResponse { policyId, version, approvedOn, source, personalDataMode, activeHoldCount, categories[] { category, retentionPeriod, trigger, action, evidenceRequired } }`; `401` Problem Details `recent_mfa_required` when the session never proved a factor. The configured policy carries `PolicyId`, `Version`, `Owner`, `ApprovedOn`, `Source`, a `Category` from the closed set `PersonalProfileNames`, `PersonalIdentityDocument`, `SessionRecords`, `AuditEvents`, `OutboxMessages`, `OutboxSecrets`, `DeliveryEvidence`, `PlatformMfaMaterial`, a `RetentionPeriod` ISO-8601 duration with **no default and none proposed**, a `Trigger` of `RecordCreation`/`LastActivity`/`AccountClosure`, an `Action` of `Retain`/`Anonymise`/`Erase`, `EvidenceRequired`, `LegalHolds[]` of `{ HoldId, SubjectIdentityId, ReasonCode, Reference, PlacedAt, PlacedByMembershipId, ReleasedAt }` and declarative `BackupTreatment`; each purge writes an append-only `PersonalDataErasureRecord { RecordId, SubjectIdentityId, Category, PolicyId, PolicyVersion, ExecutedAt, AffectedRowCount }` with a `Restrict` FK in the destructive write's transaction, nulls ciphertext and fingerprint, stamps `PurgedAt`/`PurgePolicyId`/`PurgePolicyVersion` and terminalizes the subject's envelopes and unleased undelivered messages, a leased one still being deliverable. `PersonalDataReadiness : IHostedService` refuses to start on `Real` without `RetentionPolicy:PolicyId`/`Version`/`Owner`/`ApprovedOn`, on `Synthetic` holding a `Real`-classified row, and on `Synthetic` with `IdentityAccess:Deployment:ServesRealUsers=true`; a `Real` deployment with leftover `Synthetic` rows starts, those rows staying ineligible. `GET /api/identity/context` gains `personalData: { "mode": "Synthetic" }`. |
| `POST /api/platform/retention/holds`; `DELETE /api/platform/retention/holds/{holdId}` | active Platform tenant + `platform.retention.manage` + recent MFA step-up + antiforgery; `PlatformRetentionHoldRequest { subjectIdentityId, reasonCode, reference }`, `reference` constrained to `^[A-Za-z0-9._:-]{1,64}$` **(product default on the 64)** | `201` `PlatformRetentionHoldResponse { holdId, subjectIdentityId, reasonCode, reference, placedAt, placedByMembershipId, releasedAt, version }` + `Location`; `409` `retention_hold_conflict` when an active hold with that subject and reason exists; `409` `retention_hold_subject_purged` when a purge committed first; `404` when the subject identity does not exist. Release is a bodyless `204`, idempotent for an already-released or unknown `holdId`, with no conflict status. Neither route changes the subject's account state, its sessions, its permissions or its ability to sign in: placing a hold stops the subject's rows being erased and does nothing else, and releasing one restores nothing but eligibility for erasure |

| Contended write | Winner | Loser's answer |
|---|---|---|
| Two maintenance instances claim the same subject | the instance whose conditional claim affects one row; the claim is a lease (`PurgeLeaseOwner`, `PurgeLeaseExpiresAt`) taken like `OutboxDispatcher`'s five-minute lease, so a crashed instance's claim expires | the subject is skipped and stays reclaimable |
| Hold placement racing a purge | whichever takes `SELECT … FOR UPDATE` on the subject's retention-eligible rows first | a winning hold makes the purge affect zero rows and the cycle records `reason=legal_hold`; a winning purge makes the hold answer `409` `retention_hold_subject_purged`. A hold cannot be made retroactive |
| Two concurrent holds with the same subject and reason, and two concurrent releases of one hold | the partial unique index on `(SubjectIdentityId, ReasonCode) WHERE "ReleasedAt" IS NULL`; release is the conditional `UPDATE … SET "ReleasedAt" = @now WHERE "HoldId" = @id AND "ReleasedAt" IS NULL` | `409` `retention_hold_conflict` for the second hold; both releases receive `204`, the second affecting zero rows |
| Parallel attempts at a budget threshold | the single `INSERT … ON CONFLICT ("Scope","KeyHash","WindowStart") DO UPDATE SET "Count" = "Count" + 1 WHERE "Count" < @budget RETURNING "Count"`, decided by PostgreSQL row locking | exactly the budget is admitted; every further caller receives `429` `rate_limit_exceeded` with `Retry-After` derived from the same row. `IdentityAttemptBudgets` holds `Scope`, `KeyHash`, `WindowStart`, `Count`, `ExpiresAt` under PK `(Scope, KeyHash, WindowStart)`; **product defaults** recorded per environment are client 20 per 5 minutes, account 10 per 15 minutes, MFA 5 per 15 minutes, bootstrap recovery 5 per 15 minutes and C3's `personal.document.claim` 3 per identity per 24 hours, with a fixed `Retry-After: 30` on the `503` an unreachable store produces, fixed windows keeping the inherited `2 × budget` boundary, and bootstrap recovery's key moving to the trusted forwarded-headers address. |

- **Permissions.** `platform.retention.read`, `platform.retention.manage` (PROPOSED, tenant-scoped `Platform`, in
  `Permissions.Catalog` and in neither application-scoped set; manage does not imply read); `platform.identities.read`
  and `platform.audit.read` (EXIST, tenant-scoped `Platform`) unchanged, the first operationally required alongside
  manage. Purge, erasure-record writing and budget cleanup have no endpoint and no permission.
- **Audit and outbox (proposed).** Audit `personal.data.purged`, `personal.data.anonymised`,
  `personal.data.retention.skipped` (once per cycle; `reason` `policy_absent`, `legal_hold` or
  `synthetic_classification`), `personal.data.hold.placed`, `personal.data.hold.released` and
  `identity.attempt.budget.unavailable` (best-effort, at most once per scope per window, only once the database is
  reachable — the metric, not the audit table, is the operator's signal), all with the Platform tenant id and, for
  worker events, a reserved non-impersonating system actor; individual denials are not audited. **Outbox: none.**
- **Amends.** Extends IA-REQ-044's allowlist with the retention event types and the two hold DTOs while adding no
  metadata key, `AuditEvent` still admitting only `reason`, `code` and `outcome`, which is why erasure evidence is a
  separate record; and amends IA-REQ-026, which requires an audit record for every sensitive denial. Amendment A5
  adds `service_unavailable` as a stable code — the first `503` any Application request produces — leaving
  `rate_limit_exceeded` to mean only an exhausted budget; and amendment A4 removes the `LegalHoldAt` marker C6
  proposed, this entry's `RetentionLegalHold` record being the only legal hold there is.
- **Consumed by** Task 19 first: the `DataClassification` stamp and the shared budget port, adapter and table are
  persistence, and Task 20 cannot expose Personal registration without a budget already in place. Then Task 26's
  retention work, and Task 27's remaining budget scopes, cross-instance proof and startup guards — `PersonalDataReadiness`
  belongs there with the other fail-closed startup checks, not in Task 19, which only stamps rows. Task 28 records the
  G1 local closure; G2 (real personal data) and G3 (production) stay blocked behind their own owners.

## 15. Reference adoption map (proposed — Task 17 Step 2)

**Pinned source:** repository `RepositorioBaseNet`, revision `052a39873ed74a3c66c502c521a2469dfbeb523d`,
`docs/standards/identity-access/02-business-rules.md` and `05-authentication.md`. That is a source identifier,
not a dependency on one machine's filesystem path, and not an authorization to install anything the reference
happens to run on.

Three columns of this table say different things and are never interchangeable. **Adopted semantics** are rules
this product takes on as its own. **Proposed portable implementation** is how this repository would satisfy them
with what it already has. **Unadopted external stack** is machinery the reference uses that this repository does
not take on — naming it as unadopted is not the same as waiving the guarantee it implements, and no row below is
closed by deciding not to install a product.

| Source rule / section | Adopted semantics | Proposed portable implementation | Task | Planned evidence |
|---|---|---|---|---|
| BR-REG-001..005, `05` §Registro de persona B2C / §Registro de empresa B2B | Signup is one logical operation; a partial failure never leaves a tenant without its owner; duplicates are refused without disclosing more than necessary; a documentary collision answers neutrally and never links another account | IA-REQ-048's two-phase intent/proof registration, inside the existing `IApplicationTransaction` and `RegistrationSubmission` idempotency record | 18, 19 | Paired-sequence privacy tests, real-PostgreSQL concurrent confirmation, rollback-after-effect tests |
| BR-ID-003/006/007, `05` §Registro de persona B2C | Personal belongs to no organization; AR requires `AR`+`DNI`; the normalized documentary identity is globally unique; the number is stored protected with a keyed fingerprint for uniqueness | IA-REQ-050's `PersonProfile`/`IdentityDocument` reusing the established encrypted-value + versioned-keyed-fingerprint shape and a PostgreSQL partial unique index over unpurged rows | 19, 20 | Mapping and constraint tests on real PostgreSQL; concurrent claim of one document leaves one valid graph |
| BR-ID-005, `05` §Inicio de sesión con Google steps 6–9 | Provider identity is located by provider+subject, never by display name or by matching email; an existing email match neither signs in nor links; linking is initiated from an existing session with an explicit `Link` purpose, recent reauthentication, consent, and a verified provider email | IA-REQ-052 over ASP.NET Core OpenID Connect middleware and the existing `AspNetUserLogins` provider-key uniqueness | 23 | Callback validation tests, no-auto-link tests, purpose-crossing refusal |
| BR-SEC-001/002/003, `05` §Cierre y revocación de sesiones, §Contraseña | The web cookie is not the source of truth; authenticating or elevating regenerates the session identifier; closing a session invalidates its cookie and persisted state; a sensitive change revokes sessions according to policy | IA-REQ-049's session list, revoke-one, revoke-others and eviction cap over the existing `UserSession` and `SessionCookieEvents` | 21, 22 | Real-HTTP cookie tests, concurrency tests on the cap, revocation audit |
| `05` §Contraseña §Recuperación steps 1–6 | Neutral answer always; a one-time token issued through the outbox; the token expires and is never persisted in plain text; setting a new password raises the security version and revokes sessions; a security event is recorded | IA-REQ-051's reset token in the existing `OutboxSecret` envelope with fragment delivery | 22 | Delivered-link acceptance journey, neutrality tests, session-revocation tests |
| `05` §Contraseña §Cambio autenticado | Requires the current password or an equivalent reauthentication; a Google-only account defines a password through a verified flow, never a fictitious value | IA-REQ-051's single-use recent proof bound to identity, session, action and security version | 21, 22, 23 | Proof consumption/expiry tests, Google-only path test |
| BR-AUT-001..008, `06-authorization.md` | The catalogue belongs to deployed code; administrators create custom roles only from permissions they hold; effective permissions are the union of active assigned roles; deny by default; a role change raises the authorization version; protected system roles cannot be removed or stripped; at least one membership able to administer the tenant must remain; `platform.*` only in the Platform tenant | IA-REQ-053 extending the existing `Role`/`RolePermission`/`MembershipRole` model, the permission evaluator, and `TenantAuthorizationAuditInterceptor` | 24, 25 | Delegation-ceiling tests, last-effective-administrator refusals, evaluator and version tests |
| BR-INV-001..005, BR-TEN-004/006 | An invitation belongs to one tenant, email and role set; the token is hashed, expiring and single-use; acceptance binds the authenticated matching identity; reissuing creates no duplicate membership or unlimited live tokens; invitation and outbox message are one transaction; deactivating a membership does not delete the identity or its other memberships | Already implemented as IA-REQ-014..018/047; IA-REQ-053 adds the widening-cancels-offers rule, and IA-REQ-054 adds the membership lifecycle | 25, 26 | Offer-cancellation atomicity, membership lifecycle transitions |
| `05` §Autenticación multifactor | TOTP with one-time recovery codes; reauthentication to enable, disable or regenerate; mandatory for platform accounts; secrets encrypted at rest with an isolated purpose; audited without revealing the secret | Already implemented for Platform (IA-REQ-041); IA-REQ-054 adds factor recovery requiring fresh primary proof **and** an unused recovery code | 26 | Recovery-code consumption tests, refusal without fresh proof |
| BR-SEC-004/005, `05` §Observabilidad requerida | Failed authentication, recovery, 2FA, membership changes and sensitive denials are audited; secrets, tokens, personal documents and their normalized form never reach logs, URLs, history or analytics | Already implemented as IA-REQ-026/029; IA-REQ-050 extends it to the document and IA-REQ-052 to provider codes and tokens | 19–27 | Negative leak scans over responses, logs, audit and outbox payloads |
| `05` §Bootstrap inicial de Platform (lines 240–251) | An external lifecycle ledger appends `PlatformBootstrapPrepared` **before** privileges are created; a Prepared bound to another target, or any Completed, fails closed; Production/PII and the public edge stay closed until a restore point at or after `MinimumRecoverableRestorePointUtc` is verified with generation, source binding and evidence | **Guarantee adopted, implementation not.** IA-REQ-055 requires an operator-controlled admission record held outside the restored database and a named verification authority. This repository proposes no ledger product; until the record and its authority are accepted and proved, live restore release stays blocked | 26, 27, gate in 28 | Fail-closed admission tests with synthetic evidence; the live gate is explicitly **not** closed by them |
| `05` §Restore de seguridad (lines 259–317, 428) | A restore ceremony uses an audited `RecoveryEpoch`; sessions, invitations and outbox rows carry `IssuedRecoveryEpoch` and a new epoch invalidates the previous ones; infrastructure credentials are revalidated against their own source, not against the restored database; proofing binds a challenge to epoch, opaque target, purpose/policy/provider and nonce; an RPO failure fails closed; two approvers, separate permanent roles, dual control and explicit monitoring | **Guarantees adopted, stack unadopted.** IA-REQ-055 carries the epoch, the revalidation rule, the fail-closed default and the quarantine-after-restart rule. The reference's Azure/Entra recovery application, WORM ledger, P2S administrative network, federated identity credentials and `ISecurityRecoveryProofProvider` implementation are **not adopted** — that is a scope decision, not a statement that the guarantees are optional | 26, 27, gate in 28 | Synthetic-evidence guard tests only; full-reference compliance remains blocked and is not claimed |

### Named differences from the reference

These are deliberate local deviations. Each is a proposal in its own right and is listed here so no reader mistakes
this product for a compliant implementation of the source.

1. **Registration timing.** The reference creates the Personal aggregate — profile, document, tenant, owner
   membership — in the same transaction as signup, before the address is confirmed (`05` §Registro de persona B2C,
   steps 5–10). IA-REQ-048 defers every exclusive durable reservation until after confirmation, because the
   reference's timing is exactly what makes an unconfirmed anonymous request leave a state difference an attacker
   can read. The atomicity guarantee itself is kept: the aggregate is still created in one transaction, just a
   later one.
2. **Endpoint combination.** The reference describes journeys, not routes. This product's HTTP surface stays as
   SPEC §6 declares it — one endpoint per business transition, semantic statuses, RFC 9457 for every failure —
   rather than a combined account controller.
3. **Recovery architecture.** Described above: guarantees adopted, Azure/Entra/WORM stack unadopted.
4. **Session revocation policy.** The reference says sessions are revoked "según política" and fixes no number.
   The five-session cap in IA-REQ-049 is this product's proposed default, not a reference requirement.
