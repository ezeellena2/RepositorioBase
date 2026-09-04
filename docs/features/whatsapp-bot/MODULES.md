# Adding a Module

**Status:** Proposed. Developer guide for [ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md).

A module is a vertical business slice — invoicing, appointments, notifications, logistics — that optionally exposes some of itself to the assistant. This guide is what a module must bring, and what it must never have to touch.

## The honest split

The bot machinery is generic and cheap. The business slice is not.

| Part | Cost |
|---|---|
| Domain entities, migration, Application requests, React screens, tests | **the module** — a full feature |
| Permissions added to the code catalog | a few lines |
| Bot capabilities exposed | an attribute per request |
| Credential schema, if it calls an external system | one JSON Schema |
| External execution and reconciliation, if it writes outside | one interface |

Exposing an existing feature to the assistant is a small fraction of building it. Nothing here makes writing the appointments feature cheaper; it makes reaching it from WhatsApp nearly free once it exists.

## What never changes

Adding the seventh module touches none of this:

- the webhook receiver, its signature check, and the inbox;
- link resolution, the allowlist, and per-message revalidation;
- routing, the capability catalog, and the resolution chain;
- the two-phase confirmation flow and the run state machine;
- outbox delivery, audit, and the Platform panel;
- the credential screen, which is generated from the module's declared schema.

If a module needs a change in any of those, that is a signal the abstraction is wrong, not that the module is special.

## Where module tables live

Module tables live in the same database and the same `DbContext`, under **their own PostgreSQL schema** — `scheduling.appointments`, not `public.appt_appointments`. One connection and one transaction boundary are preserved, module ownership is legible in the schema name, and cross-module joins become visible rather than accidental.

Entity configurations are discovered by assembly scanning, as the template already does. Migrations remain a single stream; a module's migration is named for its module.

Three rules are not negotiable, because they are what makes module seven as safe as module one:

1. **Every tenant-scoped table carries `tenant_id`**, and every association carries it in a composite foreign key, extending IA-REQ-034.
2. **Every Application request declares its permission and tenant requirement**, or the explicit public marker, under IA-REQ-011. Deny-by-default has no module exemption.
3. **Audit classification follows WA-INV-009.** Mutable state derives from `BaseAuditableEntity`; history derives from `BaseEntity` and carries no modification columns.

## Checklist

1. **Domain** — entities and value objects, tenant-scoped, in the module's namespace.
2. **Persistence** — entity configurations under the module's schema; one migration; delete behavior declared on every foreign key; concurrency token on sensitive rows.
3. **Application** — commands and queries implementing the authorized-request contract, returning `Result`/`Result<T>` for expected failures.
4. **Permissions** — added to the code catalog as `resource.action`; they synchronize idempotently under IA-REQ-009.
5. **Capabilities** — decorate the requests to expose. Declare the kind, the audiences, reversibility, and the default confirmation mode:

   ```csharp
   [BotCapability(
       Code               = "sacar_turno",
       Module             = "scheduling",
       Kind               = CapabilityKind.Action,
       Audiences          = Audience.Members | Audience.Contacts,
       Confirmation       = ConfirmationMode.InChat,
       RequiredPermission = "scheduling.book",
       IsReversible       = true)]
   ```

   The tool schema is generated from the request's shape and its validation rules. Write the description for the model to read: routing accuracy is a function of that sentence.
6. **Credentials** — if the module calls an external system, declare a JSON Schema. The configuration screen is generated from it; no new UI.
7. **External execution** — if the module writes outside, implement execution **and** reconciliation. A module that cannot answer "did this already happen" must not declare an irreversible capability.
8. **Health** — a check the panel can call, so an unconfigured module removes its own capabilities from the catalog instead of failing at use.
9. **Web and React** — the module's own screens; the assistant is an additional surface, never the only one.
10. **Tests** — domain, application, functional against real PostgreSQL, the cross-tenant matrix, and routing fixtures for each new capability.

## Audiences are per capability, not per module

One module commonly serves more than one audience, with different capabilities. Appointments is the clearest case:

| Capability | Audience | Who asks |
|---|---|---|
| `ver_agenda_del_dia` | `Members` | staff, on the platform channel |
| `cancelar_turno_de_cliente` | `Members` | staff, on the platform channel |
| `sacar_turno` | `Members` `Contacts` | staff, or a customer on that organization's own channel |
| `cancelar_mi_turno` | `Contacts` | a customer, row-scoped to their own phone |

Same domain tables, same module, different exposure. A `Contacts` capability is authorized by row scope and never by permission, under WA-REQ-053 — it returns what that phone number owns inside that one tenant, and nothing else.

Any capability that issues a fiscal document, moves money, or reads organization-wide data MUST NOT declare `Contacts`. An architecture test enforces it.

## A module is a credential boundary

