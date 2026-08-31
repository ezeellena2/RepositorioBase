# ADR-004: Adopt Multitenant Identity Access with React and PostgreSQL

**Status:** Proposed  
**Date:** 2026-08-31

## Context

The repository is currently the source of a Clean Architecture template with Angular/React and SQLite/PostgreSQL/SQL Server options. Its reference sign-in uses default ASP.NET Core Identity endpoints, general roles/claims, a minimal React client, open CORS, database recreation in Development, and a default administrator password.

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

This decision adopts the reference standard's business semantics, not its repository workflow or delivery methodology.

## Rejected alternatives

- **Keep Identity roles as the primary authorization model:** this does not isolate permissions by tenant and makes business access depend on potentially stale claims.
- **Store JWTs in React local storage:** this increases XSS exposure and provides no benefit for a same-origin SPA.
- **Create one user per organization:** this duplicates identity, complicates invitations, and contradicts context switching within one session.
- **Retain multiple database providers:** this weakens the PostgreSQL-specific guarantees required for partial indexes, composite foreign keys, and baseline concurrency behavior.
- **Send email inside the HTTP transaction:** this cannot make a business change atomic with an external provider.
- **Wrap every HTTP response in `{ success, data, error }`:** this hides HTTP semantics, duplicates Problem Details, weakens generated contracts, and leaks an internal control-flow model across Web and React.

## Consequences

### Positive

- verifiable tenant-scoped isolation;
- revocable sessions and permissions without waiting for claims to expire;
- one user across multiple organizations;
- recoverable invitation and email infrastructure;
- business rules testable without React, cookies, or an email provider.

### Costs

- additional tables, constraints, and integration tests;
- a separate outbox worker and secret-envelope lifecycle;
- replacement of the generated sign-in and registration flow;
- environment-specific Data Protection and secret management;
- a further roadmap for 2FA, Google, Platform, and complete recovery.

## Acceptance gate

This ADR and its associated specification must move to `Accepted` through an explicit human decision before implementing tasks that replace the current sign-in flow.
