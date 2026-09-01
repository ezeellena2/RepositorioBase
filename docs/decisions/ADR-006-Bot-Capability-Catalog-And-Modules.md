# ADR-006: Bot Capability Catalog and Pluggable Modules

**Status:** Proposed  
**Date:** 2026-08-31

## Context

[ADR-005](ADR-005-Adopt-WhatsApp-Delivery-Channel.md) establishes a WhatsApp channel that resolves an identity and a tenant. It does not decide what the assistant may do once it has them.

The assistant must interpret free-form natural language, which means a language model participates in choosing what runs. It must also grow: the first integration is electronic invoicing against ARCA, but payment processors, logistics providers, and internal reporting are equally plausible, and each brings its own per-organization credentials, its own sandbox and production environments, and its own failure behaviour.

Two properties of these integrations drive this decision. First, some effects are irreversible: an authorised electronic invoice cannot be deleted, only offset by another permanent fiscal document, and its numbering admits no gaps. Second, external services fail in a way that hides the outcome — a timed-out request may or may not have taken effect, and a blind retry can duplicate a real-world action.

The channel itself is lower assurance than the web session ADR-004 specifies: possession of a phone is the only proof of identity, and there is no antiforgery pair or MFA step-up.

## Decision

1. Expose a closed catalog of capabilities. The model selects a capability and supplies parameters; it never authors a query, a command, or an identifier of its own.
2. Split capabilities by kind. A `Query` may be declarative — a parameterised statement configurable from the operations panel. An `Action` must be code: a request type, reviewed and tested. Enforce the split with a database constraint, so an action can never be authored as free text in a form.
3. Project capabilities onto existing Application requests. The bot is a delivery mechanism beside HTTP, not a parallel application; a capability declares an existing request and inherits its validation and its permission through the same `AuthorizationBehaviour`.
4. Execute every action in two phases. The first resolves entities, validates, computes every derived value in code, and records a durable run carrying the summary that was rendered for the human. The second requires an explicit confirmation referencing that run. A language model never sits between the confirmation and the effect.
5. Render confirmation summaries deterministically from persisted data. A model never states an amount, a total, a tax, a document type, or an identifier that the human is asked to approve.
6. Default irreversible capabilities to confirmation in an authenticated web session, and allow an organization to relax or tighten confirmation per capability without code changes.
7. Require reconciliation from every external capability that writes. Reconciliation answers "did this already happen" against the external system and is mandatory in the contract for any non-idempotent external write, not optional. A read-only external capability declares no reconciliation, because it has nothing to reconcile; that asymmetry is what makes a read-only integration a cheap and safe way to prove a module's credentials, ticket lifecycle, and health before any write capability is enabled.
8. Never retry an unknown outcome. A request whose result could not be observed transitions to reconciliation; only an outcome positively known to be retryable is retried.
9. Model module credentials generically: a module declares a credential schema, an organization supplies values, and the platform stores them encrypted, per environment, with an expiry and a health state.
10. Compute capability availability per request through an explicit resolution chain. A module whose credentials are missing, invalid, or expired removes its capabilities from the catalog the model is offered, so an unavailable capability cannot be selected and then fail.
11. Treat a WhatsApp Flow as one rendering of a capability's parameter schema, not as a separate feature. The flow token is the action run identifier, and business logic never lives in published flow definitions.
12. Make audience exposure a property of the capability. Every capability declares which audiences may reach it, the resolution chain intersects that declaration with the audience of the current message, and no capability that issues a fiscal document, moves money, or reads organization-wide data may declare the customer-facing audience. This is asserted by an architecture test, not by configuration.

## Rationale

**Why a closed catalog instead of generated queries.** A model that writes statements against a multi-tenant database must be trusted to scope every one of them to the validated tenant. A model that picks from a catalog cannot address another tenant's data, because the tenant is bound by the server after selection. The catalog also makes each capability independently testable and each denial explainable.

**Why actions must be code while queries may be configuration.** Adding a report should not require a deployment; adding an effect should require review. The distinction is enforceable and cheap, and it prevents the operations panel from becoming an unaudited path to arbitrary writes.

**Why two phases rather than a confirmation prompt in the model's own turn.** A single-turn confirmation leaves the model as the last component before the effect, and its restatement of the parameters is generated text. Persisting the run and rendering the summary from that run makes the approved content deterministic and produces evidence of exactly what the human saw.

**Why reconciliation is mandatory rather than best-effort.** Every external integration eventually times out. Making reconciliation part of the interface forces the author to solve the ambiguity when the module is written, rather than during the incident that duplicates a customer's invoice. Modules that genuinely cannot answer the question are modules that must not perform irreversible actions.

**Why availability is computed rather than assumed.** Offering a capability whose credentials expired produces a failure the user cannot act on and a model that keeps retrying a dead path. Removing it from the catalog turns a runtime error into an absence.

**Rejected: generated SQL constrained by prompt instructions.** Instructions are not a security boundary, and the failure is silent cross-tenant disclosure.

**Rejected: a bot-specific service layer.** Duplicating business logic for the channel creates a second authorization path with its own drift, which is the outcome ADR-004 rejects for Platform administration and rejects here for the same reason.

**Rejected: letting the model compute monetary or fiscal values.** Rounding and classification errors become permanent documents. Extraction of intent is a model task; arithmetic and classification are not.

**Rejected: per-module bespoke credential storage.** It multiplies the number of places secrets live and the number of encryption decisions to audit.

## Consequences

### Easier

- one integration contract, so the second external module costs a fraction of the first;
- cross-tenant disclosure through generated statements is structurally impossible;
- new reports ship as configuration while new effects keep code review;
- capability availability, limits, and confirmation strength are per-organization settings;
- duplicate external effects have one designed remedy instead of per-module improvisation.

### Harder

- every action carries a durable run, a rendered summary, and an expiry to manage;
- module authors must implement reconciliation, which is the hardest part of an integration;
- the catalog and its per-organization overrides add tables, resolution logic, and tests;
- confirmation adds a round trip, and a web confirmation adds a context switch for the user;
- capability descriptions become a maintained interface, since routing quality depends on them.

## Acceptance gate

This decision depends on [ADR-005](ADR-005-Adopt-WhatsApp-Delivery-Channel.md). It must move to `Accepted` through an explicit human decision before any capability, module, or action-execution code is written. No irreversible module may ship before a reversible one has exercised the two-phase flow in production.