Group capabilities by the credentials they share, not by the screen they appear on. Voucher verification, taxpayer lookup, invoice issuing, and credit notes all use one certificate against one tax authority, so they are one module with four capabilities — not four modules sharing a secret. One credential row, one lifecycle, one health check, one expiry warning.

Enablement is per capability, so one module can be half-live: its read capabilities enabled for every organization while its write capabilities stay dark.

### Credentials are per tenant, even when the certificate is not

A tax-authority certificate identifies the taxpayer that signs. It is never one certificate for the whole platform, because a certificate cannot issue documents for someone else's tax identification. Two arrangements exist and the credential schema accommodates both:

| | Certificate per organization | Platform certificate plus delegation |
|---|---|---|
| Who generates the signing request | each organization | the platform, once |
| What the organization does | generates a request and uploads a certificate | delegates the web service to the platform's tax identification, in the authority's own administration site |
| Friction | high — a real conversion barrier for an individual | low — one administrative step |
| Credential row content | the encrypted certificate and key | the delegated tax identification; **no secret** |

Both are one row per tenant in `tenant_module_credentials`. The second is what makes self-service onboarding viable for individuals, who will not generate a signing request. The choice is commercial and legal, not technical, and a module may support both — the credential schema declares which arrangement a given row represents.

In Argentina the second arrangement is a **service delegation** performed by the taxpayer in the authority's *Administrador de Relaciones de Clave Fiscal*: a new relationship is created for the electronic-invoicing web service, naming the platform's tax identification as representative. Technically the platform then authenticates with its own certificate and names the represented taxpayer in the request, so the resulting document belongs to that taxpayer — their numbering, their liability — and the platform is only the operator.

### The onboarding sequence

Exactly one step needs the taxpayer. Everything after it is the platform's, and automatable.

| | Step | Who |
|---|---|---|
| 1 | Delegate the services in *Administrador de Relaciones* | **the taxpayer**, with their own fiscal key |
| 2 | Accept the designation | platform — automatable |
| 3 | Create or verify a point of sale for web-service issuance | platform — automatable, **if step 1 included it** |
| 4 | Obtain and cache the first access ticket | platform |
| 5 | Verify with a read-only call | platform |
| 6 | Promote the credential to `Valid`; capabilities appear in the catalog | platform |
| 7 | Notify the person that they can start | platform |

Four properties of that flow shape the product, not just the integration:

- **The delegation must cover more than the invoicing service.** Issuing by web service requires a point of sale configured for that issuance method, distinct from the one a taxpayer uses in the authority's own web portal. Creating it is an administrative action, so unless the taxpayer also delegates point-of-sale administration in step 1, step 3 cannot be performed and every onboarding stalls there. Ask for both services in one delegation, or the person has to come back.
- **The point of sale is module configuration, not a secret.** It is persisted alongside the credential and is required to issue; a credential without one is incomplete and MUST NOT be promoted to `Valid`.
- **A delegation is not effective immediately.** Approval can take up to a day, so onboarding cannot assume the capability works the moment the person finishes. The credential lifecycle of WA-REQ-031 already models this: the row is created `Pending`, the health check promotes it, and the capability is absent from the catalog until then rather than failing at use. Because the channel is already open, step 7 turns that wait into a message rather than an abandonment — the only onboarding where the delay is itself a reason to make contact.
- **It requires a sufficient fiscal-key security level on the taxpayer's side**, which some individuals will not have. Onboarding must detect and explain that, because it is the most likely place a self-service signup stalls.

Do not confuse delegation with issuing *on account and order of a third party*, which is a different commercial figure: there the document comes from the platform's own numbering and carries a legend, and the liability is not the same. The schema described here models delegation only, in which the document is the taxpayer's own — their point of sale, their numbering, their authorization code.

## Worked example: a voucher-review module

Checking whether a supplier's invoice is genuine is the ideal first external module, and worth building before anything that writes.

| | |
|---|---|
| Capabilities | `constatar_comprobante`, `consultar_contribuyente` |
| Kind | `Query` — dispatches to a request type, not a statement |
| Audiences | `Members`, `Personal` |
| Credentials | the tax authority certificate, shared with the issuing capabilities |
| Reconciliation | none — a read has nothing to reconcile |
| External side effects | none |

It exercises everything expensive about an external integration — certificate storage and rotation, the access-ticket cache and its renewal window, the sandbox/production split, the health check that removes capabilities when credentials expire, timeout and retry behaviour — while being **unable to cause any external effect at all**. When it has run in production for a while, the write capabilities of the same module inherit machinery that is already proven.

It also pairs naturally with media intake: the person photographs a supplier invoice, the assistant extracts the fields, and the module verifies them against the authority. That makes media a dependency, not an afterthought.

## Sequencing rule

Do not make an irreversible capability the first exercise of the machinery that protects it. A read-only or reversible capability must have run the credential lifecycle, and where applicable the two-phase flow and the reconciliation path, in production first. This is a gate in [TASKS.md](TASKS.md), not a preference.
