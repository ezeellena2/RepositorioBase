# Identity and Access Specification

## Purpose

Define the implemented identity, tenancy, authorization, session, invitation, personal identity, and Platform
administration baseline. This specification records behavior demonstrated by the current source and executable test
suites and implements
[ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md).

This is a current baseline, not the retired roadmap. Behavior still being designed or delivered by an active change,
including account setup and onboarding owned by `openspec/changes/account-setup-onboarding/`, is deliberately absent
until that change is applied, verified, and archived.

## Requirements

### Requirement: IA-REQ-001/002 — Identity is global and tenant-neutral

One normalized email MUST identify at most one active local identity. `ApplicationUser` MUST NOT carry `TenantId`, a
tenant role, CUIT, documentary identity, or a B2C/B2B discriminator. Tenant participation MUST be represented by
memberships.

#### Scenario: One identity joins another tenant

- **WHEN** an existing confirmed identity accepts an invitation from another tenant
- **THEN** the system reuses the global identity and creates at most one membership for that tenant
- **AND** it does not create a second account or copy tenant data onto the identity.

### Requirement: IA-REQ-003/048 — Exclusive registration state requires proof of control

An exclusive durable reservation, including a normalized CUIT, organization, tenant, membership, role, personal
document, or global email identity, MUST be created only for a validated persisted session or a single-use token
delivered to and spent by the identity's email address. Unproven initiation MUST leave no state that changes another
caller's answer.

Anonymous organization registration MUST return a neutral `202`, persist only a bounded pending intent and delivery
effect, and reveal neither email nor CUIT existence. Authenticated registration MUST derive the address from the
validated session and reject a conflicting CUIT with `409 registration_conflict`. CUIT input MUST normalize to eleven
digits and satisfy the AFIP modulo-11 verifier.

#### Scenario: An anonymous caller submits a known address or CUIT

- **WHEN** the caller starts organization registration without a validated session
- **THEN** the API returns the same neutral bodyless `202` for known and unknown state
- **AND** creates no identity, tenant, organization, membership, role, or CUIT claim before email proof is spent.

#### Scenario: Proof arrives after the CUIT was claimed

- **WHEN** a person spends a valid email token after another organization claimed the CUIT
- **THEN** the proved identity may still be activated
- **AND** organization creation is refused with `409 registration_conflict` without creating a partial graph.

### Requirement: IA-REQ-004 — Registration is atomic and idempotent

Organization creation MUST write identity, tenant, profile, responsible membership, initial roles, audit, and outbox
effects in one consistency boundary or write none. Equivalent anonymous submissions MUST reuse the completed neutral
response without duplicating intent, message, or audit state. Confirmation tokens MUST be single-use and later spends
MUST return the recorded terminal outcome.

#### Scenario: The same initiation is replayed

- **WHEN** an equivalent anonymous registration request is submitted more than once
- **THEN** every response remains bodyless `202`
- **AND** only one intent, delivery effect, and audit record exist.

### Requirement: IA-REQ-005 — Confirmation purposes remain distinct

Email MUST be confirmed before invitations are accepted, roles are administered, members are invited, or sensitive
operations execute. Organization, personal, invited-member, and Platform confirmation envelopes MUST be classified by
their exact message type and MUST NOT cross purposes. Invalid or terminal personal/member/Platform confirmation
answers `400 invalid_confirmation`; terminal organization registration answers `409 registration_conflict`.

#### Scenario: A token is presented to the wrong confirmation flow

- **WHEN** a valid token for one confirmation purpose reaches another purpose
- **THEN** it is refused without confirming the identity or activating a membership.

### Requirement: IA-REQ-006–013 — Tenant context is server-owned and authorization is deny-by-default

Every tenant-scoped operation MUST resolve `TenantId` from the validated persisted session's `ActiveTenantId` after
checking session, tenant, and membership state. Client headers, queries, claims, or storage MUST NOT establish tenant
context. Each request MUST use one `(UserId, TenantId)` membership and MUST NOT accumulate permissions across tenants.

Application requests MUST either declare their permission and tenant requirement or explicitly implement the public
request marker. Roles group code-defined `resource.action` permissions within one tenant; display role names MUST NOT
authorize. Tenant-scoped reads MUST filter by validated tenant and identifier. Authorization-changing writes MUST
advance `AuthorizationVersion` in the same transaction.

#### Scenario: A caller supplies a different tenant identifier

