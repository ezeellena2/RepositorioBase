# WhatsApp Bot — Tasks

**Status:** Proposed.

## Review workload forecast

Decision needed before apply: Yes — ADR-005 and ADR-006 both carry acceptance gates.  
Chained PRs recommended: Yes.  
Chain strategy: size-exception.  
400-line budget risk: High.

Suggested units: channel persistence and panel; receiver and inbox; linking; catalog and routing; actions and execution; modules and Flows. Each unit is a mandatory commit, verification, and rollback boundary.

## External dependency

The whole package is blocked on the identity-access first increment delivering `Tenant`, `TenantMembership`, the permission catalog, the authorization evaluator, and the outbox — that is, IA-008 complete. No WA task may reach `Ready` before then.

## Canonical requirement ownership

- WA-002 owns WA-REQ-001..006 and WA-INV-004.
- WA-003 owns WA-REQ-014..020 and WA-INV-002.
- WA-004 owns WA-REQ-007..013, WA-INV-001, and the opt-in evidence chain.
- WA-006 owns WA-REQ-024..032 and WA-INV-006.
- WA-007 owns WA-REQ-033..039, WA-INV-003, and WA-INV-008.
- WA-005 owns WA-REQ-021..023. WA-009 provides acceptance evidence only and is never a normative owner.

## Tracked work

| ID | Task | Requirements | Status | Depends on | Observable result |
|---|---|---|---|---|---|
| WA-001 | Approve ADR-005, ADR-006, and SPEC | — | Review | IA-001 | human approval |
| WA-002 | Channel persistence, encrypted credentials, setup checklist, alerting, Platform panel | WA-REQ-001..006, 044, 055, 056 | Blocked | WA-001, IA-008 | configured channel; secrets never readable; connectivity check; provider-derived checklist that reports an unsubscribed application; degradation raises one alert until cleared |
| WA-003 | Webhook receiver, signature, inbox, deduplication | WA-REQ-014..020, 047 | Blocked | WA-002 | signed events persisted and acknowledged; replay produces one effect |
| WA-004 | Linking, verification, allowlist, revalidation | WA-REQ-007..013, 045 | Blocked | WA-003 | web-initiated link confirmed from the phone; revocation immediate |
| WA-005 | Outbound sending, service window, delivery evidence | WA-REQ-021..023 | Blocked | WA-004 | outbox delivery with idempotency key; window enforced before send |
| WA-006 | Capability catalog, resolution chain, model routing | WA-REQ-024..032, 046 | Blocked | WA-005 | closed catalog; tenant-filtered queries; absent capabilities not offered |
| WA-007 | Action runs, two-phase confirmation, idempotency | WA-REQ-033..035, 038, 039 | Blocked | WA-006 | rendered summary approved; one effect per confirmation |
| WA-008 | Module contract, credentials, reconciliation | WA-REQ-031, 036, 037 | Blocked | WA-007 | reversible external module; `Unknown` reconciles, never retries |
| WA-009 | React screens and E2E acceptance evidence | evidence only; delivers the 12 first-increment screens of [SCREENS.md](SCREENS.md) | Blocked | WA-008 | linking, confirmation, and panel journeys verified, each screen handling its six shared states |
| WA-010 | WhatsApp Flows and encrypted data endpoint | WA-REQ-040..043 | Proposed | WA-009 | published Flow per channel; drift detected; no business rules in the definition |
| WA-014 | Self-service enrollment and `Personal` tenants on the channel | WA-REQ-048..051 | Proposed | WA-010, IA-010, PII policy | phone claim with no authority; link activated only after web registration; personal and organization scopes isolated |
| WA-011 | Embedded Signup, `TenantOwned` channels, and the `Contacts` audience | roadmap | Proposed | WA-010, Tech Provider approval, contact-catalog design | a client-connected channel serving that organization's own customers, row-scoped, with no fiscal capability reachable |
| WA-012a | ARCA read-only module: voucher verification and taxpayer lookup | roadmap | Proposed | WA-008, WA-013 for media intake | certificate, ticket cache, environment, and health proven with zero external side effects |
| WA-012b | ARCA write capabilities: issue and credit note | roadmap | Proposed | WA-012a in production, legal review | irreversible capability with proven reconciliation |
| WA-013 | Conversational components and media inbound | roadmap | Proposed | WA-009 | ice breakers, commands, audio and image intake |

## Sequencing constraint

The gate is per capability, not per module. WA-012b MUST NOT start before WA-012a has run in production long enough to prove the certificate lifecycle, the access-ticket cache, the environment split, and the health check. ADR-006 makes this a gate, not a preference: the first irreversible integration must not also be the first exercise of the machinery that protects it.

Splitting ARCA this way is deliberate. Voucher verification is read-only — it calls the same external system with the same credentials and has no side effect to reconcile — so it de-risks the expensive half of the integration before a single fiscal document can be issued.

## Module roadmap

Indicative, not committed. Recorded so the sequencing argument survives, and so nobody has to re-derive why invoicing is not first.

| Module | What it is | External | Reversible | Depends on |
|---|---|---|---|---|
| `core` | queries over the product's own data | no | reads only | WA-006 |
| `arca-read` | voucher verification, taxpayer lookup | yes | reads only | WA-008, media intake for photographed vouchers |
| `arca-issue` | invoices and credit notes | yes | **no** | `arca-read` in production |
| `banks` | statement import, matching, reconciliation rules | yes | reversible | its own domain, largest of these |
| `payments` | payment links, collection status | yes | link reversible, collection not | decision 8 if it triggers issuing |
| `scheduling` | agenda, appointments | no | reversible | `Contacts` audience for customer-facing use |

Two properties of this table matter more than its contents.

**The channel does not grow with the modules.** The fifth module costs the same in channel terms as the first: some rows, an attribute per capability, and a credential schema. What grows is the product — each module is a full vertical slice with its own domain, migration, screens, and tests, and the assistant is a surface onto it. Reaching a feature from WhatsApp is a small fraction of building that feature.

**Order is chosen by consequence, not by value.** `arca-read` precedes `arca-issue` even though issuing is worth more, because it exercises the same credentials, the same access-ticket lifecycle, and the same health path with **no external side effect at all**. The first module through the machinery should not be the one that cannot be undone.

## Executable-task contract

Before `Ready`, record actor, preconditions, request marker and permission, tenant scope, files and migration, compile-safe RED, behavioral RED, GREEN, REFACTOR, denial and concurrency and replay tests, audit and outbox behavior, and evidence. Channel tasks additionally prove signature rejection against a captured raw body, secret non-disclosure across every projection, and phone normalization. Action tasks additionally prove expiry, foreign-tenant refusal, double confirmation, and reconciliation on `Unknown`. WA-001 approves documentation only.
