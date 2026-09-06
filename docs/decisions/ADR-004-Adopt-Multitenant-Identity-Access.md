# ADR-004: Adopt Multitenant Identity Access with React and PostgreSQL

**Status:** Proposed  
**Date:** 2026-08-31

**Continuation decisions 18-24:** Decision 18 (C1) accepted 2026-09-06 and implemented by Task 18. Amendments A1-A5
were folded into decisions 20, 21, 23 and 24 and their SPEC entries on 2026-09-06; folding them in changes what is
being asked, not whether it was answered. Decisions 19-24
(C2-C7) remain proposed, five of them with required amendments recorded in the acceptance gate below.

## Context

Before Tasks 1 and 2, the generated template paired Angular/React/API-only clients with default ASP.NET Core Identity endpoints, general roles/claims, a minimal React client, open CORS, Development database recreation, and a fixed administrator account. Tasks 1 and 2 now make PostgreSQL the only database provider, apply `BaselinePostgreSql` with `MigrateAsync`, and remove destructive initialization and default credential seeding. The remaining reference sign-in still uses the default Identity endpoints, general roles/claims, and minimal React client that this decision replaces incrementally.

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

## Proposed continuation decisions (Task 17 — not accepted)

Decisions 1–17 above are the first increment's and are unchanged. Decisions 18–24 below are the continuation's
decision register, written so that Tasks 18–28 can be implemented without another product-discovery round. They
are **proposed**. Writing them here does not accept them, and no dependent task is `Ready` until the human
decision recorded in the acceptance gate names them.

