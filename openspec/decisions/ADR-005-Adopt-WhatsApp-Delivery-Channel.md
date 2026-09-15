# ADR-005: Adopt WhatsApp as a Second Delivery Channel

**Status:** Proposed  
**Date:** 2026-08-31

## Context

The product needs a WhatsApp assistant in two operating models that share one audience: people the platform can identify. In the first, the platform owns one WhatsApp Business Account and phone number and serves two kinds of interlocutor — members of client organizations acting for their company, and individuals acting on their own `Personal` tenant, some of whom arrive with no account at all. In the second, a client organization connects its own WhatsApp Business Account through Meta Embedded Signup and serves only its own members, on its own number and brand. The second model is the same internal assistant on a different phone; it is not a channel toward that organization's customers.

[ADR-004](ADR-004-Adopt-Multitenant-Identity-Access.md) resolves the active tenant exclusively from `UserSession.ActiveTenantId` after validating a protected cookie (IA-REQ-006). An inbound WhatsApp message carries no cookie, no session, and no antiforgery pair. It carries a phone number, which WhatsApp verifies as controlling that account but which does not identify a person, survives SIM swap, and does not change when an employee leaves an organization.

Meta's On-Premises API is discontinued; Cloud API is the only supported transport. Unofficial client libraries that drive WhatsApp Web violate the Terms of Service and risk account termination, which is unacceptable for a shared B2B channel.

The repository currently contains the unmodified template. Identity-access exists only as an approved-pending specification, so this decision depends on it and cannot be implemented before it.

## Decision

1. Use the WhatsApp Cloud API as the only transport. Never drive WhatsApp Web, and never depend on an unofficial client library.
2. Provision the Platform-owned channel manually, once, through the Meta dashboard. Do not build Embedded Signup for a channel the platform owns.
3. Model every channel as one `WhatsAppChannel` with a `Mode` of `PlatformOwned` or `TenantOwned`, and route every inbound event by WABA identifier from the first slice, so Embedded Signup requires no change to the receiver.
4. Extend IA-REQ-006 explicitly rather than implicitly. A channel actor resolves its tenant only from a verified, server-owned `WhatsAppLink`. On a `TenantOwned` channel the link must additionally belong to that channel's own tenant, so channel and link must agree before anything proceeds. Message content, sender-supplied identifiers, and bot commands never establish tenancy, and every message revalidates tenant and membership state under IA-REQ-008.
5. Create a link only from an authenticated web session that already carries a validated tenant, and confirm it from the phone with a single-use code whose hash alone is persisted. A phone number identifies; it never authenticates.
6. Treat the webhook as public in HTTP terms and gate it with an HMAC-SHA256 signature computed over the exact raw request body. It carries the `IPublicRequest` marker under IA-REQ-011, and the signature is its real authorization.
7. Persist every inbound event before acknowledging, return `200` immediately, and process asynchronously. Deduplicate by Meta message identifier.
8. Send every outbound message through the ADR-004 transactional outbox, using the outbox message identifier as the provider idempotency key.
9. Store channel and module secrets only in encrypted envelopes following the `OutboxSecret` pattern, with the wrapping key held outside the database.
10. Compute channel permissions as the intersection of the membership's effective permissions and the channel allowlist. A channel never grants a permission the membership lacks.
11. Never enroll a number without opt-in evidence. Every active allowlist entry retains the inbound message that confirmed it.
12. Declare the audience on the channel, and make the audience decide the whole authorization story. `Members` and `Personal` resolve tenant and identity from a verified link and authorize by permission; `Contacts` resolves tenant from the channel, has no platform identity, and authorizes only by row scope. A `PlatformOwned` channel serves `Members` and `Personal`; a `TenantOwned` channel serves `Contacts` and nothing else. The two groups never mix on one channel.
13. Invert the enrollment order for self-service without inverting the authority. An inbound message from an unknown sender on a self-service channel creates a phone claim that carries no tenant, no identity, and no capability. Only a completed web registration — creating the identity, confirming its email, and creating its `Personal` tenant — converts that claim into an active link.

