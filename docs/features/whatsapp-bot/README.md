# WhatsApp Bot

Proposed planning package for a WhatsApp delivery channel over the multitenant identity-access foundation: a platform-owned assistant that identifies members by verified phone link, routes natural language to a closed capability catalog, and executes effects only through explicit two-phase confirmation.

The package adds a channel and a catalog. It adds no business rules: every capability projects onto an Application request that is already authorized.

## Artifacts

- [SPEC.md](SPEC.md): requirements, data model, contracts, and acceptance criteria.
- [TASKS.md](TASKS.md): slices, dependencies, and status.
- [TRACEABILITY.md](TRACEABILITY.md): requirement → task → test → evidence.
- [ADR-005](../../decisions/ADR-005-Adopt-WhatsApp-Delivery-Channel.md): the channel decision.
- [ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md): the capability and module decision.
- [SCREENS.md](SCREENS.md): the screen inventory, its states, and what each screen serves.
- [ONBOARDING.md](ONBOARDING.md): the operational runbook for provisioning a channel in Meta.
- [MODULES.md](MODULES.md): what a module must bring, and what it never has to touch.

## Status

The documentation proposal is under human review. It does not authorize implementation and claims no runtime behavior. It depends on the [identity-access](../identity-access/README.md) first increment for `Tenant`, `TenantMembership`, the permission catalog, and the outbox; no task here may start before that dependency is `Accepted` and delivered.

## Two operating models

| | Platform-owned channel | Organization-owned channel |
|---|---|---|
| Provisioning | manual, once, in the Meta dashboard | Meta Embedded Signup, per organization |
| Audiences | `Members` + `Personal` | `Contacts` |
| Who writes | members of any client organization, **and** individuals acting for themselves | that organization's own customers |
| Tenant comes from | the verified `WhatsAppLink` | the channel |
| Authorization | membership permissions | row scope only — no permissions, no platform identity |
| Invoicing reachable | **yes** | **never**, by capability declaration |
| Increment | first | roadmap, and a separate product surface |

The two groups never mix on one channel. The platform's assistant is the one that issues fiscal documents, for companies and for individuals alike; an organization's own assistant serves its customers and is structurally unable to invoice, because invoicing declares only the `Members` and `Personal` audiences.

Both share one receiver, one inbox, one outbox, and one audit trail, which is why the first increment routes by WABA identifier even though it has a single channel.

## Non-negotiables

- A phone number identifies; it never authenticates. Links derive authority from an authenticated web session.
- The language model selects from a closed catalog and never authors statements or computes values presented for approval.
- Every external module implements reconciliation. An unknown outcome is never retried.
- The webhook is signature-gated, acknowledges before processing, and deduplicates by provider message identifier.