Each decision states what it changes for a person using the product, and which of the three separate gates in
[SPEC §2.5](../features/identity-access/SPEC.md#25-continuation-scope-and-its-three-separate-gates) it belongs to.
A decision that is declined or amended blocks only the tasks that consume it.

18. **(Accepted 2026-09-06.)** Require proof of the identity a reservation will belong to before creating any exclusive durable
    claim.  Anonymous registration records only a bounded, expiring intent and one outbox message to that address: no
    identity, tenant, organization profile, membership, role or CUIT claim, and nothing another caller can read or be
    refused by. Exactly two proofs authorize creation: a validated persisted session, or a single-use token delivered
    to that address and spent by the request — the mechanism the existing invitation and confirmation paths use. Under
    proof the whole graph commits in one transaction, finalization taking the advisory locks initiation already takes
    with the unique indexes as backstops. This narrows IA-REQ-003/004/005 rather than adding to them: the anonymous
    phase now writes an intent, a message and a tenantless audit event even for an occupied CUIT, where the approved
    third bullet promised none. Rejected: keeping pre-confirmation aggregate creation and hiding only the status
    difference, which still leaks through the durable CUIT claim and the attacker's context.

19. **(Proposed.)** Let an identity's sessions coexist under a serialized cap instead of revoking them all at sign-in:
    at most five live sessions, the sign-in that would exceed that revoking the oldest by creation time then
    identifier and committing the new one in the same transaction. Serialize every request that issues, rotates or
    mass-revokes a session behind a transaction-scoped advisory lock keyed by that identity, in the two-argument lock
    space so it cannot collide with the registration and catalogue keys; order eviction in PostgreSQL only, since .NET
    `Guid` and PostgreSQL `uuid` ordering differ; and answer an exhausted wait with `429`, so no status is reachable
    only after a valid credential. Give the identity its own list and revocation under the existing self-service
    capability, addressing rows by an unguessable reference that is never the identifier the cookie carries and
    publishing only a coarse device label and timestamps. The cap is held by serialized recomputation rather than a
    constraint, liveness depending on elapsed time. Rejected: counting without serializing, which is a lost update,
    and refusing the sign-in that would exceed the cap.

20. **(Proposed.)** Make a person's own profile a self-service resource of the authenticated identity and nothing
    else: keyed by the identity, reached only through requests carrying no owner parameter, read and edited through
    two proposed application-scoped permissions. Editing covers the full name and display name; the email, ownership
    and the document lie outside it, and a request naming any other member is refused whole under a stable code naming
    that member. Store the documentary identity as an authenticated-encryption ciphertext on the key ring
    `OutboxSecret` and the Platform TOTP secret already use, with uniqueness on a keyed, versioned fingerprint child
    table, so a purge deletes those rows in the transaction that clears the ciphertext and a rotation carries the old
    and new value at once. The plaintext leaves the protector at exactly two named seams, the owner's own read and an
    offline rotation adapter with no route and no permission, and no Organization or Platform route resolves a profile
    or document. **Amended 2026-09-06 (A1):** correction is defined rather than deferred, as a two-party verified
    process — the owner opens a dispute over their own document and can never write one; a Platform operator resolves
    it under a distinct permission, a recent second factor and an external case reference, and may not resolve a
    dispute whose subject is themselves. There is no route that writes a document without a stored dispute, which is
    what "no support bypass" means. A duplicate document at Personal creation receives the same refusal every other
    refused claim receives, under a small per-identity budget, and the residual it still leaks is named and left to
    the real-personal-data gate rather than hidden. Rejected: a partial index on one fingerprint column, which cannot
    hold two retained key versions; free editing of a recorded document; and an operator route that needs no dispute.

21. **(Proposed.)** Make "prove it is still you" a single-use server-side artifact rather than a re-typed password or
    a trusted cookie: a recent identity proof binds one identity, one session, one action and the identity's security
    version, is issued only by the current password or a fresh challenge to a linked provider, never travels to the
    client, and is consumed by a conditional update that re-checks the bound session; a provider-only identity
    therefore needs its own proof start route, or the rule would demand a credential it cannot have. Every credential
    or authenticator change increments a Domain-owned security version that invalidates all outstanding proofs,
    Identity's `SecurityStamp` still rotating but not being the authority. **Amended 2026-09-06 (A3):** that
    provider-only proof route is not enough for someone who cannot sign in at all, so a fourth purpose, `Recovery`,
    binds to a pending reactivation ticket instead of a session, issues nothing, and is consumed by that ticket
    alone; and one recovery contract is stated once for both this decision and decision 23 — the neutral
    answer never varies, what is enqueued is fixed per account state, and a completed reset revokes sessions and
    changes a credential without lifting an administrative suspension or clearing a second factor.
    Adopt external providers through the
    framework's OpenID Connect middleware with explicit linking only, and exempt the callback from two rules rather
    than one — exact-origin plus antiforgery, which a top-level cross-site GET cannot carry, and the
    no-token-in-a-query-string rule, which the authorization code breaks — as an allowlist of one path, narrow because
    that callback mutates nothing. **Amended 2026-09-06 (A2):** "explicit linking only" is now written as two
    separate rules rather than one refusal. Refuse the automatic path — an unattended sign-in that would adopt a
    local identity because the addresses match — and refuse any subject another identity already owns. Allow the
    explicit path: a confirmed, authenticated person linking their own provider account with consent and a live
    proof, which is the ordinary case and stays allowed precisely when the provider's verified address is their own.
    A `Link` is still refused when the provider's address belongs to somebody else's local identity. Rejected:
    treating an unexpired cookie as proof; a current-password field a provider-only identity can never fill; and one
    conflict rule covering both linking paths, which refused the person the feature exists for.

22. **(Proposed.)** Delegate `Organization` administration through the existing evaluator with one grant-time ceiling
    and one effective-administrator floor. An actor may cause an identity to hold only permissions the actor itself
    effectively holds in that tenant at commit time and that the catalogue permits for that tenant type — on the added
    codes when a role's set changes, on the whole set when a role is assigned or offered, never at acceptance. Accept
    the consequence: a code nobody holds can never be granted, so the system `Owner` role must be provisioned with
    every Organization-allowed code and backfilled, and every future catalogue addition carries that step — until
    which this decision is unsatisfiable, today's provisioner granting that role nothing. One administrator means one
    distinct identity whose recomputed effective permissions contain the floor, recomputed after an explicit flush
    inside the mutating transaction. Hold ownership as one additive tenant-level reference with a composite foreign
    key, transferred only by the current owner under a permission distinct from tenant management plus a recent proof,
    and refuse to suspend or revoke the owner's own membership. Rejected: counting role assignment rows, which
    miscounts retired roles, suspended memberships and one person holding two admin roles.

23. **(Proposed.)** Make identity and membership lifecycle finite, and give every non-terminal disabled state a
    reactivation path that does not require the session it denies. Account state becomes a closed set that renames the
    existing `Suspended` member rather than adding to it — safe only while that enum has no persisted column and no
    reader. **Amended 2026-09-06 (A4):** a legal hold is not part of this decision at all. It is decision 24's record,
    with decision 24's actor, permission and endpoints, and its whole effect is to stop erasure; it is never an
    account state, never refuses a sign-in and never blocks a reactivation, so retaining data and blocking access stay
    separate concerns. Only `Active` satisfies IA-REQ-020, which makes any "sign in and then
    reactivate" design circular; a person recovers instead through the public, neutral, rate-limited shape already
    built for bootstrap and password recovery, combined with a current authenticator, because mailbox control alone
    already resets a password. **Amended 2026-09-06 (A3):** that current authenticator is a password or decision 21's
    `Recovery` purpose, which is what an identity that only ever signed in through a provider uses; the three disabled
    cases are named once — self-deactivation returns by this public route, administrative suspension is lifted only by
    an operator, and a closed account is a tombstone with no way back — and no credential recovery lifts a suspension
    or skips a second factor. Reactivation issues no session and restores the pre-disable state, and no transition
    out of `Active` may strand a tenant without an effective administrator. Make restore admission fail closed against
    evidence the backup cannot contain: closed on every process start until an operator-controlled record held outside
    the restored database, verified against an operator-held key, advances the epoch — stored only at the release
    commit, so recovery does not demand a fresh signature after every reboot. Rejected: an in-database admission flag,
    which the restore rewrites; a startup environment variable, which a later restart silently honours; and the
    reference's cloud WORM ledger, unadopted rather than waived.

24. **(Proposed.)** Hold personal data under one deployment-wide mode, keep real personal data behind a separate human
    gate, and record an erasure as evidence rather than an audit payload. The mode — never a caller, never a request
    field — is `Synthetic` unless explicitly `Real`, stamps a server-derived classification on every stored profile
    and document row, and is checked by a startup readiness probe that refuses `Real` without an approved policy
    identity and refuses `Synthetic` over real-classified rows, while deliberately letting a `Real` deployment start
    over leftover synthetic rows, which stay ineligible, since refusing there would make the transition these gates
    exist to support unreachable. Retention is configuration and never code, and a missing policy or a closed
    restore-admission gate produces no destructive action at all. A destructive write commits with an append-only
    erasure record carrying only opaque identifiers, category, policy version and a row count, because audit admits
    three safe keys and widening them would dismantle the guard keeping PII out of audit. A purge erases ciphertext
    and keyed fingerprint alike, so a purged document number is reclaimable with no retained link. **Amended
    2026-09-06 (A4):** the legal hold this decision owns stops erasure and nothing else — it does not suspend an
    account, refuse a sign-in or block a reactivation, and no lifecycle or authorization branch reads it. Move every
    guessing budget onto shared PostgreSQL state through one port and one conditional upsert, failing closed when the
    store is unreachable. **Amended 2026-09-06 (A5):** failing closed keeps the refusal but changes the answer — an
    unreachable budget store returns `503` `service_unavailable` with `Retry-After`, and `429` `rate_limit_exceeded`
    is left to mean what it says. Rejected: erasure evidence in the audit endpoint, which `AuditEvent` cannot carry;
    a subject-indexed erasure directory; and calling an outage "too many attempts".

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

Decisions 18–24 and [SPEC §14](../features/identity-access/SPEC.md#14-task-17-decision-package-proposed-not-approved)
carry their own gate, and it is a separate one. They are the Task 17 decision package. A decision recorded against
them must name the date, the decisions accepted as written, the decisions amended and how, and the decisions
declined; an entry that is declined or amended blocks only the tasks listed as its consumers. Approving them
authorizes implementation against synthetic data alone. Two prerequisites stay outside that approval and are not
granted by it: the use of real personal data, which needs the responsible human or legal owner to approve purpose,
field scope, access, retention periods, legal holds, deletion evidence and backup/restore treatment (IA-REQ-056);
and production deployment together with full reference compliance, which needs the restore admission authority and
its external record (IA-REQ-055), the per-environment key and certificate ownership, and the recorded abuse budgets.

### Decision record — 2026-09-06

**Accepted: decision 18 (C1) only.** Proposed IA-REQ-048 and the amended wordings of IA-REQ-003, IA-REQ-004 and
IA-REQ-005 in [SPEC §14.1](../features/identity-access/SPEC.md#141-c1--registration-reservation-no-exclusive-durable-claim-before-proved-control-of-the-address)
are accepted as written and now live in [SPEC §4](../features/identity-access/SPEC.md#4-normative-requirements); §14.1
is kept as the decision record that produced them. Task 18 was the only task this decision made `Ready`, and it was
implemented and verified the same day. Task 17 is **not** complete and decisions 19–24 are **not** accepted; nothing
else in §14 or §15 is approved by this record.

**Amendments required before decisions 19–24 are put forward again** — recorded 2026-09-06 and folded into the
proposals the same day, as the table after them records. Each is a change to the proposal, not a question about it,
and each block arrives with its contract already reconciled: a contradiction left as a note for the implementer is
not a delivered contract.

| # | Entry | Required amendment |
|---|---|---|
| A1 | C3 (§14.3) | Define verified document correction and dispute **before** real data is enabled, including the answer a person receives when the document they submit is already recorded. Neither free editing nor a support bypass is acceptable. This replaces §14.3's "ship no correction route" recommendation and closes its `**Gap:**` on the duplicate-document answer. |
| A2 | C4 (§14.4) | Separate automatic linking from explicit linking. BR-ID-005/006 forbid linking a provider identity **automatically** on a matching email; they do not forbid an authenticated person linking their own account. The `Login` or `Link` row that refuses `409 external_login_conflict` for "an unlinked subject whose email already belongs to a local identity" must not refuse a `Link` by a confirmed, authenticated identity that supplies consent and a live proof and whose own address is the match. |
| A3 | C4 and C6 (§14.4, §14.6) | Reconcile recovery across SPEC and ADR: one route name, one answer for a deactivated or suspended account, and one answer for a forgotten password on such an account. Define how an identity that signs in only through a provider returns, without requiring a session it cannot obtain — §14.6 currently records that path as a disagreement. Recovering a credential must never lift an administrative suspension by itself and must never skip a second factor that would otherwise apply. |
| A4 | C6 and C7 (§14.6, §14.7) | Separate retaining data from blocking access. A legal hold must stop erasure; it must not suspend an account or block reactivation as a side effect. §14.6's `LegalHoldAt` row, which blocks every reactivation path and names no actor that sets or clears it, is amended by this. |
| A5 | C7 (§14.7) | Keep the safe refusal when the shared abuse-control store is unavailable, and change the answer: `503` `service_unavailable` with `Retry-After`, not `429` `rate_limit_exceeded`. A person meeting an outage is told it is an outage. |

**Folded in — 2026-09-06.** Each amendment was written into the entry it names, and each contradiction those entries
carried was decided rather than passed on. What changed, and where to read it:

| # | Landed in | What it now says |
|---|---|---|
| A1 | SPEC §14.3, IA-REQ-058; decision 20 | The owner opens a dispute; a Platform operator resolves it under a distinct permission, a second factor and an external case reference, never for themselves. A duplicate document gets the same refusal every refused `Personal` claim gets, under a 3-per-day budget, with the remaining leak named and left to the real-data gate. |
| A2 | SPEC §14.4 callback table; decision 21 | Automatic linking is refused, explicit linking is allowed — including, and especially, when the provider's verified address is the caller's own. A `Link` is still refused when that address belongs to another local identity. |
| A3 | SPEC §14.4 and §14.6; decisions 21 and 23 | One `Recovery` OIDC purpose bound to a ticket rather than a session, so a provider-only identity can return. One neutral answer, one enqueue rule per account state, stated once. A reset never lifts a suspension and never clears a second factor. |
| A4 | SPEC §14.6 and §14.7; decisions 23 and 24 | `LegalHoldAt` is gone from the lifecycle table. A hold is C7's record with C7's endpoints, it stops erasure, and it changes nothing about access. |
| A5 | SPEC §14.7, IA-REQ-057; decision 24 | An unreachable budget store still refuses every attempt and now answers `503` `service_unavailable` with `Retry-After`. |

Two contradictions outside the amendment list were decided the same way rather than left as notes: C4's `{handle}`
session route is withdrawn in favour of C2's `{sessionRef}` with C4's proof requirement, and C6's
`tenant_last_administrator` spelling is withdrawn in favour of C5's `last_administrator_required` with C6's
`expectedStatus` precondition.

Nothing above authorizes implementation of C2–C7, and folding an amendment in is not accepting the entry that
carries it: decisions 19–24 are still proposed and still need one answer each. Real personal data and production
deployment remain the two separate gates named at the top of this section, and decision 18 does not touch either.