## Rationale

**Why not an unofficial library.** It violates Meta's Terms of Service and puts one shared number at risk of termination. For a channel every client depends on, that is an unacceptable single point of failure with no remedy.

**Why not treat the phone number as authentication.** SIM swap, shared devices, and staff turnover all break the assumption. Deriving the link from an already-authenticated web session means the bot inherits an identity and tenant that ADR-004 already validated, instead of establishing a second, weaker authority.

**Why not resolve the tenant from message content.** Accepting `"organización: Acme"` from the message body reintroduces exactly the client-supplied tenant context IA-REQ-006 forbids. The prohibition is not about HTTP headers; it is about who asserts tenancy.

**Why not a WABA per client under our own portfolio.** The on-behalf-of arrangement makes the platform the owner, which transfers policy liability and Meta billing to the platform and makes a client's departure a migration instead of a revocation.

**Why a client-owned channel is customer-facing, and only that.** An organization connecting its own number wants an assistant for its own customers, under its own brand. That interlocutor genuinely has no platform identity, and pretending otherwise — issuing them a tenant, a membership, or a permission — would manufacture identity for people who never asked for an account. The honest model is a third audience with no permissions at all, whose entire authority is the rows their own phone number owns inside one fixed tenant. Keeping `Contacts` on its own channels, and off the platform channel, means the permission-based path and the row-scoped path never meet inside one request.

**Why the invoicing boundary is structural rather than commercial.** The platform's assistant issues fiscal documents; a client's customer-facing assistant must never be able to. Expressing that as a per-capability audience declaration, checked in the resolution chain and asserted by an architecture test, makes the boundary impossible to cross by misconfiguration. Expressing it as a policy or a panel setting would make it one careless toggle away from a stranger issuing an invoice against someone else's CUIT.

**Why self-service enrollment does not weaken the link rule.** The rule that matters is that a link never derives authority from a phone number. Holding an unverified claim until a web registration completes preserves that rule while letting the assistant do the data collection, so an individual can start in WhatsApp without the platform gaining a second, weaker identity mechanism.

**Why not process inside the webhook.** Meta retries on slow or failed acknowledgement. Synchronous processing turns every timeout into a duplicate, and for an action channel a duplicate is a second real-world effect.

**Why not create the identity from the phone number alone.** It is the shortest path for an individual and it is what a WhatsApp-only product can afford, but it contradicts IA-REQ-001 and IA-REQ-020 — one confirmed email identifies an identity, and only a confirmed identity obtains a session. A product with a web application would then carry two authentication mechanisms with different guarantees, and the weaker one would own fiscal capabilities.

**Why one channel model instead of two implementations.** The two operating models differ only in credential provenance and in which side of the conversation is a known identity. Sharing the receiver, the inbox, the outbox, and the audit trail while varying tenant resolution keeps one code path to secure and to test.

## Consequences

### Easier

- one receiver, one signature check, and one inbox serve both operating models;
- Embedded Signup becomes an additive slice rather than a rewrite;
- the bot inherits the ADR-004 permission evaluator with no second authorization model;
- opt-in evidence and audit fall out of the message log instead of a separate mechanism;
- a client-owned channel isolates message quality and policy risk to that client.

### Harder

- a second tenant-resolution path must be specified, tested, and defended against drift;
- inbound processing becomes asynchronous, adding an inbox, a worker, and replay tests;
- the platform stores third-party channel credentials and must manage their lifecycle;
- Embedded Signup requires business verification, Tech Provider status, and App Review before it can ship;
- a shared `PlatformOwned` number couples message quality across all client organizations.

## Acceptance gate

This decision depends on [ADR-004](ADR-004-Adopt-Multitenant-Identity-Access.md) reaching `Accepted` and on the identity-access first increment providing `Tenant`, `TenantMembership`, the permission catalog, and the outbox. It must move to `Accepted` through an explicit human decision before any WhatsApp code is written.