- **WHEN** a caller attempts to select tenant context through a header, query, claim, or client storage
- **THEN** authorization ignores that input and evaluates only the validated persisted session and membership.

#### Scenario: A request has no authorization declaration

- **WHEN** architecture validation discovers an Application request that is neither authorized nor explicitly public
- **THEN** the architecture test fails.

### Requirement: IA-REQ-014–018/047 — Invitations are tenant-bound, private, and bounded by inviter authority

An invitation MUST belong to one Organization, normalized recipient address, and roles from that tenant. Its random
token MUST be stored only as a hash, expire, be single-use, and be invalidated on cancellation or acceptance. A new
offer to the same recipient MUST replace the live offer atomically rather than create parallel usable tokens.

An inviter MUST offer only roles whose effective permissions are already held by that inviter in the tenant.
Acceptance MUST require a confirmed authenticated identity matching the recipient and create at most one membership.
The usable token MUST NOT enter outbox payloads, logs, audit, Problem Details, or telemetry; delivery MUST use an
encrypted expiring secret envelope and terminalize it after acknowledged delivery.

#### Scenario: An inviter offers authority they do not hold

- **WHEN** an invitation includes a role with any permission outside the inviter's effective set
- **THEN** issuing, reissuing, or replacing the offer is refused without creating a usable invitation.

#### Scenario: Delivery succeeds and is retried

- **WHEN** an adapter acknowledges the logical invitation message
- **THEN** the secret ciphertext is cleared and non-secret delivery evidence is stored
- **AND** a retry consults delivery state instead of sending the logical message twice.

### Requirement: IA-REQ-019–025 — Sessions are persisted, bounded, and securely transported

