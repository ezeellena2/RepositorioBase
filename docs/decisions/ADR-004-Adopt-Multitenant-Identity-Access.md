# ADR-004: Adopt Multitenant Identity Access with React and PostgreSQL

**Status:** Proposed  
**Date:** 2026-08-31

## Context

The repository is currently the source of a Clean Architecture template with Angular/React/API-only clients and PostgreSQL as its only database provider. Its reference sign-in uses default ASP.NET Core Identity endpoints, general roles/claims, a minimal React client, open CORS, database recreation in Development, and a default administrator password.

The target starter needs global identity, multitenant organizations, memberships, contextual authorization, invitations, revocable sessions, audit, and reliable external effects. The functional standard comes from the external `CleanArchitecture` reference repository at `docs/standards/identity-access`.

## Decision

1. Specialize the target product for ASP.NET Core 10, React 19, and PostgreSQL.
2. Serve React and the API same-origin and use a protected BFF cookie; do not expose authentication tokens to JavaScript.
3. Use ASP.NET Core Identity only as a credential and technical-identity adapter. Tenant, membership, role, permission, invitation, and session are Domain/Application concepts.
4. Keep one global identity and authorize within an active tenant resolved only from the validated persisted session.
5. Define permissions in code and dynamic roles in PostgreSQL; do not authorize by role name or durable role claims.
6. Persist `UserSession` for server-side revocation and active-tenant selection, and require antiforgery for state-changing web requests.
7. Make Application authorization deny-by-default; only requests carrying an explicit public marker bypass permission evaluation.
8. Use a transactional outbox for confirmation, invitation, and recovery messages, with usable tokens held only in separate encrypted expiring secret envelopes.
9. Protect multitenant invariants with PostgreSQL constraints and composite foreign keys.
10. Remove default administrator credentials and destructive database recreation; evolve the schema with migrations.
11. Implement incrementally through this repository's SDD protocol without claiming compliance for pending slices.
12. Keep internal failure flow distinct from the HTTP contract. Domain/Application return typed `Result`/`Result<T>` only for expected business failures with stable codes/categories; unexpected infrastructure or programmer failures remain exceptions. Web owns semantic success mapping, the central RFC 9457 Problem Details writer, and OpenAPI metadata; it never serializes Result or a universal success/error envelope. React owns one typed API parser/client boundary and never depends on internal Result.
13. Make organization registration idempotent in Application: a durable submission record keyed by canonical normalized caller scope and registration intent is atomically claimed before business effects and retains the completed neutral response. Its unique key coordinates concurrent claims but does not replace the Application idempotency decision; true IA-REQ-003 conflicts retain their deterministic branches.
14. Model Platform administration as one reserved singleton `TenantType.Platform`. A Platform owner is an ordinary global identity with an active Platform membership, active Platform tenant, and explicit `platform.*` permission through the same evaluator used elsewhere; no global claim, boolean, `IsSuperAdmin`, or context bypass exists.
15. Bootstrap the first owner only from an explicitly configured deployment-owner email and only while Platform is absent. The one transaction creates the tenant, system owner role, pending invitation, audit, and outbox intent without a password or embedded secret. Before activation, recovery is bodyless, same-origin, antiforgery-protected, rate-limited, and idempotent: it takes no email or identity, derives only the unchanged configured/pending recipient, and may rotate an expired/permanently failed invitation without activation/elevation. Valid opaque states are neutral `202`; invalid antiforgery is typed `400 antiforgery_validation_failed`, and rate rejection is `429 rate_limit_exceeded` with `Retry-After`. A configuration change never replaces the pending owner; activation permanently closes bootstrap.
16. Platform invitation onboarding is token-aware. It creates a missing matching global identity with the submitted PasswordOptions-valid password, or ignores credential material for an existing identity and returns only generic sign-in/confirmation behavior; it never generates a password. It then sends/consumes confirmation through the existing outbox pattern and does not activate membership. Require confirmed identity, encrypted TOTP enrollment, hashed recovery-code acknowledgement, and an MFA-authenticated session before Platform membership activation. Enrollment/verification/recovery acknowledgement require antiforgery, the bound one-time invitation token, and the authenticated confirmed identity matching its normalized email; a session may have no active Platform tenant until activation. Require recent MFA step-up for Platform mutations; administrator changes use invitation/revocation and protect the last active owner.
17. Limit Platform operations to explicit permissions and safe operational projections: organization lifecycle is conditional, audited, and immediately effective through normal authorization; Platform itself cannot be suspended/deleted. The panel may inspect only documented allowlisted organization, identity, administrator, and global security/audit projections, never private tenant/business data, secrets, credentials, raw tokens, CUIT, arbitrary profile payloads, or mutable audit history. Platform directories use typed bounded/cursor responses and do not alter `/api/identity/*` contracts.

This decision adopts the reference standard's business semantics, not its repository workflow or delivery methodology.

## Rejected alternatives

- **Keep Identity roles as the primary authorization model:** this does not isolate permissions by tenant and makes business access depend on potentially stale claims.
- **Store JWTs in React local storage:** this increases XSS exposure and provides no benefit for a same-origin SPA.
- **Create one user per organization:** this duplicates identity, complicates invitations, and contradicts context switching within one session.
- **Retain multiple database providers:** this weakens the PostgreSQL-specific guarantees required for partial indexes, composite foreign keys, and baseline concurrency behavior.
- **Send email inside the HTTP transaction:** this cannot make a business change atomic with an external provider.
- **Wrap every HTTP response in `{ success, data, error }`:** this hides HTTP semantics, duplicates Problem Details, weakens generated contracts, and leaks an internal control-flow model across Web and React.
- **Add `IsSuperAdmin`, a global claim, or a role bypass:** this bypasses active-tenant membership and makes revocation/audit semantics unverifiable.
- **Create a default Platform administrator or password:** this creates an unaudited credential and cannot prove deployment-owner intent.
- **Elevate arbitrary users directly:** this skips invitation, confirmation, MFA, recovery acknowledgement, and last-owner protections.
- **Impersonate users or expose tenant-private data from Platform:** these are unnecessary for the approved operational slice and violate tenant isolation.

## Consequences

### Positive

- verifiable tenant-scoped isolation;
- revocable sessions and permissions without waiting for claims to expire;
- one user across multiple organizations;
- recoverable invitation and email infrastructure;
- business rules testable without React, cookies, or an email provider.
- a constrained, auditable Platform control plane without a second authorization model.

### Costs

- additional tables, constraints, and integration tests;
- a separate outbox worker and secret-envelope lifecycle;
- replacement of the generated sign-in and registration flow;
- environment-specific Data Protection and secret management;
- a further roadmap for Google, complete password recovery, and broader operational controls.
- encrypted TOTP material, recovery-code lifecycle, and Platform-specific operational projections/tests.

## Acceptance gate

This ADR and its associated specification must move to `Accepted` through an explicit human decision before implementing tasks that replace the current sign-in flow.