Local sign-in MUST use email and password over HTTPS, apply shared IP/account budgets and lockout, and return generic
responses. Only a confirmed `Active` identity may receive a session. The protected cookie MUST reference a persisted
`UserSession`, use `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, and no `Domain`, and MUST NOT be the durable
permission source. Authenticated mutations MUST enforce exact-origin and antiforgery checks.

Sessions MUST have a 30-minute idle and 12-hour absolute lifetime by default. Sign-out MUST revoke the persisted
session and delete the cookie. React MUST NOT place authentication cookies, tickets, JWTs, invitation tokens, or
antiforgery tokens in web storage.

#### Scenario: A session is revoked or expires

- **WHEN** a protected request presents its former cookie
- **THEN** the server returns `401` and does not authorize from cookie contents alone.

### Requirement: IA-REQ-049 — Session coexistence has a deterministic cap

An identity MUST have at most five live sessions at commit. Issuance MUST serialize per identity and revoke the
oldest live sessions by `(CreatedAt, Id)` until the new session fits. A person MAY list and revoke only their own
sessions by opaque references. Password reset MUST revoke all sessions and issue none; password change MUST revoke all
other sessions and rotate the acting session without carrying its identifier, antiforgery pair, recent proof, or MFA
evidence forward.

#### Scenario: A sixth session is issued

- **WHEN** five live sessions already exist
- **THEN** the oldest session is revoked deterministically and the new session commits in the same boundary.

### Requirement: IA-REQ-051 — Sensitive self-service uses a single-use recent proof

A sensitive self-service mutation MUST consume a server-side proof bound to one identity, session, action, and
security version. The default lifetime is five minutes. A valid session alone MUST NOT qualify. Password proof or a
fresh signed `auth_time` from an already-linked provider may issue proof; missing, future, unreadable, or stale provider
authentication time MUST be refused. Credential or authenticator change MUST advance the security version and
invalidate outstanding proofs. Platform MFA step-up is separate and MUST NOT substitute for this proof.

#### Scenario: A proof is replayed or used for another action

- **WHEN** a proof has already been consumed or does not match the current session, action, or security version
- **THEN** the sensitive mutation is refused without changing protected state.

### Requirement: IA-REQ-052 — External identities are explicitly linked

An identity MAY have at most one link per external provider. Linking MUST require explicit consent, a recent primary
proof, a confirmed identity, and provider-verified email. Matching email MUST NOT auto-link accounts. Unlinking MUST
NOT remove the last usable authenticator. `Login`, `Link`, and `Proof` purposes MUST remain separate in server-side
handoff state. The callback performs no business mutation and MUST validate one-use state, framework correlation,
nonce, PKCE `S256`, issuer, audience, signature, and expiry.

The historical provider-based `Recovery` purpose is not asserted by this baseline because the former traceability
record did not demonstrate it. Password recovery remains governed by current executable behavior.

#### Scenario: Provider login matches an existing local email

- **WHEN** an unlinked provider identity presents a verified address that already belongs to a local identity
- **THEN** login is refused with `409 external_login_conflict` and no link is created.

### Requirement: Password recovery is neutral and invalidates prior sessions

Password-recovery initiation MUST return the same neutral response regardless of account state and MUST NOT reveal
whether an address exists. A usable reset token MUST be single-use, purpose-bound, time-bounded, and compared through
its protected/hash representation. A completed reset MUST change the credential, advance identity security version,
revoke every persisted session, invalidate earlier recovery material, and issue no session.

#### Scenario: Recovery is requested for known and unknown addresses

- **WHEN** public recovery initiation receives either address within the shared caller budget
- **THEN** both requests receive the same neutral response and no observable account-state distinction.

#### Scenario: A reset completes

- **WHEN** a valid current recovery token and acceptable new password are submitted
- **THEN** every existing session and outstanding identity proof becomes unusable
- **AND** the person must sign in again with the new credential.

### Requirement: IA-REQ-053 — Organization administration preserves delegation ceilings and an administrator floor

Organization roles MAY be customized, but an actor MUST NOT grant permissions they do not effectively hold or that
the tenant type disallows. Role-permission edits, membership-role changes, and ownership transfer MUST consume their
own recent proof. No write may leave the Organization without one effective administrator holding both
`roles.manage` and `members.manage`. Ownership MUST have exactly one active same-tenant holder; the owner's membership
cannot be suspended or revoked. Widening or retiring an offered role MUST cancel affected pending invitations and
their undelivered secrets in the same transaction.

#### Scenario: A role change would remove the final effective administrator

- **WHEN** the flushed post-change permission state contains no qualifying administrator
- **THEN** the complete mutation is rolled back.

#### Scenario: Ownership is transferred

- **WHEN** the current owner supplies a live action-specific proof and an active confirmed same-tenant recipient
- **THEN** the ownership reference and related authorization effects change atomically.

### Requirement: IA-REQ-050 — Personal identity data is owner-scoped and protected

An identity with a `Personal` tenant MUST have exactly one owner-only `PersonProfile`. Self-service editing MAY change
only `FullName` and `DisplayName`; it MUST NOT change email, ownership, or documentary identity. The canonical
`country|type|number` document tuple MUST be authenticated-encryption ciphertext with keyed versioned fingerprints.
Plaintext MUST leave protection only at named seams and MUST NOT reach logs, audit, outbox, Problem Details, OpenAPI,
or responses. The owner sees only masked document status. Duplicate document and already-owned Personal context MUST
share `409 personal_registration_conflict`.

#### Scenario: A person reads and updates their profile

- **WHEN** the owner reads the profile and updates only accepted name fields with the current version
- **THEN** the projection remains masked and email, ownership, and document values remain unchanged.

### Requirement: IA-REQ-058 — Documentary correction is a two-party dispute

A recorded documentary identity MUST be corrected only through the implemented dispute workflow, never ordinary
profile editing. The owner MUST open a dispute over their own document with a live action-specific recent proof. A
Platform operator with distinct permission, recent MFA, a stored open dispute, and an external evidence reference MAY
resolve it, but MUST NOT resolve their own identity. At most one dispute per identity may be open. Correcting MUST
replace ciphertext and all retained fingerprints atomically and record no plaintext; rejection MUST leave the
document unchanged.

#### Scenario: A Platform operator corrects an open dispute

- **WHEN** an authorized different operator supplies recent MFA and a valid evidence reference
- **THEN** ciphertext and retained fingerprints are replaced in one transaction and the dispute becomes corrected
- **AND** no documentary value appears in audit or response data.

### Requirement: IA-REQ-054 — Identity and membership lifecycle is explicit

Identity state MUST be one of `PendingConfirmation`, `Active`, `SelfDeactivated`, `AdministrativelySuspended`, or
terminal `Closed`; only `Active` may sign in. Lifecycle transitions MUST be conditional, audited, and atomic with
session, token, and outbox effects. Self-reactivation MUST use a public single-use hashed ticket with a 30-minute
default lifetime and a current password; it MUST restore the prior eligible state and never lift administrative
suspension. Legal hold and credential recovery MUST NOT be treated as account lifecycle transitions.

#### Scenario: A self-deactivated identity returns

- **WHEN** the identity proves the current password through a live reactivation ticket
- **THEN** it returns to its prior eligible state without bypassing administrative suspension or MFA.

### Requirement: IA-REQ-055 — Restore admission fails closed

A restored deployment MUST admit no public ingress, issue no session, accept no restored session or one-time token,
and dispatch no restored outbox delivery until external operator-controlled evidence verifies against an
operator-held key. Missing, unreadable, expired, wrongly signed, wrong-deployment, or non-advancing evidence MUST keep
admission closed on every process start.

#### Scenario: Only restored database state is available

- **WHEN** the deployment starts without valid external admission evidence
- **THEN** restored data cannot authorize ingress, sessions, tokens, or delivery.

### Requirement: IA-REQ-026–032 — Audit and outbox effects are durable and secret-free

Sign-in, confirmation, sessions, invitations, memberships, roles, lifecycle changes, and sensitive denials MUST write
append-only audit records with correlation identifiers and no secrets. Reliable external effects MUST write an outbox
message in the business transaction. The worker MUST claim by lease and compare-and-swap, retry with backoff, and use
idempotent handlers. Passwords, tokens, cookies, codes, connection strings, and personal data MUST NOT appear in logs,
Problem Details, audit, outbox payloads, or telemetry.

#### Scenario: A business transaction that queues delivery fails

- **WHEN** the transaction cannot commit all business, audit, and outbox state
- **THEN** none of those effects remain committed.

### Requirement: IA-REQ-033–037 — Persistence enforces tenant and concurrency boundaries

PostgreSQL constraints MUST protect normalized email, slug, CUIT, membership, document fingerprint, and token-hash
uniqueness. Membership/role and invitation/role associations MUST use tenant-bearing composite keys and foreign keys.
Sensitive mutations MUST use concurrency tokens or conditional updates and map a lost update to `409`. Foreign-key
delete behavior MUST be explicit and MUST NOT cascade away identity, authorization, or audit history. Migrations MUST
be tested from empty and predecessor databases; delete/create is not a normal migration strategy.

#### Scenario: Cross-tenant association is attempted

- **WHEN** application code attempts to persist a membership-role or invitation-role link across tenants
- **THEN** PostgreSQL rejects the combination.

### Requirement: IA-REQ-056 — Retention and personal-data mode fail closed

`IdentityAccess:PersonalData:Mode` MUST be `Synthetic` or `Real`; absent, blank, or invalid configuration MUST resolve
to `Synthetic`. Personal profile and document rows MUST carry server-derived persisted classification that callers
cannot set. Retention policy MUST be configuration, with no destructive action when policy is absent. Purge MUST erase
ciphertext and fingerprints together, retain non-identifying erasure evidence, and allow the document number to be
reclaimed. A legal hold MUST stop erasure only and MUST NOT affect sign-in, lifecycle, or authorization.

#### Scenario: No retention policy is configured

- **WHEN** maintenance runs in either personal-data mode
- **THEN** it performs no destructive action.

#### Scenario: A subject has a live legal hold

- **WHEN** that subject becomes eligible for retention processing
- **THEN** erasure is skipped while authentication and authorization behavior remain unchanged.

### Requirement: IA-REQ-057 — Guessing budgets are shared and fail closed

Every guessing budget MUST live in shared PostgreSQL state across instances and restarts behind
`ISharedAttemptBudget`. An unavailable store MUST admit no attempt and return `503 service_unavailable` with
`Retry-After`; `429 rate_limit_exceeded` is reserved for an actually exhausted budget. Budget writes MUST commit
outside business transactions so rollback cannot restore an attempt.

#### Scenario: Two application instances spend one budget

- **WHEN** attempts for the same protected key cross instance boundaries
- **THEN** both instances observe one shared threshold and the attempt after exhaustion returns `429`.

### Requirement: IA-REQ-038 — HTTP failures use the shared Problem Details contract

Expected business failures MUST use typed `Result` values; unexpected failures remain exceptions. Web MUST map both
to endpoint-specific success DTOs or bodyless statuses and RFC 9457 `application/problem+json`, never a universal
success envelope or serialized internal Result. Problems MUST contain matching status, stable code, opaque trace ID,
optional safe detail, and structured validation errors only where applicable. A safe `500` MUST expose no internal
diagnostics. `401`, `403`, and `429` MUST use the same writer; cross-tenant absence MAY return `404`.

React MUST consume one typed transport boundary. It MUST preserve valid server Problems and otherwise emit only the
status-`0` client codes `network_unavailable`, `request_timeout`, `unreadable_response`, or `client_failure`. Reads are
retryable only for status `0`, `429`, or `>=500`; mutations MUST NOT replay automatically. Offset-paged Platform
directories MUST follow the canonical
[API offset pagination specification](../api-offset-pagination/spec.md); identity endpoints MUST NOT gain a generic
pagination envelope.

#### Scenario: An unexpected server failure occurs

- **WHEN** infrastructure or programmer code throws unexpectedly
- **THEN** the response is a safe generic `500` Problem with a trace identifier and no internal detail.

#### Scenario: A mutation has an unknown transport outcome

- **WHEN** the client loses the response to a submitted mutation
- **THEN** the transport does not automatically replay it and the person must refresh or reconcile state.

### Requirement: IA-REQ-059 — Identity language follows the localization contract

`ApplicationUser.PreferredLanguage` MAY be null or a canonical supported language. Own-account language mutation
MUST derive the identity from the validated session and MUST NOT change credential, security, session, tenant, or
authorization state. Registration and invitation intents MUST snapshot trusted request language; a flow that finds an
existing account MUST NOT overwrite its preference. Delivery MUST use account preference, then the immutable snapshot,
then default, bind that language on first preparation, and keep invariant API/outbox/audit surfaces. The complete
cross-project behavior is defined by the
[localization specification](../localization/spec.md).

#### Scenario: An invitation is retried after account preference changes

- **WHEN** the logical message was first prepared from its account/snapshot/default precedence
- **THEN** every retry uses the originally bound language and reveals no account-existence distinction.

### Requirement: IA-REQ-039–041 — Platform authority uses normal tenant membership and MFA

Exactly one reserved Platform tenant MAY exist. Platform owners and administrators MUST be ordinary global identities
with active Platform memberships and explicit `platform.*` permissions evaluated through the normal tenant path; no
super-admin claim, global role, or context bypass is allowed.

Bootstrap MAY create the singleton tenant, system owner role, pending invitation, audit, and outbox effect only while
the Platform tenant is absent and only for the explicitly configured recipient. It MUST create no password. Platform
membership activation MUST require a confirmed identity, completed encrypted TOTP enrollment, acknowledged hashed
recovery codes, and an MFA-authenticated session. Platform mutations MUST require recent session-bound MFA. Attempt
budgets MUST return `400 invalid_mfa_code` or `400 invalid_recovery_code` for rejected credentials, `429` only on
exhaustion, and `503` on store failure.

#### Scenario: Configuration changes after Platform activation

- **WHEN** the configured bootstrap address changes after the first owner activates
- **THEN** no identity is replaced or elevated and bootstrap remains permanently closed.

### Requirement: IA-REQ-042–046 — Platform administration is explicit and non-destructive

Platform administrators MUST be invited and revoked through Platform invitations and memberships. The system MUST
preserve at least one active Platform owner. Authorized administrators MAY conditionally suspend or reactivate an
Organization with a closed reason and audit; the effect MUST be concurrency-safe and immediately visible to normal
authorization. Platform itself MUST NOT be suspended or deleted through those endpoints.

Platform organization, identity, administrator, retention, and audit views MUST be allowlisted operational
projections. They MUST NOT expose CUIT, tenant business data, arbitrary profiles, credentials, secrets, tokens, or
mutable audit payloads. The Platform surface MUST NOT implement impersonation, destructive identity/tenant deletion,
arbitrary tenant selection, private tenant-data access, default credentials, or authorization bypass.

#### Scenario: The final Platform owner is revoked

- **WHEN** a mutation would leave no active Platform owner
- **THEN** it is refused and the current authority remains intact.

#### Scenario: An operator reads a Platform directory

- **WHEN** an MFA-authenticated Platform session holds the directory permission
- **THEN** the endpoint returns only its allowlisted typed projection under canonical offset paging.

## Current boundaries and evidence

The implemented evidence lives in `src/Domain/IdentityAccess`, `src/Application/IdentityAccess`,
`src/Infrastructure/IdentityAccess`, `src/Web/Endpoints`, and the Domain, Application, integration, React, and
Reqnroll/Playwright test suites. Those executable checks remain the evidence for this baseline.

This specification intentionally does not claim retired roadmap items solely because they appeared in the former
proposal. Provider-based recovery is not asserted. Account setup, setup guards, onboarding routing, and other behavior
owned by the active `account-setup-onboarding` change are not current baseline until that change completes its apply,
verify, and archive lifecycle.
